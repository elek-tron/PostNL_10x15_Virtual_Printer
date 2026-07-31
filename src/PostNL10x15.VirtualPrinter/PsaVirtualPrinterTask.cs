using Windows.ApplicationModel;
using Windows.ApplicationModel.Background;
using Windows.Graphics.Printing.Workflow;
using Windows.Storage;
using Windows.Storage.Streams;

namespace PostNL10x15.VirtualPrinter;

/// <summary>
/// Ontvangt de afdrukgegevens van de virtuele printer en geeft het PDF-bestand
/// door aan de bestaande uitsnede- en afdrukverwerker.
/// </summary>
public sealed class PsaVirtualPrinterTask : IBackgroundTask
{
    private static readonly TimeSpan WorkerTimeout =
        TimeSpan.FromSeconds(90);
    private BackgroundTaskDeferral? _taskDeferral;

    public PsaVirtualPrinterTask()
    {
        EndpointLog.Write("PsaVirtualPrinterTask constructed.");
    }

    public void Run(IBackgroundTaskInstance task)
    {
        try
        {
            EndpointLog.Write("PsaVirtualPrinterTask.Run entered.");
            PrintWorkflowVirtualPrinterTriggerDetails? details =
                task.TriggerDetails
                as PrintWorkflowVirtualPrinterTriggerDetails;
            if (details?.VirtualPrinterSession is null)
            {
                EndpointLog.Write("Virtual printer details missing.");
                return;
            }

            _taskDeferral = task.GetDeferral();
            details.VirtualPrinterSession.VirtualPrinterDataAvailable +=
                OnDataAvailable;
            details.VirtualPrinterSession.Start();
            EndpointLog.Write("Virtual printer session started.");
        }
        catch (Exception exception)
        {
            EndpointLog.Write(
                "PsaVirtualPrinterTask.Run exception: " + exception);
            _taskDeferral?.Complete();
            throw;
        }
    }

    private async void OnDataAvailable(
        PrintWorkflowVirtualPrinterSession sender,
        PrintWorkflowVirtualPrinterDataAvailableEventArgs args)
    {
        PrintWorkflowSubmittedStatus status =
            PrintWorkflowSubmittedStatus.Failed;

        try
        {
            EndpointLog.Write(
                "Print data available: "
                + args.SourceContent.ContentType);
            StoredJob storedJob = await StoreJobAsync(args);
            EndpointLog.Write(
                "Print job "
                + storedJob.JobId
                + " stored in inbox as "
                + storedJob.Format
                + ".");
            status = await RunPreviewWorkflowAsync(args, storedJob);
        }
        catch (Exception exception)
        {
            EndpointLog.Write(
                "Print data processing exception: " + exception);
            await WriteEndpointErrorAsync(exception);
        }
        finally
        {
            args.CompleteJob(status);
            EndpointLog.Write("Print job completed with status " + status);
            _taskDeferral?.Complete();
        }
    }

    private static async Task<PrintWorkflowSubmittedStatus>
        RunPreviewWorkflowAsync(
            PrintWorkflowVirtualPrinterDataAvailableEventArgs args,
            StoredJob storedJob)
    {
        string previewDirectory = Directory.CreateDirectory(
            Path.Combine(
                ApplicationData.Current.LocalFolder.Path,
                "Preview")).FullName;
        string activeJobPath = Path.Combine(
            previewDirectory,
            "active-job.txt");
        string decisionPath = JobPath(
            previewDirectory,
            storedJob.JobId,
            ".decision");

        File.WriteAllText(activeJobPath, storedJob.JobId);
        TryDelete(decisionPath);

        try
        {
            await LaunchWorkerAsync(
                "--prepare-preview " + storedJob.JobId);
            await WaitForWorkerAsync(
                previewDirectory,
                storedJob.JobId,
                ".ready");
            EndpointLog.Write(
                "Preview prepared for " + storedJob.JobId + ".");

            bool printRequested;
            if (args.UILauncher.IsUILaunchEnabled())
            {
                PrintWorkflowUICompletionStatus uiStatus =
                    await args.UILauncher.LaunchAndCompleteUIAsync();
                string decision = File.Exists(decisionPath)
                    ? File.ReadAllText(decisionPath).Trim()
                    : "cancel";
                printRequested = string.Equals(
                    decision,
                    "print",
                    StringComparison.OrdinalIgnoreCase);
                EndpointLog.Write(
                    "Preview UI completed with "
                    + uiStatus
                    + "; decision "
                    + decision
                    + ".");
            }
            else
            {
                // Afdrukken vanuit een niet-interactieve Windows-route blijft
                // mogelijk wanneer Windows geen venster mag openen.
                printRequested = true;
                EndpointLog.Write(
                    "Preview UI unavailable; printing automatically.");
            }

            if (!printRequested)
            {
                await LaunchWorkerAsync(
                    "--cancel-preview " + storedJob.JobId);
                await WaitForWorkerAsync(
                    previewDirectory,
                    storedJob.JobId,
                    ".canceled");
                CleanupPreviewFiles(
                    previewDirectory,
                    storedJob.JobId);
                return PrintWorkflowSubmittedStatus.Canceled;
            }

            await LaunchWorkerAsync(
                "--print-preview " + storedJob.JobId);
            await WaitForWorkerAsync(
                previewDirectory,
                storedJob.JobId,
                ".printed");
            CleanupPreviewFiles(
                previewDirectory,
                storedJob.JobId);
            EndpointLog.Write(
                "Previewed label sent to target printer.");
            return PrintWorkflowSubmittedStatus.Succeeded;
        }
        finally
        {
            try
            {
                if (File.Exists(activeJobPath)
                    && string.Equals(
                        File.ReadAllText(activeJobPath).Trim(),
                        storedJob.JobId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(activeJobPath);
                }
            }
            catch
            {
                // Een achtergebleven verwijzing wordt bij de volgende taak
                // overschreven.
            }
        }
    }

    private static async Task LaunchWorkerAsync(string arguments)
    {
        await FullTrustProcessLauncher
            .LaunchFullTrustProcessForCurrentAppWithArgumentsAsync(
                arguments);
        EndpointLog.Write("Worker launched: " + arguments);
    }

    private static async Task WaitForWorkerAsync(
        string previewDirectory,
        string jobId,
        string successExtension)
    {
        string successPath = JobPath(
            previewDirectory,
            jobId,
            successExtension);
        string errorPath = JobPath(
            previewDirectory,
            jobId,
            ".error.txt");
        DateTime deadline = DateTime.UtcNow + WorkerTimeout;

        do
        {
            if (File.Exists(errorPath))
            {
                throw new InvalidDataException(
                    "Het label kon niet worden voorbereid of afgedrukt."
                    + Environment.NewLine
                    + File.ReadAllText(errorPath));
            }

            if (File.Exists(successPath))
            {
                return;
            }

            await Task.Delay(200);
        }
        while (DateTime.UtcNow < deadline);

        throw new TimeoutException(
            "Het voorbereiden of afdrukken van het label duurde te lang.");
    }

    private static string JobPath(
        string previewDirectory,
        string jobId,
        string extension) =>
        Path.Combine(previewDirectory, jobId + extension);

    private static void CleanupPreviewFiles(
        string previewDirectory,
        string jobId)
    {
        foreach (string path in Directory.EnumerateFiles(
                     previewDirectory,
                     jobId + ".*",
                     SearchOption.TopDirectoryOnly))
        {
            TryDelete(path);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Tijdelijke voorbeeldbestanden mogen de afdrukstatus niet
            // veranderen.
        }
    }

    private static async Task<StoredJob> StoreJobAsync(
        PrintWorkflowVirtualPrinterDataAvailableEventArgs args)
    {
        StorageFolder inbox = await ApplicationData.Current.LocalFolder
            .CreateFolderAsync(
                "Inbox",
                CreationCollisionOption.OpenIfExists);
        string jobId = Guid.NewGuid().ToString("N");
        StorageFile temporaryFile = await inbox.CreateFileAsync(
            jobId + ".receiving",
            CreationCollisionOption.FailIfExists);

        try
        {
            using IRandomAccessStream output = await temporaryFile.OpenAsync(
                FileAccessMode.ReadWrite);

            PrintWorkflowPdlSourceContent source = args.SourceContent;
            string extension;
            if (string.Equals(
                    source.ContentType,
                    "application/pdf",
                    StringComparison.OrdinalIgnoreCase))
            {
                using IInputStream input = source.GetInputStream();
                using IOutputStream destination =
                    output.GetOutputStreamAt(0);
                await RandomAccessStream.CopyAsync(input, destination);
                await destination.FlushAsync();
                extension = ".pdf";
            }
            else if (string.Equals(
                         source.ContentType,
                         "application/oxps",
                         StringComparison.OrdinalIgnoreCase))
            {
                PrintWorkflowPdlConverter converter = args.GetPdlConverter(
                    PrintWorkflowPdlConversionType.XpsToPdf);
                using IInputStream input = source.GetInputStream();
                using IOutputStream destination =
                    output.GetOutputStreamAt(0);
                await converter.ConvertPdlAsync(
                    args.GetJobPrintTicket(),
                    input,
                    destination);
                await destination.FlushAsync();
                extension = ".pdf";
            }
            else if (string.Equals(
                         source.ContentType,
                         "application/postscript",
                         StringComparison.OrdinalIgnoreCase))
            {
                using IInputStream input = source.GetInputStream();
                using IOutputStream destination =
                    output.GetOutputStreamAt(0);
                await RandomAccessStream.CopyAsync(input, destination);
                await destination.FlushAsync();
                extension = ".ps";
            }
            else
            {
                throw new InvalidDataException(
                    "Niet ondersteund printerformaat: "
                    + source.ContentType);
            }

            await output.FlushAsync();
            output.Dispose();

            await temporaryFile.RenameAsync(
                jobId + extension,
                NameCollisionOption.FailIfExists);
            return new StoredJob(
                jobId,
                extension.TrimStart('.'));
        }
        catch
        {
            await temporaryFile.DeleteAsync(
                StorageDeleteOption.PermanentDelete);
            throw;
        }
    }

    private sealed record StoredJob(string JobId, string Format);

    private static async Task WriteEndpointErrorAsync(Exception exception)
    {
        try
        {
            StorageFile log = await ApplicationData.Current.LocalFolder
                .CreateFileAsync(
                    "endpoint-errors.log",
                    CreationCollisionOption.OpenIfExists);
            await FileIO.AppendTextAsync(
                log,
                DateTimeOffset.Now.ToString("O")
                + " "
                + exception
                + Environment.NewLine);
        }
        catch
        {
            // De afdrukstatus geeft de fout ook aan Windows door.
        }
    }
}
