using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace PostNL10x15.VirtualEndpoint
{
    public sealed partial class PrintSettingsPage : Page
    {
        public PrintSettingsPage()
        {
            InitializeComponent();
        }

        private void OkClicked(object sender, RoutedEventArgs e)
        {
            (Application.Current as App)?.ExitPrintSettings();
        }
    }
}
