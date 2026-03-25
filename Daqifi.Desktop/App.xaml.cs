using Daqifi.Core.Communication.Transport;
using Daqifi.Core.Firmware;
using Daqifi.Desktop.DialogService;
using Daqifi.Desktop.Logger;
using Daqifi.Desktop.WindowViewModelMapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Net.Http;
using System.Windows;

namespace Daqifi.Desktop;

public partial class App
{
    private SplashScreen SplashScreen { get; set; }
    public static IServiceProvider ServiceProvider { get; private set; }

    /// <summary>
    /// Root directory for DAQiFi application data (logs, database).
    /// </summary>
    public static string DaqifiDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DAQiFi");

    /// <summary>
    /// Full path to the SQLite database file.
    /// </summary>
    public static string DatabasePath { get; } = Path.Combine(DaqifiDataDirectory, "DAQiFiDatabase.db");

    public bool IsWindowInit { get; set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShowSplashScreen();

        Directory.CreateDirectory(DaqifiDataDirectory);
        AppDomain.CurrentDomain.SetData("DataDirectory", DaqifiDataDirectory);

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddDbContextFactory<LoggingContext>(options =>
            options.UseSqlite($"Data source={DatabasePath}")
        );

        serviceCollection.AddLogging();
        serviceCollection.AddHttpClient();
        serviceCollection.AddSingleton<IFirmwareDownloadService>(provider =>
        {
            var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
            return new GitHubFirmwareDownloadService(httpClientFactory.CreateClient());
        });
        serviceCollection.AddSingleton<IHidTransport, HidLibraryTransport>();
        serviceCollection.AddSingleton<IExternalProcessRunner, ProcessExternalProcessRunner>();
        serviceCollection.AddSingleton<IFirmwareUpdateService, FirmwareUpdateService>();

        serviceCollection.AddSingleton<LoggingManager>();
        ServiceLocator.RegisterSingleton<IDialogService, DialogService.DialogService>();
        ServiceLocator.RegisterSingleton<IWindowViewModelMappings, WindowViewModelMappings>();

        ServiceProvider = serviceCollection.BuildServiceProvider();
        // Create and show main window
        var view = new MainWindow();
        view.Show();
    }

    private void ShowSplashScreen()
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
