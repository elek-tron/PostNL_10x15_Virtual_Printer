using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf.IO;

namespace PostNL10x15.Core;

public sealed class PostNlLabelDetector
{
    private const double MinimumLabelAreaFraction = 0.10;
    private const double MaximumLabelAreaFraction = 0.90;
    private const double MinimumAspectRatio = 1.45;
    private const double MaximumAspectRatio = 1.55;

    public LabelBounds Detect(string pdfPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);
        string fullPath = Path.GetFullPath(pdfPath);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("De PDF is niet gevonden.", fullPath);
        }

        using var document = PdfReader.Open(fullPath, PdfDocumentOpenMode.Import);
        if (document.PageCount == 0)
        {
            throw new LabelDetectionException("De PDF bevat geen pagina's.");
        }

        var page = document.Pages[0];
        double pageWidth = page.Width.Point;
        double pageHeight = page.Height.Point;
        double pageArea = pageWidth * pageHeight;
        CSequence content = ContentReader.ReadContent(page);

        LabelBounds? pendingRectangle = null;
        LabelBounds? largestLabelRectangle = null;
        double largestArea = 0;
        var transformations = new Stack<PdfMatrix>();
        PdfMatrix transformation = PdfMatrix.Identity;
        List<PdfPoint>? pendingPath = null;
        bool pathClosed = false;

        foreach (COperator operation in OperatorsIn(content))
        {
            if (operation.Name == "q")
            {
                transformations.Push(transformation);
                pendingPath = null;
                pathClosed = false;
                continue;
            }

            if (operation.Name == "Q")
            {
                transformation = transformations.Count == 0
                    ? PdfMatrix.Identity
                    : transformations.Pop();
                pendingRectangle = null;
                pendingPath = null;
                pathClosed = false;
                continue;
            }

            if (operation.Name == "cm" && operation.Operands.Count >= 6)
            {
                var added = new PdfMatrix(
                    PdfNumber(operation.Operands[0]),
                    PdfNumber(operation.Operands[1]),
                    PdfNumber(operation.Operands[2]),
                    PdfNumber(operation.Operands[3]),
                    PdfNumber(operation.Operands[4]),
                    PdfNumber(operation.Operands[5]));
                transformation = transformation.Then(added);
                pendingRectangle = null;
                pendingPath = null;
                pathClosed = false;
                continue;
            }

            if (operation.Name == "re" && operation.Operands.Count >= 4)
            {
                double x = PdfNumber(operation.Operands[0]);
                double y = PdfNumber(operation.Operands[1]);
                double width = PdfNumber(operation.Operands[2]);
                double height = PdfNumber(operation.Operands[3]);
                PdfPoint[] corners =
                [
                    transformation.Transform(x, y),
                    transformation.Transform(x + width, y),
                    transformation.Transform(x, y + height),
                    transformation.Transform(x + width, y + height)
                ];
                double left = corners.Min(point => point.X);
                double right = corners.Max(point => point.X);
                double bottom = corners.Min(point => point.Y);
                double upper = corners.Max(point => point.Y);
                double normalizedWidth = right - left;
                double normalizedHeight = upper - bottom;
                double top = pageHeight - bottom - normalizedHeight;

                pendingRectangle = new LabelBounds(
                    left,
                    top,
                    normalizedWidth,
                    normalizedHeight,
                    pageWidth,
                    pageHeight);
                pendingPath = null;
                pathClosed = false;
                continue;
            }

            if (operation.Name == "m" && operation.Operands.Count >= 2)
            {
                pendingRectangle = null;
                pendingPath =
                [
                    transformation.Transform(
                        PdfNumber(operation.Operands[0]),
                        PdfNumber(operation.Operands[1]))
                ];
                pathClosed = false;
                continue;
            }

            if (operation.Name == "l"
                && operation.Operands.Count >= 2
                && pendingPath is not null)
            {
                pendingPath.Add(
                    transformation.Transform(
                        PdfNumber(operation.Operands[0]),
                        PdfNumber(operation.Operands[1])));
                continue;
            }

            if (operation.Name == "h" && pendingPath is not null)
            {
                pathClosed = true;
                continue;
            }

            if (IsStrokeOperator(operation.Name))
            {
                pendingRectangle ??= PathRectangle(
                    pendingPath,
                    pathClosed,
                    pageWidth,
                    pageHeight);

                if (pendingRectangle is null)
                {
                    pendingPath = null;
                    pathClosed = false;
                    continue;
                }

                double shortSide = Math.Min(
                    pendingRectangle.Width,
                    pendingRectangle.Height);
                double longSide = Math.Max(
                    pendingRectangle.Width,
                    pendingRectangle.Height);
                double ratio = longSide / shortSide;
                double area = pendingRectangle.Width * pendingRectangle.Height;

                if (ratio is >= MinimumAspectRatio and <= MaximumAspectRatio
                    && area >= pageArea * MinimumLabelAreaFraction
                    && area < pageArea * MaximumLabelAreaFraction
                    && area > largestArea)
                {
                    largestArea = area;
                    largestLabelRectangle = pendingRectangle;
                }

                pendingRectangle = null;
                pendingPath = null;
                pathClosed = false;
                continue;
            }

            pendingRectangle = null;
        }

        return largestLabelRectangle
            ?? throw new LabelDetectionException(
                "Geen duidelijke rechthoekige 10x15-labelrand gevonden. "
                + "Er wordt niet gegokt of gekalibreerd; de PDF is niet afgedrukt.");
    }

    private static IEnumerable<COperator> OperatorsIn(CSequence sequence)
    {
        foreach (CObject item in sequence)
        {
            if (item is COperator operation)
            {
                yield return operation;
            }
            else if (item is CSequence nested)
            {
                foreach (COperator nestedOperation in OperatorsIn(nested))
                {
                    yield return nestedOperation;
                }
            }
        }
    }

    private static double PdfNumber(CObject value) =>
        value switch
        {
            CInteger integer => integer.Value,
            CReal real => real.Value,
            _ => throw new InvalidDataException(
                "Onverwacht getal in de PDF-inhoud.")
        };

    private static bool IsStrokeOperator(string operatorName) =>
        operatorName is "S" or "s" or "B" or "B*" or "b" or "b*";

    private static LabelBounds? PathRectangle(
        IReadOnlyList<PdfPoint>? path,
        bool closed,
        double pageWidth,
        double pageHeight)
    {
        if (!closed || path is null || path.Count is < 4 or > 5)
        {
            return null;
        }

        var points = path.ToList();
        if (points.Count == 5
            && NearlyEqual(points[0].X, points[^1].X)
            && NearlyEqual(points[0].Y, points[^1].Y))
        {
            points.RemoveAt(points.Count - 1);
        }

        if (points.Count != 4)
        {
            return null;
        }

        for (int index = 0; index < points.Count; index++)
        {
            PdfPoint current = points[index];
            PdfPoint next = points[(index + 1) % points.Count];
            bool horizontal = NearlyEqual(current.Y, next.Y);
            bool vertical = NearlyEqual(current.X, next.X);
            if (!horizontal && !vertical)
            {
                return null;
            }
        }

        double left = points.Min(point => point.X);
        double right = points.Max(point => point.X);
        double bottom = points.Min(point => point.Y);
        double top = points.Max(point => point.Y);
        if (right <= left || top <= bottom)
        {
            return null;
        }

        return new LabelBounds(
            left,
            pageHeight - top,
            right - left,
            top - bottom,
            pageWidth,
            pageHeight);
    }

    private static bool NearlyEqual(double first, double second) =>
        Math.Abs(first - second) <= 0.5;

    private readonly record struct PdfPoint(double X, double Y);

    private readonly record struct PdfMatrix(
        double A,
        double B,
        double C,
        double D,
        double E,
        double F)
    {
        public static PdfMatrix Identity { get; } =
            new(1, 0, 0, 1, 0, 0);

        public PdfPoint Transform(double x, double y) =>
            new(
                A * x + C * y + E,
                B * x + D * y + F);

        public PdfMatrix Then(PdfMatrix added) =>
            new(
                A * added.A + C * added.B,
                B * added.A + D * added.B,
                A * added.C + C * added.D,
                B * added.C + D * added.D,
                A * added.E + C * added.F + E,
                B * added.E + D * added.F + F);
    }
}
