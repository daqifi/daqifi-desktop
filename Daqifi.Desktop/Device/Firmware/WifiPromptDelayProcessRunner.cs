using Daqifi.Core.Firmware;

namespace Daqifi.Desktop.Device.Firmware;

/// <summary>
/// Adds a short delay before responding to WINC bootloader prompts.
/// The legacy desktop updater waited before sending Enter, and some devices
/// appear sensitive to that timing during WiFi programming.
/// </summary>
public sealed class WifiPromptDelayProcessRunner : IExternalProcessRunner
{
    private const string WincBootPromptMarker = "Power cycle WINC and set to bootloader mode";

    private readonly IExternalProcessRunner _innerRunner;
    private readonly TimeSpan _promptResponseDelay;

    public WifiPromptDelayProcessRunner(
        IExternalProcessRunner? innerRunner = null,
        TimeSpan? promptResponseDelay = null)
    {
        _innerRunner = innerRunner ?? new ProcessExternalProcessRunner();
        _promptResponseDelay = promptResponseDelay ?? TimeSpan.FromSeconds(2);
    }

    public Task<ExternalProcessResult> RunAsync(
        ExternalProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var wrappedRequest = new ExternalProcessRequest
        {
            FileName = request.FileName,
            Arguments = request.Arguments,
            WorkingDirectory = request.WorkingDirectory,
            Timeout = request.Timeout,
            OnStandardOutputLine = request.OnStandardOutputLine,
            OnStandardErrorLine = request.OnStandardErrorLine,
            StandardInputResponseFactory = WrapInputResponder(request.StandardInputResponseFactory)
        };

        return _innerRunner.RunAsync(wrappedRequest, cancellationToken);
    }

    private Func<string, string?>? WrapInputResponder(Func<string, string?>? responder)
    {
        if (responder == null)
        {
            return null;
        }

        return line =>
        {
            var response = responder(line);
            if (response == null || !line.Contains(WincBootPromptMarker, StringComparison.OrdinalIgnoreCase))
            {
                return response;
            }

            Thread.Sleep(_promptResponseDelay);
            return response;
        };
    }
}
