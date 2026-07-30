using PostNL10x15.Core;

namespace PostNL10x15.Worker;

internal sealed class InboxProcessor(
    PostNlLabelCropper cropper,
    WindowsLabelPrinter printer,
    PostScriptConverter postScriptConverter)
{
    private const string MutexName =
        @"Local\PostNL10x15.VirtualPrinter.Inbox";

    public int Process(AppSettings settings)
    {
        using var mutex = new Mutex(false, MutexName);
        bool ownsMutex;
        try
        {
            ownsMutex = mutex.WaitOne(TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            ownsMutex = true;
        }

        if (!ownsMutex)
        {
            return 0;
        }

        try
        {
            return ProcessExclusive(settings);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private int ProcessExclusive(AppSettings settings)
    {
        string localState = PackageIdentity.GetLocalStatePath();
        string inbox = Directory.CreateDirectory(
            Path.Combine(localState, "Inbox")).FullName;
        string processed = Directory.CreateDirectory(
            Path.Combine(localState, "Processed")).FullName;
        string failed = Directory.CreateDirectory(
            Path.Combine(localState, "Failed")).FullName;
        int failures = 0;

        foreach (string inputPath in Directory
                     .EnumerateFiles(inbox)
                     .Where(path =>
                         Path.GetExtension(path).Equals(
                             ".pdf",
                             StringComparison.OrdinalIgnoreCase)
                         || Path.GetExtension(path).Equals(
                             ".ps",
                             StringComparison.OrdinalIgnoreCase))
                     .OrderBy(File.GetCreationTimeUtc))
        {
            try
            {
                ProcessOne(inputPath, processed, settings);
            }
            catch (Exception exception)
            {
                failures++;
                MoveFailed(inputPath, failed, exception);
            }
        }

        return failures == 0 ? 0 : 1;
    }

    private void ProcessOne(
        string inputPath,
        string processedDirectory,
        AppSettings settings)
    {
        WaitUntilReady(inputPath);

        string? convertedPath = null;
        string sourcePdf = inputPath;
        if (Path.GetExtension(inputPath).Equals(
                ".ps",
                StringComparison.OrdinalIgnoreCase))
        {
            convertedPath = Path.Combine(
                Path.GetDirectoryName(inputPath)!,
                Path.GetFileNameWithoutExtension(inputPath)
                + ".converted.pdf");
            postScriptConverter.ConvertToPdf(inputPath, convertedPath);
            sourcePdf = convertedPath;
        }

        string croppedPath = Path.Combine(
            Path.GetDirectoryName(inputPath)!,
            Path.GetFileNameWithoutExtension(inputPath)
            + ".cropped.pdf");

        try
        {
            cropper.Crop(sourcePdf, croppedPath);
            printer.Print(
                croppedPath,
                settings.TargetPrinter,
                settings);

            string archivedPath = UniquePath(
                processedDirectory,
                Path.GetFileName(inputPath));
            File.Move(inputPath, archivedPath);
        }
        finally
        {
            if (File.Exists(croppedPath))
            {
                File.Delete(croppedPath);
            }

            if (convertedPath is not null
                && File.Exists(convertedPath))
            {
                File.Delete(convertedPath);
            }
        }
    }

    private static void WaitUntilReady(string inputPath)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        IOException? lastException = null;

        do
        {
            try
            {
                using var stream = new FileStream(
                    inputPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.None);
                return;
            }
            catch (IOException exception)
            {
                lastException = exception;
                Thread.Sleep(100);
            }
        }
        while (DateTime.UtcNow < deadline);

        throw new IOException(
            "Windows hield de ontvangen afdrukgegevens langer dan "
            + "vijf seconden bezet.",
            lastException);
    }

    private static void MoveFailed(
        string inputPath,
        string failedDirectory,
        Exception exception)
    {
        string failedPath = UniquePath(
            failedDirectory,
            Path.GetFileName(inputPath));
        if (File.Exists(inputPath))
        {
            File.Move(inputPath, failedPath);
        }

        File.WriteAllText(
            failedPath + ".error.txt",
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
}
