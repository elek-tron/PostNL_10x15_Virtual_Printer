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
            string storedFormat = await StoreJobAsync(args);
            EndpointLog.Write(
                "Print job stored in inbox as " + storedFormat + ".");
            await FullTrustProcessLauncher
                .LaunchFullTrustProcessForCurrentAppAsync("ProcessInbox");
            EndpointLog.Write("Worker launched.");
            status = PrintWorkflowSubmittedStatus.Succeeded;
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

    private static async Task<string> StoreJobAsync(
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
            return extension.TrimStart('.');
        }
        catch
        {
            await temporaryFile.DeleteAsync(
                StorageDeleteOption.PermanentDelete);
            throw;
        }
    }

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
