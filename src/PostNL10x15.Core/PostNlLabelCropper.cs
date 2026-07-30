using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PostNL10x15.Core;

public sealed class PostNlLabelCropper(PostNlLabelDetector detector)
{
    public const double LandscapeWidthMillimeters = 150.0;
    public const double LandscapeHeightMillimeters = 100.0;

    public CropResult Crop(string inputPdfPath, string outputPdfPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPdfPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPdfPath);

        string inputPath = Path.GetFullPath(inputPdfPath);
        string outputPath = Path.GetFullPath(outputPdfPath);

        if (string.Equals(
                inputPath,
                outputPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Invoer- en uitvoerbestand mogen niet hetzelfde zijn.",
                nameof(outputPdfPath));
        }

        LabelBounds crop = detector.Detect(inputPath);
        bool landscape = crop.IsLandscape;
        double outputWidthMillimeters = landscape
            ? LandscapeWidthMillimeters
            : LandscapeHeightMillimeters;
        double outputHeightMillimeters = landscape
            ? LandscapeHeightMillimeters
            : LandscapeWidthMillimeters;
        double outputWidth = PdfUnits.MillimetersToPoints(
            outputWidthMillimeters);
        double outputHeight = PdfUnits.MillimetersToPoints(
            outputHeightMillimeters);

        string? outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        using (var output = new PdfDocument())
        {
            output.Info.Title = "PostNL verzendlabel 10x15 cm";
            output.Info.Creator = "PostNL 10x15 Virtual Printer";

            PdfPage page = output.AddPage();
            page.Width = XUnit.FromPoint(outputWidth);
            page.Height = XUnit.FromPoint(outputHeight);

            using XPdfForm form = XPdfForm.FromFile(inputPath);
            double scale = Math.Min(
                outputWidth / crop.Width,
                outputHeight / crop.Height);

            using XGraphics graphics = XGraphics.FromPdfPage(page);
            graphics.DrawImage(
                form,
                -crop.X * scale,
                -crop.Y * scale,
                form.PointWidth * scale,
                form.PointHeight * scale);

            output.Save(outputPath);
        }

        ValidateOutputSize(
            outputPath,
            outputWidthMillimeters,
            outputHeightMillimeters);

        return new CropResult(
            outputPath,
            crop,
            outputWidthMillimeters,
            outputHeightMillimeters);
    }

    private static void ValidateOutputSize(
        string outputPath,
        double expectedWidthMillimeters,
        double expectedHeightMillimeters)
    {
        using var validation = PdfReader.Open(
            outputPath,
            PdfDocumentOpenMode.Import);
        if (validation.PageCount != 1)
        {
            throw new InvalidDataException(
                "De gemaakte label-PDF heeft niet precies een pagina.");
        }

        PdfPage page = validation.Pages[0];
        double actualWidth = PdfUnits.PointsToMillimeters(page.Width.Point);
        double actualHeight = PdfUnits.PointsToMillimeters(page.Height.Point);
        const double toleranceMillimeters = 0.05;

        if (Math.Abs(actualWidth - expectedWidthMillimeters)
                > toleranceMillimeters
            || Math.Abs(actualHeight - expectedHeightMillimeters)
                > toleranceMillimeters)
        {
            throw new InvalidDataException(
                $"Onjuiste uitvoermaat: {actualWidth:F2} x "
                + $"{actualHeight:F2} mm.");
        }
    }
}
