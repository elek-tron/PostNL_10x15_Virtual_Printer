using System;
using System.IO;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Background;
using Windows.Graphics.Printing.Workflow;
using Windows.Storage;
using Windows.Storage.Streams;

namespace PostNL10x15.VirtualEndpoint
{
    public sealed class PrintWorkflowBackgroundTask : IBackgroundTask
    {
        private BackgroundTaskDeferral _taskDeferral;

        public void Run(IBackgroundTaskInstance taskInstance)
        {
            var details =
                taskInstance.TriggerDetails
                as PrintWorkflowVirtualPrinterTriggerDetails;
            if (details == null)
            {
                return;
            }

            _taskDeferral = taskInstance.GetDeferral();
            PrintWorkflowVirtualPrinterSession session =
                details.VirtualPrinterSession;
            session.VirtualPrinterDataAvailable += OnDataAvailable;
            session.Start();
        }

        private async void OnDataAvailable(
            PrintWorkflowVirtualPrinterSession sender,
            PrintWorkflowVirtualPrinterDataAvailableEventArgs args)
        {
            PrintWorkflowSubmittedStatus status =
                PrintWorkflowSubmittedStatus.Failed;

            try
            {
                await StoreJobAsPdfAsync(args);
                await FullTrustProcessLauncher
                    .LaunchFullTrustProcessForCurrentAppAsync(
                        "ProcessInbox");
                status = PrintWorkflowSubmittedStatus.Succeeded;
            }
            catch (Exception exception)
            {
                await WriteEndpointErrorAsync(exception);
            }
            finally
            {
                args.CompleteJob(status);
                _taskDeferral?.Complete();
            }
        }

        private static async Task StoreJobAsPdfAsync(
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

            using (IRandomAccessStream output = await temporaryFile.OpenAsync(
                       FileAccessMode.ReadWrite))
            {
                PrintWorkflowPdlSourceContent source = args.SourceContent;
                if (string.Equals(
                    source.ContentType,
                    "application/pdf",
                    StringComparison.OrdinalIgnoreCase))
                {
                    await RandomAccessStream.CopyAsync(
                        source.GetInputStream(),
                        output.GetOutputStreamAt(0));
                }
                else if (string.Equals(
                    source.ContentType,
                    "application/oxps",
                    StringComparison.OrdinalIgnoreCase))
                {
                    PrintWorkflowPdlConverter converter =
                        args.GetPdlConverter(
                            PrintWorkflowPdlConversionType.XpsToPdf);
                    await converter.ConvertPdlAsync(
                        args.GetJobPrintTicket(),
                        source.GetInputStream(),
                        output.GetOutputStreamAt(0));
                }
                else
                {
                    throw new InvalidDataException(
                        "Niet ondersteund printerformaat: "
                        + source.ContentType);
                }

                await output.FlushAsync();
            }

            await temporaryFile.RenameAsync(
                jobId + ".pdf",
                NameCollisionOption.FailIfExists);
        }

        private static async Task WriteEndpointErrorAsync(
            Exception exception)
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
                // De Windows printstatus meldt de fout al aan de afzender.
            }
        }
    }
}

