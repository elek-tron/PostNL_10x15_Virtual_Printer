using System;
using System.Threading.Tasks;
using Windows.ApplicationModel.Resources;
using Windows.Graphics.Printing.Workflow;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;

namespace PostNL10x15.VirtualEndpoint
{
    public sealed partial class JobPreviewPage : Page
    {
        private readonly ResourceLoader resources =
            ResourceLoader.GetForCurrentView();
        private string jobId;
        private bool decisionStarted;

        public JobPreviewPage()
        {
            InitializeComponent();
        }

        internal void BeginLoadPreview()
        {
            App.LogJobUi("Loading preview.");
            _ = LoadPreviewAsync();
        }

        internal void OnVirtualPrinterUIDataAvailable(
            PrintWorkflowJobUISession sender,
            PrintWorkflowVirtualPrinterUIEventArgs args)
        {
            App.LogJobUi("Virtual printer UI data received.");
            using (args.GetDeferral())
            {
                // Het voorbeeld is al vanuit de UI-draad geladen. Dit
                // Windows-event hoeft alleen te worden vrijgegeven.
            }
            App.LogJobUi("Virtual printer UI data released.");
        }

        private async Task LoadPreviewAsync()
        {
            try
            {
                StorageFolder previewFolder =
                    await ApplicationData.Current.LocalFolder
                        .CreateFolderAsync(
                            "Preview",
                            CreationCollisionOption.OpenIfExists);
                StorageFile activeJobFile =
                    await previewFolder.GetFileAsync("active-job.txt");
                jobId = (await FileIO.ReadTextAsync(activeJobFile)).Trim();
                ValidateJobId(jobId);

                StorageFile printerFile =
                    await previewFolder.GetFileAsync(
                        jobId + ".printer.txt");
                string printerName =
                    (await FileIO.ReadTextAsync(printerFile)).Trim();
                PrinterText.Text = string.Format(
                    resources.GetString("PreviewPrinterFormat"),
                    printerName);

                StorageFile previewFile =
                    await previewFolder.GetFileAsync(jobId + ".png");
                using (IRandomAccessStream stream =
                       await previewFile.OpenAsync(FileAccessMode.Read))
                {
                    var bitmap = new BitmapImage();
                    await bitmap.SetSourceAsync(stream);
                    PreviewImage.Source = bitmap;
                }

                LoadingPanel.Visibility = Visibility.Collapsed;
                PrintButton.IsEnabled = true;
                App.LogJobUi("Preview loaded.");
            }
            catch
            {
                LoadingPanel.Visibility = Visibility.Collapsed;
                PreviewError.Visibility = Visibility.Visible;
                PrintButton.IsEnabled = false;
                App.LogJobUi("Preview could not be loaded.");
            }
        }

        private async void PrintClicked(
            object sender,
            RoutedEventArgs e)
        {
            await CompleteDecisionAsync("print");
        }

        private async void CancelClicked(
            object sender,
            RoutedEventArgs e)
        {
            await CompleteDecisionAsync("cancel");
        }

        private async Task CompleteDecisionAsync(string decision)
        {
            if (decisionStarted)
            {
                return;
            }

            decisionStarted = true;
            PrintButton.IsEnabled = false;
            CancelButton.IsEnabled = false;

            if (!string.IsNullOrWhiteSpace(jobId))
            {
                StorageFolder previewFolder =
                    await ApplicationData.Current.LocalFolder
                        .CreateFolderAsync(
                            "Preview",
                            CreationCollisionOption.OpenIfExists);
                StorageFile decisionFile =
                    await previewFolder.CreateFileAsync(
                        jobId + ".decision",
                        CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(decisionFile, decision);
            }

            App.LogJobUi("Decision written: " + decision + ".");
            Window.Current.Close();
        }

        private static void ValidateJobId(string value)
        {
            if (value.Length != 32)
            {
                throw new InvalidOperationException();
            }

            foreach (char character in value)
            {
                bool hexadecimal =
                    character >= '0' && character <= '9'
                    || character >= 'a' && character <= 'f'
                    || character >= 'A' && character <= 'F';
                if (!hexadecimal)
                {
                    throw new InvalidOperationException();
                }
            }
        }
    }
}
