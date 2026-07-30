using Windows.ApplicationModel.Background;
using Windows.Graphics.Printing.PrintSupport;
using Windows.Graphics.Printing.Workflow;

namespace PostNL10x15.VirtualPrinter;

/// <summary>
/// Verzorgt de door Windows verplichte validatie van de printinstellingen.
/// </summary>
public sealed class PsaExtensionTask : IBackgroundTask
{
    public PsaExtensionTask()
    {
        EndpointLog.Write("PsaExtensionTask constructed.");
    }

    public void Run(IBackgroundTaskInstance task)
    {
        RunGuarded(() =>
        {
            EndpointLog.Write("PsaExtensionTask.Run entered.");
            BackgroundTaskDeferral deferral = task.GetDeferral();
            PrintSupportExtensionTriggerDetails? details =
                task.TriggerDetails as PrintSupportExtensionTriggerDetails;

            if (details?.Session is null)
            {
                EndpointLog.Write("PrintSupportExtension details missing.");
                deferral.Complete();
                return;
            }

            task.Canceled += (_, reason) =>
                RunGuarded(() =>
                {
                    EndpointLog.Write(
                        "PsaExtensionTask canceled: " + reason);
                    deferral.Complete();
                });

            details.Session.PrintTicketValidationRequested += (_, args) =>
                RunGuarded(() =>
                {
                    using var validationDeferral = args.GetDeferral();
                    args.SetPrintTicketValidationStatus(
                        WorkflowPrintTicketValidationStatus.Resolved);
                    EndpointLog.Write("Print ticket validation resolved.");
                });

            details.Session.Start();
            EndpointLog.Write("PrintSupportExtension session started.");
        });
    }

    private static void RunGuarded(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            EndpointLog.Write(
                "PsaExtensionTask exception: " + exception);
            // Windows kan een sessie intrekken terwijl de callback nog loopt.
        }
    }
}
