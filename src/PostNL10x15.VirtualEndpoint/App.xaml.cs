using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Graphics.Printing.PrintSupport;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace PostNL10x15.VirtualEndpoint
{
    sealed partial class App : Application
    {
        private Deferral settingsDeferral;

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

        internal void ExitPrintSettings()
        {
            settingsDeferral?.Complete();
            settingsDeferral = null;
        }
    }
}
