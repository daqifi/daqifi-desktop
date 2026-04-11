using Daqifi.Core.Communication.Transport;
using Daqifi.Core.Firmware;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.DialogService;
using Daqifi.Desktop.Logger;
using Daqifi.Desktop.WindowViewModelMapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
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

    /// <summary>
    /// Initializes the application and wires global exception handlers for Sentry reporting.
    /// </summary>
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
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
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

        // Apply database migrations before any DB access
        var contextFactory = ServiceProvider.GetRequiredService<IDbContextFactory<LoggingContext>>();
        DatabaseMigrator.MigrateDatabase(contextFactory);

        // Create and show main window
        var view = new MainWindow();
        view.Show();

        AppLogger.Instance.AddBreadcrumb("app", "App startup complete");
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLogger.Instance.Error(e.Exception, "Unhandled dispatcher exception");
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            AppLogger.Instance.Error(ex, "Unhandled AppDomain exception");
        }

        if (e.IsTerminating)
        {
            try { AppLogger.Instance.Shutdown(); } catch { }
        }
    }

    private static void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLogger.Instance.Error(e.Exception, "Unobserved task exception");
        e.SetObserved();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLogger.Instance.Shutdown();
        base.OnExit(e);
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
