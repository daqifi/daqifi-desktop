using Daqifi.Desktop.DialogService;
using Daqifi.Desktop.WindowViewModelMapping;
using System;
using System.IO;
using System.Windows;
using Xamarin;

namespace Daqifi.Desktop
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App
    {
        private const string AppName = "DAQifi";
        private const string Version = "1.0.4";
        private const string XamarinKey = "699deee1bbdab8013281235f782bc0b49f082df4";

        public SplashScreen SplashScreen { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            AppDomain.CurrentDomain.SetData("DataDirectory", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DAQifi"));

            ShowSplashScreen();

            // Initialize call should happen as soon as possible, ideally at app start-up.
            Insights.Initialize(XamarinKey, Version, AppName);

            // Configure service locator
            ServiceLocator.RegisterSingleton<IDialogService, DialogService.DialogService>();
            ServiceLocator.RegisterSingleton<IWindowViewModelMappings, WindowViewModelMappings>();

            // Create and show main window
            var view = new MainWindow();
            view.Show();
        }

        protected void ShowSplashScreen()
        {
            try
            {
                SplashScreen = new SplashScreen("Images/DAQifi.png");
                SplashScreen.Show(true, true);
            }
            catch
            {
                try
                {
                    SplashScreen.Close(new TimeSpan(0, 0, 1));
                }
                catch { }
            }
        }
    }
}
