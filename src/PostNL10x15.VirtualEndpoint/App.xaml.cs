using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Graphics.Printing.PrintSupport;
using Windows.Graphics.Printing.Workflow;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using System;
using System.IO;
using Windows.Storage;

namespace PostNL10x15.VirtualEndpoint
{
    sealed partial class App : Application
    {
        private Deferral settingsDeferral;
        private PrintWorkflowJobUISession jobUiSession;

        public App()
        {
            InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            var rootFrame = Window.Current.Content as Frame;
            if (rootFrame == null)
            {
                rootFrame = new Frame();
                Window.Current.Content = rootFrame;
            }

            if (rootFrame.Content == null)
            {
                rootFrame.Navigate(typeof(MainPage));
            }

            Window.Current.Activate();
        }

        protected override void OnActivated(IActivatedEventArgs args)
        {
            if (args.Kind == ActivationKind.PrintSupportJobUI)
            {
                ActivateJobPreview(args);
                return;
            }

            if (args.Kind != ActivationKind.PrintSupportSettingsUI)
            {
                base.OnActivated(args);
                return;
            }

            var settingsArgs =
                args as PrintSupportSettingsActivatedEventArgs;
            if (settingsArgs == null)
            {
                return;
            }

            settingsDeferral = settingsArgs.GetDeferral();

            var rootFrame = new Frame();
            rootFrame.Navigate(
                typeof(PrintSettingsPage),
                settingsArgs.Session);
            Window.Current.Content = rootFrame;
            Window.Current.Activate();
        }

        private void ActivateJobPreview(IActivatedEventArgs args)
        {
            LogJobUi("Activation received.");
            var jobArgs = args as PrintWorkflowJobActivatedEventArgs;
            if (jobArgs?.Session == null)
            {
                LogJobUi("Activation has no session.");
                return;
            }

            var rootFrame = new Frame();
            rootFrame.Navigate(typeof(JobPreviewPage));
            Window.Current.Content = rootFrame;
            LogJobUi("Preview page created.");

            var previewPage = rootFrame.Content as JobPreviewPage;
            if (previewPage == null)
            {
                return;
            }

            jobUiSession = jobArgs.Session;
            jobUiSession.VirtualPrinterUIDataAvailable +=
                previewPage.OnVirtualPrinterUIDataAvailable;
            previewPage.BeginLoadPreview();
            Window.Current.Activate();
            LogJobUi("Window activated; starting job UI session.");
            jobUiSession.Start();
            LogJobUi("Job UI session started.");
        }

        internal static void LogJobUi(string message)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(
                        ApplicationData.Current.LocalFolder.Path,
                        "job-ui-trace.log"),
                    DateTimeOffset.Now.ToString("O")
                    + " "
                    + message
                    + Environment.NewLine);
            }
            catch
            {
                // Diagnostiek mag het voorbeeldvenster niet blokkeren.
            }
        }

        internal void ExitPrintSettings()
        {
            settingsDeferral?.Complete();
            settingsDeferral = null;
        }
    }
}
