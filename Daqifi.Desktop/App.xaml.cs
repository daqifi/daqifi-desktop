using Daqifi.Desktop.DialogService;
using Daqifi.Desktop.WindowViewModelMapping;
using System;
using System.IO;
using System.Windows;

namespace Daqifi.Desktop
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App
    {
        public SplashScreen SplashScreen { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            AppDomain.CurrentDomain.SetData("DataDirectory", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DAQifi"));

            ShowSplashScreen();

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
