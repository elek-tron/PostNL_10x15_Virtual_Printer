using PDFtoImage;
using SkiaSharp;

namespace PostNL10x15.Worker;

public sealed class LabelRasterizer
{
    public SKBitmap Render(string croppedPdfPath, AppSettings settings)
    {
        int width = checked(
            (int)Math.Round(
                settings.LabelWidthMm * settings.RenderDotsPerMillimeter));
        int height = checked(
            (int)Math.Round(
                settings.LabelHeightMm * settings.RenderDotsPerMillimeter));

        byte[] pdfBytes = File.ReadAllBytes(croppedPdfPath);
        var renderOptions = new RenderOptions(
            Dpi: settings.RenderDpi,
            Width: width,
            Height: height);

        SKBitmap bitmap = Conversion.ToImage(
            pdfBytes,
            page: 0,
            options: renderOptions);

        if (bitmap.Width != width || bitmap.Height != height)
        {
            bitmap.Dispose();
            throw new InvalidDataException(
                $"Onjuiste rastermaat: verwacht {width}x{height} pixels.");
        }

        return bitmap;
    }

    public void SavePng(
        string croppedPdfPath,
        string pngPath,
        AppSettings settings)
    {
        using SKBitmap bitmap = Render(croppedPdfPath, settings);
        string fullPath = Path.GetFullPath(pngPath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using FileStream output = File.Create(fullPath);
        if (!bitmap.Encode(output, SKEncodedImageFormat.Png, 100))
        {
            throw new InvalidDataException("Het PNG-testbeeld kon niet worden gemaakt.");
        }
    }
}

