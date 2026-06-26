using Daqifi.Core.Communication.Transport;
using Daqifi.Core.Firmware;
using Daqifi.Desktop.Common;
using Daqifi.Desktop.Common.Loggers;
using Daqifi.Desktop.Loggers;
using Daqifi.Desktop.Configuration;
using Daqifi.Desktop.Device.Firmware;
using Daqifi.Desktop.DialogService;
using Daqifi.Desktop.Services;
using Daqifi.Desktop.Logger;
using Daqifi.Desktop.View;
using Daqifi.Desktop.WindowViewModelMapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    /// Root directory for DAQiFi application data (logs, database). Resolved by
    /// <see cref="AppDataPaths"/>: machine-wide for elevated (production) runs, per-user for
    /// any un-elevated run (the UI-test harness or a normal non-admin Debug launch).
    /// </summary>
    public static string DaqifiDataDirectory => AppDataPaths.DataDirectory;

    /// <summary>
    /// Full path to the SQLite database file.
    /// </summary>
    public static string DatabasePath { get; } = Path.Combine(DaqifiDataDirectory, "DAQiFiDatabase.db");

    public bool IsWindowInit { get; set; }

    /// <summary>
    /// Indicates the application was launched in unattended/test mode (environment variable
    /// <c>DAQIFI_TEST_MODE=1</c>). In this mode modal dialogs are suppressed so UI automation
    /// can drive the app without prompts. Defaults to <c>false</c> for normal launches.
    /// </summary>
    public static bool IsTestMode => AppDataPaths.IsTestMode;

    /// <summary>
    /// <c>true</c> when the process is running elevated (administrator). Firewall
    /// configuration (which requires admin) only runs when elevated; un-elevated runs use a
    /// per-user data directory and skip it, so a non-admin Debug launch never crashes on an
    /// admin-owned database or shows a "configure firewall manually" prompt.
    /// </summary>
    public static bool IsElevated => AppDataPaths.IsElevated;

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

        if (IsTestMode)
        {
            // Suppress any modal message boxes so UI automation is never blocked.
            FirewallConfiguration.SetMessageBoxService(new NoOpMessageBoxService());
        }

        ShowSplashScreen();

        Directory.CreateDirectory(DaqifiDataDirectory);
        AppDomain.CurrentDomain.SetData("DataDirectory", DaqifiDataDirectory);

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddDbContextFactory<LoggingContext>(options =>
            options.UseSqlite($"Data source={DatabasePath}")
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
        );

        // Route Core's Microsoft.Extensions.Logging output (e.g. the firmware update service's
        // WiFi flash tool output / progress) into the desktop NLog file via AppLogger. Restrict
        // forwarding to DAQiFi/Core categories at Information+ so framework noise (EF Core,
        // HttpClient, etc.) doesn't flood DAQiFiAppLog.log and bury the flash diagnostics.
        serviceCollection.AddLogging(builder =>
        {
            builder.AddProvider(new AppLoggerLoggerProvider());
            // Scope the filter to this provider so it only governs what reaches the NLog file and
            // never suppresses logs for any other provider added later. Always surface Error+ from
            // any category (framework errors are worth keeping), plus DAQiFi/Core diagnostics at
            // Information+; non-DAQiFi informational noise (EF Core, HttpClient, …) is dropped.
            builder.AddFilter<AppLoggerLoggerProvider>((category, level) =>
                level is >= LogLevel.Error and < LogLevel.None
                || (category is not null
                    && category.StartsWith("Daqifi", StringComparison.OrdinalIgnoreCase)
                    && level is >= LogLevel.Information and < LogLevel.None));
        });
        serviceCollection.AddHttpClient();
        serviceCollection.AddSingleton<IFirmwareDownloadService>(provider =>
        {
            var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
            return new GitHubFirmwareDownloadService(httpClientFactory.CreateClient());
        });
        // Widen the bootloader HID timeouts past Core's 10 s defaults so a long late flash op
        // doesn't trip the PIC32 update; see FirmwareUpdateServiceConfig / issue #575.
        serviceCollection.AddSingleton<IHidTransport>(_ => FirmwareUpdateServiceConfig.CreateBootloaderHidTransport());
        serviceCollection.AddSingleton<IExternalProcessRunner, ProcessExternalProcessRunner>();
        serviceCollection.AddSingleton<IFirmwareUpdateService>(provider => new FirmwareUpdateService(
            provider.GetRequiredService<IHidTransport>(),
            provider.GetRequiredService<IFirmwareDownloadService>(),
            provider.GetRequiredService<IExternalProcessRunner>(),
            provider.GetRequiredService<ILogger<FirmwareUpdateService>>(),
            options: FirmwareUpdateServiceConfig.CreateOptions()));

        serviceCollection.AddSingleton<LoggingManager>();
        ServiceLocator.RegisterSingleton<IDialogService, DialogService.DialogService>();
        ServiceLocator.RegisterSingleton<IWindowViewModelMappings, WindowViewModelMappings>();

        ServiceProvider = serviceCollection.BuildServiceProvider();

        // Apply database migrations before any DB access.
        // Temporarily switch to OnExplicitShutdown so closing the migration
        // status window does not terminate the application.
        var contextFactory = ServiceProvider.GetRequiredService<IDbContextFactory<LoggingContext>>();
        if (DatabaseMigrator.PrepareMigration(contextFactory, DatabasePath))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var statusWindow = new MigrationStatusWindow();
            statusWindow.Show();

            DatabaseMigrator.ApplyMigrations(contextFactory, DatabasePath);

            statusWindow.Close();

            ShutdownMode = ShutdownMode.OnLastWindowClose;
        }

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
