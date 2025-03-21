using Daqifi.Desktop.DialogService;
using Daqifi.Desktop.Logger;
using Daqifi.Desktop.WindowViewModelMapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Windows;

namespace Daqifi.Desktop;

public partial class App
{
    public SplashScreen SplashScreen { get; private set; }
    public static IServiceProvider ServiceProvider { get; private set; }
    private bool isWindowInit = false;

    public bool IsWindowInit
    {
        get { return isWindowInit; }
        set { isWindowInit = value; }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShowSplashScreen();

        var daqifiDataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DAQiFi");
        Directory.CreateDirectory(daqifiDataDirectory);
        AppDomain.CurrentDomain.SetData("DataDirectory", daqifiDataDirectory);

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddDbContextFactory<LoggingContext>(options =>
            options.UseSqlite($"Data source={Path.Combine(daqifiDataDirectory, "DAQiFiDatabase.db")}")
        );

        serviceCollection.AddSingleton<LoggingManager>();
        ServiceLocator.RegisterSingleton<IDialogService, DialogService.DialogService>();
        ServiceLocator.RegisterSingleton<IWindowViewModelMappings, WindowViewModelMappings>();

        ServiceProvider = serviceCollection.BuildServiceProvider();
        // Create and show main window
        var view = new MainWindow();
        view.Show();
    }

    protected void ShowSplashScreen()
    {
        try
        {
            SplashScreen = new SplashScreen("Images/DAQiFi.png");
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