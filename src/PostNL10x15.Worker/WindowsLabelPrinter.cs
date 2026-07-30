using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using SkiaSharp;

namespace PostNL10x15.Worker;

public sealed class WindowsLabelPrinter(LabelRasterizer rasterizer)
{
    public static IReadOnlyList<string> InstalledPrinterNames() =>
        PrinterSettings.InstalledPrinters
            .Cast<string>()
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

    public void Print(
        string croppedPdfPath,
        string printerName,
        AppSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);

        if (!InstalledPrinterNames().Contains(
                printerName,
                StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Printer '{printerName}' is niet geinstalleerd.");
        }

        using SKBitmap rendered = rasterizer.Render(croppedPdfPath, settings);
        using var encoded = new MemoryStream();
        if (!rendered.Encode(encoded, SKEncodedImageFormat.Png, 100))
        {
            throw new InvalidDataException(
                "Het label kon niet voor Windows worden gerenderd.");
        }

        encoded.Position = 0;
        using Image decoded = Image.FromStream(encoded);
        using var printableImage = new Bitmap(decoded);
        printableImage.SetResolution(settings.RenderDpi, settings.RenderDpi);

        using var document = new PrintDocument
        {
            DocumentName = $"PostNL 10x15 - {Path.GetFileName(croppedPdfPath)}",
            OriginAtMargins = false,
            PrintController = new StandardPrintController()
        };

        document.PrinterSettings.PrinterName = printerName;
        document.PrinterSettings.Copies = 1;
        document.DefaultPageSettings.Color = false;
        document.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
        // The ZD220 feeds the 150 mm direction through a 100 mm-wide
        // printhead. Windows rotates the 150 x 100 mm artwork onto that
        // physical 100 x 150 mm sheet.
        document.DefaultPageSettings.Landscape = true;
        document.DefaultPageSettings.PaperSize = new PaperSize(
            "PostNL 100 x 150 mm",
            MillimetersToHundredthsOfInch(settings.LabelHeightMm),
            MillimetersToHundredthsOfInch(settings.LabelWidthMm));
        document.DefaultPageSettings.PrinterResolution =
            new PrinterResolution
            {
                Kind = PrinterResolutionKind.Custom,
                X = settings.RenderDpi,
                Y = settings.RenderDpi
            };

        document.PrintPage += (_, eventArgs) =>
        {
            Graphics graphics = eventArgs.Graphics
                ?? throw new InvalidOperationException(
                    "Windows leverde geen tekenoppervlak voor de printer.");
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.CompositingQuality =
                CompositingQuality.HighSpeed;
            graphics.InterpolationMode =
                InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.SmoothingMode = SmoothingMode.None;

            var target = new RectangleF(
                0,
                0,
                (float)(settings.LabelWidthMm / 25.4 * 100.0),
                (float)(settings.LabelHeightMm / 25.4 * 100.0));
            graphics.DrawImage(printableImage, target);
            eventArgs.HasMorePages = false;
        };

        document.Print();
    }

    private static int MillimetersToHundredthsOfInch(double millimeters) =>
        checked((int)Math.Round(
            millimeters / 25.4 * 100.0,
            MidpointRounding.AwayFromZero));
}
