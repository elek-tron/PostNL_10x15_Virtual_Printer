using PostNL10x15.Core;

namespace PostNL10x15.Worker;

internal sealed class PreviewJobProcessor(
    PostNlLabelCropper cropper,
    LabelRasterizer rasterizer,
    WindowsLabelPrinter printer,
    PostScriptConverter postScriptConverter)
{
    public int Prepare(string jobId, AppSettings settings)
    {
        ValidateJobId(jobId);
        PreviewPaths paths = PreviewPaths.For(jobId);
        Directory.CreateDirectory(paths.PreviewDirectory);
        ClearResultFiles(paths);

        string inputPath = FindInput(paths);
        string? convertedPath = null;
        string sourcePdf = inputPath;

        try
        {
            if (Path.GetExtension(inputPath).Equals(
                    ".ps",
                    StringComparison.OrdinalIgnoreCase))
            {
                convertedPath = paths.ConvertedPdf;
                postScriptConverter.ConvertToPdf(inputPath, convertedPath);
                sourcePdf = convertedPath;
            }

            cropper.Crop(sourcePdf, paths.CroppedPdf);
            rasterizer.SavePng(paths.CroppedPdf, paths.PreviewPng, settings);
            File.WriteAllText(paths.PrinterName, settings.TargetPrinter);
            File.WriteAllText(
                paths.Ready,
                DateTimeOffset.Now.ToString("O"));
            return 0;
        }
        catch (Exception exception)
        {
            WriteError(paths, exception);
            return 1;
        }
        finally
        {
            TryDelete(convertedPath);
        }
    }

    public int Print(string jobId, AppSettings settings)
    {
        ValidateJobId(jobId);
        PreviewPaths paths = PreviewPaths.For(jobId);

        try
        {
            if (!File.Exists(paths.CroppedPdf))
            {
                throw new FileNotFoundException(
                    "Het voorbereide label ontbreekt.",
                    paths.CroppedPdf);
            }

            printer.Print(
                paths.CroppedPdf,
                settings.TargetPrinter,
                settings);
            ArchiveInput(paths, "Processed");
            File.WriteAllText(
                paths.Printed,
                DateTimeOffset.Now.ToString("O"));
            return 0;
        }
        catch (Exception exception)
        {
            WriteError(paths, exception);
            return 1;
        }
    }

    public int Cancel(string jobId)
    {
        ValidateJobId(jobId);
        PreviewPaths paths = PreviewPaths.For(jobId);

        try
        {
            ArchiveInput(paths, "Canceled");
            File.WriteAllText(
                paths.Canceled,
                DateTimeOffset.Now.ToString("O"));
            return 0;
        }
        catch (Exception exception)
        {
            WriteError(paths, exception);
            return 1;
        }
    }

    private static string FindInput(PreviewPaths paths)
    {
        foreach (string extension in new[] { ".pdf", ".ps" })
        {
            string candidate = Path.Combine(
                paths.InboxDirectory,
                paths.JobId + extension);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            "De ontvangen afdrukgegevens ontbreken.");
    }

    private static void ArchiveInput(
        PreviewPaths paths,
        string destinationFolderName)
    {
        string inputPath = FindInput(paths);
        string destinationDirectory = Directory.CreateDirectory(
            Path.Combine(
                paths.LocalState,
                destinationFolderName)).FullName;
        string destinationPath = UniquePath(
            destinationDirectory,
            Path.GetFileName(inputPath));
        File.Move(inputPath, destinationPath);
    }

    private static void ClearResultFiles(PreviewPaths paths)
    {
        foreach (string path in new[]
                 {
                     paths.CroppedPdf,
                     paths.ConvertedPdf,
                     paths.PreviewPng,
                     paths.PrinterName,
                     paths.Ready,
                     paths.Decision,
                     paths.Printed,
                     paths.Canceled,
                     paths.Error
                 })
        {
            TryDelete(path);
        }
    }

    private static void WriteError(
        PreviewPaths paths,
        Exception exception)
    {
        Directory.CreateDirectory(paths.PreviewDirectory);
        File.WriteAllText(
            paths.Error,
            DateTimeOffset.Now.ToString("O")
            + Environment.NewLine
            + exception);
    }

    private static string UniquePath(
        string directory,
        string fileName)
    {
        string candidate = Path.Combine(directory, fileName);
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        return Path.Combine(
            directory,
            Path.GetFileNameWithoutExtension(fileName)
            + "-"
            + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fff")
            + Path.GetExtension(fileName));
    }

    private static void ValidateJobId(string jobId)
    {
        if (jobId.Length != 32
            || jobId.Any(character =>
                !char.IsAsciiHexDigit(character)))
        {
            throw new ArgumentException(
                "Ongeldig afdruknummer.",
                nameof(jobId));
        }
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Een volgende poging kan het tijdelijke bestand opruimen.
        }
        catch (UnauthorizedAccessException)
        {
            // Een volgende poging kan het tijdelijke bestand opruimen.
        }
    }

    private sealed record PreviewPaths(
        string JobId,
        string LocalState,
        string InboxDirectory,
        string PreviewDirectory)
    {
        public string CroppedPdf =>
            Path.Combine(PreviewDirectory, JobId + ".cropped.pdf");

        public string ConvertedPdf =>
            Path.Combine(PreviewDirectory, JobId + ".converted.pdf");

        public string PreviewPng =>
            Path.Combine(PreviewDirectory, JobId + ".png");

        public string PrinterName =>
            Path.Combine(PreviewDirectory, JobId + ".printer.txt");

        public string Ready =>
            Path.Combine(PreviewDirectory, JobId + ".ready");

        public string Decision =>
            Path.Combine(PreviewDirectory, JobId + ".decision");

        public string Printed =>
            Path.Combine(PreviewDirectory, JobId + ".printed");

        public string Canceled =>
            Path.Combine(PreviewDirectory, JobId + ".canceled");

        public string Error =>
            Path.Combine(PreviewDirectory, JobId + ".error.txt");

        public static PreviewPaths For(string jobId)
        {
            string localState = PackageIdentity.GetLocalStatePath();
            return new PreviewPaths(
                jobId,
                localState,
                Directory.CreateDirectory(
                    Path.Combine(localState, "Inbox")).FullName,
                Directory.CreateDirectory(
                    Path.Combine(localState, "Preview")).FullName);
        }
    }
}
