namespace Daqifi.Desktop.Common.Loggers;

/// <summary>
/// Carrier exception for message-only errors reported through <see cref="AppLogger.Error(string)"/>.
/// Sentry requires an exception to capture an event; using a dedicated type (instead of a bare
/// <see cref="Exception"/>) keeps these events grouped separately from real thrown exceptions.
/// </summary>
public class AppLogErrorException : Exception
{
    /// <summary>Creates the carrier exception with no message.</summary>
    public AppLogErrorException() { }

    /// <summary>Creates the carrier exception for the given logged message.</summary>
    /// <param name="message">The message that was logged as an error.</param>
    public AppLogErrorException(string message) : base(message) { }

    /// <summary>Creates the carrier exception with an inner exception.</summary>
    /// <param name="message">The message that was logged as an error.</param>
    /// <param name="innerException">The originating exception.</param>
    public AppLogErrorException(string message, Exception innerException) : base(message, innerException) { }
}
