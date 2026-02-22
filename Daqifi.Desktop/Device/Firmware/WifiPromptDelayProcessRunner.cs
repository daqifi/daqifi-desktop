using Daqifi.Core.Firmware;
using Daqifi.Desktop.Common.Loggers;

namespace Daqifi.Desktop.Device.Firmware;

/// <summary>
/// Adds a short delay before responding to WINC bootloader prompts.
/// The legacy desktop updater waited before sending Enter, and some devices
/// appear sensitive to that timing during WiFi programming.
/// </summary>
public sealed class WifiPromptDelayProcessRunner : IExternalProcessRunner
{
    private const string WincBootPromptMarker = "Power cycle WINC and set to bootloader mode";
    private const string ContinuePromptMarker = "Press any key to continue";
    private const string BridgeIdQueryFailureMarker = "failed to read serial bridge ID query response";
    private const string ProgrammerInitFailureMarker = "failed to initialise programming firmware";
    private const string ProgrammingFailedMarker = "Programming device failed";

    private readonly IExternalProcessRunner _innerRunner;
    private readonly TimeSpan _promptResponseDelay;
    private readonly AppLogger _appLogger = AppLogger.Instance;
    private readonly TimeSpan _retryDelay = TimeSpan.FromSeconds(5);

    public WifiPromptDelayProcessRunner(
        IExternalProcessRunner? innerRunner = null,
        TimeSpan? promptResponseDelay = null)
    {
        _innerRunner = innerRunner ?? new ProcessExternalProcessRunner();
        _promptResponseDelay = promptResponseDelay ?? TimeSpan.FromSeconds(1);
    }

    public async Task<ExternalProcessResult> RunAsync(
        ExternalProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var firstAttempt = await RunSingleAttemptAsync(
            request,
            _promptResponseDelay,
            cancellationToken).ConfigureAwait(false);
        if (!ShouldRetry(firstAttempt, attempt: 1))
        {
            return firstAttempt;
        }

        _appLogger.Warning("WiFi flash tool reported serial bridge init failure; retrying once.");
        await Task.Delay(_retryDelay, cancellationToken).ConfigureAwait(false);
        var secondAttempt = await RunSingleAttemptAsync(
            request,
            _promptResponseDelay + TimeSpan.FromSeconds(2),
            cancellationToken).ConfigureAwait(false);
        return secondAttempt;
    }

    private Task<ExternalProcessResult> RunSingleAttemptAsync(
        ExternalProcessRequest request,
        TimeSpan promptResponseDelay,
        CancellationToken cancellationToken)
    {
        var wrappedRequest = new ExternalProcessRequest
        {
            FileName = request.FileName,
            Arguments = request.Arguments,
            WorkingDirectory = request.WorkingDirectory,
            Timeout = request.Timeout,
            OnStandardOutputLine = line =>
            {
                _appLogger.Information($"WiFi flash output: {line}");
                request.OnStandardOutputLine?.Invoke(line);
            },
            OnStandardErrorLine = line =>
            {
                _appLogger.Warning($"WiFi flash stderr: {line}");
                request.OnStandardErrorLine?.Invoke(line);
            },
            StandardInputResponseFactory = BuildInputResponder(
                request.StandardInputResponseFactory,
                promptResponseDelay)
        };

        return _innerRunner.RunAsync(wrappedRequest, cancellationToken);
    }

    private Func<string, string?>? BuildInputResponder(
        Func<string, string?>? fallbackResponder,
        TimeSpan promptResponseDelay)
    {
        if (fallbackResponder == null)
        {
            return null;
        }

        var continueSignalSent = false;

        return line =>
        {
            if (line.Contains(WincBootPromptMarker, StringComparison.OrdinalIgnoreCase))
            {
                if (continueSignalSent)
                {
                    return null;
                }

                _appLogger.Information("WiFi flash tool requested WINC power-cycle; waiting before sending continue signal.");
                Thread.Sleep(promptResponseDelay);
                continueSignalSent = true;
                _appLogger.Information("Sending continue signal to WiFi flash tool.");
                return string.Empty;
            }

            if (!continueSignalSent &&
                line.Contains(ContinuePromptMarker, StringComparison.OrdinalIgnoreCase))
            {
                continueSignalSent = true;
                _appLogger.Information("Sending continue signal to WiFi flash tool.");
                return string.Empty;
            }

            return fallbackResponder(line);
        };
    }

    private static bool ShouldRetry(ExternalProcessResult result, int attempt)
    {
        if (attempt >= 2)
        {
            return false;
        }

        if (result.TimedOut)
        {
            return false;
        }

        return ContainsAny(result.StandardErrorLines, BridgeIdQueryFailureMarker, ProgrammerInitFailureMarker) ||
               ContainsAny(result.StandardOutputLines, ProgrammingFailedMarker);
    }

    private static bool ContainsAny(IEnumerable<string> lines, params string[] markers)
    {
        foreach (var line in lines)
        {
            foreach (var marker in markers)
            {
                if (line.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
