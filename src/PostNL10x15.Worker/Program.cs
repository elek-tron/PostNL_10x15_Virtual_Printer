using PostNL10x15.Core;

namespace PostNL10x15.Worker;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        WriteTrace(
            "Worker gestart. Argumenten: "
            + (args.Length == 0
                ? "(geen)"
                : string.Join(" ", args)));
        string[] commandArguments = NormalizeArguments(args);

        try
        {
            int result = Run(commandArguments);
            WriteTrace("Worker gestopt met code " + result + ".");
            return result;
        }
        catch (Exception exception)
        {
            WriteTrace("Worker-fout: " + exception);
            Console.Error.WriteLine($"Fout: {exception.Message}");
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintHelp();
            return 0;
        }

        var detector = new PostNlLabelDetector();
        var cropper = new PostNlLabelCropper(detector);
        var rasterizer = new LabelRasterizer();
        var printer = new WindowsLabelPrinter(rasterizer);
        var postScriptConverter = new PostScriptConverter();
        var inboxProcessor = new InboxProcessor(
            cropper,
            printer,
            postScriptConverter);
        var previewJobProcessor = new PreviewJobProcessor(
            cropper,
            rasterizer,
            printer,
            postScriptConverter);
        AppSettings settings = AppSettings.Load();

        switch (args[0].ToLowerInvariant())
        {
            case "printers":
                PrintPrinters(settings);
                return 0;

            case "process-inbox":
            case "--process-inbox":
                return inboxProcessor.Process(settings);

            case "prepare-preview":
            case "--prepare-preview":
                RequireArgumentCount(
                    args,
                    2,
                    "prepare-preview <afdruknummer>");
                return previewJobProcessor.Prepare(args[1], settings);

            case "print-preview":
            case "--print-preview":
                RequireArgumentCount(
                    args,
                    2,
                    "print-preview <afdruknummer>");
                return previewJobProcessor.Print(args[1], settings);

            case "cancel-preview":
            case "--cancel-preview":
                RequireArgumentCount(
                    args,
                    2,
                    "cancel-preview <afdruknummer>");
                return previewJobProcessor.Cancel(args[1]);

            case "inspect":
                RequireArgumentCount(args, 2, "inspect <invoer.pdf>");
                PrintBounds(detector.Detect(args[1]));
                return 0;

            case "crop":
                RequireArgumentCount(
                    args,
                    3,
                    "crop <invoer.pdf> <uitvoer.pdf>");
                PrintCropResult(cropper.Crop(args[1], args[2]));
                return 0;

            case "preview":
                RequireArgumentCount(
                    args,
                    3,
                    "preview <invoer.pdf> <uitvoer.png>");
                CreatePreview(
                    cropper,
                    rasterizer,
                    settings,
                    args[1],
                    args[2]);
                return 0;

            case "print":
                RequireArgumentCount(
                    args,
                    2,
                    "print <invoer.pdf> [--printer <naam>]");
                string printerName = ReadPrinterName(args, settings);
                PrintLabel(
                    cropper,
                    printer,
                    settings,
                    args[1],
                    printerName);
                return 0;

            default:
                if (args.Length == 1
                    && string.Equals(
                        Path.GetExtension(args[0]),
                        ".pdf",
                        StringComparison.OrdinalIgnoreCase))
                {
                    string output = Path.Combine(
                        Path.GetDirectoryName(Path.GetFullPath(args[0]))!,
                        Path.GetFileNameWithoutExtension(args[0])
                        + ".10x15.pdf");
                    PrintCropResult(cropper.Crop(args[0], output));
                    return 0;
                }

                throw new ArgumentException(
                    $"Onbekend commando: {args[0]}. Gebruik --help.");
        }
    }

    private static void CreatePreview(
        PostNlLabelCropper cropper,
        LabelRasterizer rasterizer,
        AppSettings settings,
        string inputPath,
        string pngPath)
    {
        string temporaryPdf = TemporaryPdfPath();
        try
        {
            CropResult result = cropper.Crop(inputPath, temporaryPdf);
            rasterizer.SavePng(temporaryPdf, pngPath, settings);
            PrintCropResult(result);
            Console.WriteLine(
                $"Testbeeld: {Path.GetFullPath(pngPath)} "
                + $"({settings.LabelWidthMm * settings.RenderDotsPerMillimeter:F0}"
                + "x"
                + $"{settings.LabelHeightMm * settings.RenderDotsPerMillimeter:F0}"
                + " pixels)");
        }
        finally
        {
            TryDelete(temporaryPdf);
        }
    }

    private static void PrintLabel(
        PostNlLabelCropper cropper,
        WindowsLabelPrinter printer,
        AppSettings settings,
        string inputPath,
        string printerName)
    {
        string temporaryPdf = TemporaryPdfPath();
        try
        {
            CropResult result = cropper.Crop(inputPath, temporaryPdf);
            PrintCropResult(result);
            Console.WriteLine($"Printopdracht naar: {printerName}");
            printer.Print(temporaryPdf, printerName, settings);
            Console.WriteLine("Printopdracht is aan Windows doorgegeven.");
        }
        finally
        {
            TryDelete(temporaryPdf);
        }
    }

    private static string ReadPrinterName(
        IReadOnlyList<string> args,
        AppSettings settings)
    {
        if (args.Count == 2)
        {
            return settings.TargetPrinter;
        }

        if (args.Count == 4
            && string.Equals(
                args[2],
                "--printer",
                StringComparison.OrdinalIgnoreCase))
        {
            return args[3];
        }

        throw new ArgumentException(
            "Gebruik: print <invoer.pdf> [--printer <naam>]");
    }

    private static void PrintPrinters(AppSettings settings)
    {
        Console.WriteLine("Geinstalleerde printers:");
        foreach (string name in WindowsLabelPrinter.InstalledPrinterNames())
        {
            string marker = string.Equals(
                    name,
                    settings.TargetPrinter,
                    StringComparison.OrdinalIgnoreCase)
                ? " (ingesteld voor test)"
                : string.Empty;
            Console.WriteLine($"- {name}{marker}");
        }

        Console.WriteLine();
        Console.WriteLine($"Zebra-zoeknaam voor later: {settings.ZebraPrinterHint}");
    }

    private static void PrintBounds(LabelBounds bounds)
    {
        Console.WriteLine("Label gevonden zonder kalibratie:");
        Console.WriteLine(
            $"- positie: x={bounds.X:F2}, y={bounds.Y:F2} PDF-punten");
        Console.WriteLine(
            $"- bronmaat: {bounds.WidthMillimeters:F2} x "
            + $"{bounds.HeightMillimeters:F2} mm");
        Console.WriteLine(
            $"- pagina: {PdfUnits.PointsToMillimeters(bounds.PageWidth):F2} x "
            + $"{PdfUnits.PointsToMillimeters(bounds.PageHeight):F2} mm");
    }

    private static void PrintCropResult(CropResult result)
    {
        PrintBounds(result.SourceBounds);
        Console.WriteLine(
            $"- uitvoer: {result.OutputWidthMillimeters:F2} x "
            + $"{result.OutputHeightMillimeters:F2} mm");
        Console.WriteLine($"Bestand: {result.OutputPath}");
    }

    private static string TemporaryPdfPath() =>
        Path.Combine(
            Path.GetTempPath(),
            $"PostNL10x15-{Guid.NewGuid():N}.pdf");

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Tijdelijke bestanden worden door Windows later opgeruimd.
        }
        catch (UnauthorizedAccessException)
        {
            // Tijdelijke bestanden worden door Windows later opgeruimd.
        }
    }

    private static bool IsHelp(string value) =>
        value is "-h" or "--help" or "/?";

    private static string[] NormalizeArguments(string[] args)
    {
        int commandIndex = Array.FindIndex(
            args,
            value =>
                IsHelp(value)
                || value.Equals(
                    "printers",
                    StringComparison.OrdinalIgnoreCase)
                || value.Equals(
                    "process-inbox",
                    StringComparison.OrdinalIgnoreCase)
                || value.Equals(
                    "--process-inbox",
                    StringComparison.OrdinalIgnoreCase)
                || value.Equals(
                    "prepare-preview",
                    StringComparison.OrdinalIgnoreCase)
                || value.Equals(
                    "--prepare-preview",
                    StringComparison.OrdinalIgnoreCase)
                || value.Equals(
                    "print-preview",
                    StringComparison.OrdinalIgnoreCase)
                || value.Equals(
                    "--print-preview",
                    StringComparison.OrdinalIgnoreCase)
                || value.Equals(
                    "cancel-preview",
                    StringComparison.OrdinalIgnoreCase)
                || value.Equals(
                    "--cancel-preview",
                    StringComparison.OrdinalIgnoreCase)
                || value.Equals(
                    "inspect",
                    StringComparison.OrdinalIgnoreCase)
                || value.Equals(
                    "crop",
                    StringComparison.OrdinalIgnoreCase)
                || value.Equals(
                    "preview",
                    StringComparison.OrdinalIgnoreCase)
                || value.Equals(
                    "print",
                    StringComparison.OrdinalIgnoreCase)
                || value.EndsWith(
                    ".pdf",
                    StringComparison.OrdinalIgnoreCase));

        return commandIndex <= 0 ? args : args[commandIndex..];
    }

    private static void WriteTrace(string message)
    {
        try
        {
            string localState = PackageIdentity.GetLocalStatePath();
            Directory.CreateDirectory(localState);
            File.AppendAllText(
                Path.Combine(localState, "worker-trace.log"),
                DateTimeOffset.Now.ToString("O")
                + " "
                + message
                + Environment.NewLine);
        }
        catch
        {
            // De worker mag nooit falen doordat diagnostiek niet schrijfbaar is.
        }
    }

    private static void RequireArgumentCount(
        IReadOnlyCollection<string> args,
        int minimum,
        string usage)
    {
        if (args.Count < minimum)
        {
            throw new ArgumentException($"Gebruik: {usage}");
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("PostNL 10x15 - automatische labeluitsnede");
        Console.WriteLine();
        Console.WriteLine("Commando's:");
        Console.WriteLine("  printers");
        Console.WriteLine("  process-inbox");
        Console.WriteLine("  prepare-preview <afdruknummer>");
        Console.WriteLine("  print-preview <afdruknummer>");
        Console.WriteLine("  cancel-preview <afdruknummer>");
        Console.WriteLine("  inspect <invoer.pdf>");
        Console.WriteLine("  crop <invoer.pdf> <uitvoer.pdf>");
        Console.WriteLine("  preview <invoer.pdf> <uitvoer.png>");
        Console.WriteLine(
            "  print <invoer.pdf> [--printer <naam>]");
        Console.WriteLine();
        Console.WriteLine(
            "Alleen 'print' start een Windows-printopdracht.");
    }
}
