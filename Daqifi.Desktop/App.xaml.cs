using Daqifi.Core.Communication.Transport;
using Daqifi.Core.Firmware;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.DialogService;
using Daqifi.Desktop.Logger;
using Daqifi.Desktop.WindowViewModelMapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sentry;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;

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

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

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

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLogger.Instance.Error(e.Exception, "Unhandled dispatcher exception");
        SentrySdk.CaptureException(e.Exception);
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            SentrySdk.CaptureException(ex);
        }
    }

    private static void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
    {
        SentrySdk.CaptureException(e.Exception);
        e.SetObserved();
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
