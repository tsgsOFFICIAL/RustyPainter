namespace RustyPainter.Service.Logging;

/// <summary>
/// Severity levels for entries written by <see cref="AppLog"/>, ordered from least to most severe.
/// </summary>
public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error,
    Fatal
}
