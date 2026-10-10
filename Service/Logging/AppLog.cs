using System.Threading.Tasks;
using System.Globalization;
using System.Diagnostics;
using System.Text;
using System.IO;
using System;

namespace RustyPainter.Service.Logging;

/// <summary>
/// Provides file-based application logging with daily rotation, retention pruning, and level
/// filtering. Static (rather than DI-registered like the other services) so it is available
/// immediately at process start and from crash handlers, before the service container exists.
/// </summary>
public static class AppLog
{
    private const int DefaultRetentionDays = 30;

    private static readonly object Sync = new();
    private static int _retentionDays = DefaultRetentionDays;
    private static string? _logFilePath;
    private static bool _handlersRegistered;

    /// <summary>
    /// Gets or sets the minimum level that gets written. Entries below this level are dropped
    /// before any I/O happens. Defaults to <see cref="LogLevel.Debug"/> in debug builds and
    /// <see cref="LogLevel.Info"/> otherwise.
    /// </summary>
    public static LogLevel MinimumLevel { get; set; } =
#if DEBUG
        LogLevel.Debug;
#else
        LogLevel.Info;
#endif

    /// <summary>
    /// Gets the directory path where daily log files are stored.
    /// </summary>
    public static string LogDirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RustyPainter", "logs");

    /// <summary>
    /// Gets the path to the current daily log file, or <see langword="null"/> if the logger has not been initialized.
    /// </summary>
    public static string? LogFilePath => _logFilePath;

    /// <summary>
    /// Applies the user's log retention preference and immediately prunes anything now too old.
    /// Called after settings load rather than at <see cref="Initialize"/>, because the logger starts
    /// before the DI container exists and has to work with a default until then.
    /// </summary>
    /// <param name="days">How many days of logs to keep. Values below 1 are ignored.</param>
    public static void ApplyRetention(int days)
    {
        if (days < 1)
            return;

        lock (Sync)
        {
            _retentionDays = days;
            PruneOldLogs();
        }
    }

    /// <summary>
    /// Applies the user's verbose-logging preference. When on, <see cref="MinimumLevel"/> drops to
    /// <see cref="LogLevel.Debug"/> so nothing is filtered out; when off it returns to the build's
    /// default. Called after settings load, and again whenever the setting is toggled.
    /// </summary>
    /// <param name="verbose">Whether to log every level, including Debug.</param>
    public static void ApplyVerbose(bool verbose)
    {
        LogLevel target = verbose
            ? LogLevel.Debug
#if DEBUG
            : LogLevel.Debug;
#else
            : LogLevel.Info;
#endif

        MinimumLevel = target;

        // Logged unconditionally, even when the level didn't actually change, so a support log
        // always shows whether the user turned verbose logging on.
        Info("AppLog", $"Verbose logging {(verbose ? "enabled" : "disabled")}; minimum level is now {target}.");
    }

    /// <summary>
    /// Initializes the logger and creates today's log file if it does not already exist. Subsequent
    /// calls have no effect. Old logs are not pruned here - see <see cref="ApplyRetention"/>.
    /// </summary>
    public static void Initialize()
    {
        lock (Sync)
        {
            if (!string.IsNullOrWhiteSpace(_logFilePath))
                return;

            Directory.CreateDirectory(LogDirectoryPath);

            string date = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            _logFilePath = Path.Combine(LogDirectoryPath, $"app-{date}.log");

            // Deliberately does NOT prune here: the logger starts before settings are loaded, so it
            // would prune against the default window and delete files the user's own setting says
            // to keep. Pruning happens in ApplyRetention, once the real value is known.

            Write(LogLevel.Info, "AppLog",
                $"Logger initialized. Version={SafeGetVersion()}, OS={Utility.GetFriendlyOsName()} ({Environment.OSVersion.Version}), " +
                $"64-bit process={Environment.Is64BitProcess}");
        }
    }

    /// <summary>
    /// Hooks <see cref="AppDomain.UnhandledException"/> and <see cref="TaskScheduler.UnobservedTaskException"/>
    /// so crashes and swallowed task faults are always captured. Safe to call multiple times.
    /// </summary>
    public static void RegisterGlobalExceptionHandlers()
    {
        lock (Sync)
        {
            if (_handlersRegistered)
                return;

            _handlersRegistered = true;

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                Write(LogLevel.Fatal, "UnhandledException", $"Terminating={e.IsTerminating}", e.ExceptionObject as Exception);

            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                Write(LogLevel.Error, "UnobservedTaskException", "An unobserved task exception occurred.", e.Exception);
                e.SetObserved();
            };
        }
    }

    /// <summary>Writes a debug message. Filtered out unless <see cref="MinimumLevel"/> allows it.</summary>
    public static void Debug(string scope, string message) => Write(LogLevel.Debug, scope, message);

    /// <summary>Writes an informational message.</summary>
    public static void Info(string scope, string message) => Write(LogLevel.Info, scope, message);

    /// <summary>Writes a warning message, optionally including exception details.</summary>
    public static void Warn(string scope, string message, Exception? ex = null) => Write(LogLevel.Warn, scope, message, ex);

    /// <summary>Writes an error message, optionally including exception details.</summary>
    public static void Error(string scope, string message, Exception? ex = null) => Write(LogLevel.Error, scope, message, ex);

    /// <summary>Writes a fatal message, optionally including exception details.</summary>
    public static void Fatal(string scope, string message, Exception? ex = null) => Write(LogLevel.Fatal, scope, message, ex);

    /// <summary>
    /// Opens the log directory in the OS file explorer, creating it first if necessary.
    /// </summary>
    public static void OpenLogDirectory()
    {
        Directory.CreateDirectory(LogDirectoryPath);
        Utility.OpenUrl(LogDirectoryPath);
    }

    private static void Write(LogLevel level, string scope, string message, Exception? ex = null)
    {
        if (level < MinimumLevel)
            return;

        try
        {
            if (string.IsNullOrWhiteSpace(_logFilePath))
                Initialize();

            string ts = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);
            string line = $"[{ts}] [{level.ToString().ToUpperInvariant(),-5}] [{scope}] {message}";

            if (ex != null)
                line += FormatException(ex);

            lock (Sync)
                File.AppendAllText(_logFilePath!, line + Environment.NewLine, Encoding.UTF8);

            Trace.WriteLine(line);
        }
        catch (Exception writeEx)
        {
            // The logger must never be the reason the app crashes.
            Trace.WriteLine($"[AppLog-Fallback] {writeEx.GetType().Name}: {writeEx.Message}");
        }
    }

    /// <summary>
    /// Formats an exception (and any inner exceptions) as indented, appendable log lines including stack traces.
    /// </summary>
    private static string FormatException(Exception ex)
    {
        StringBuilder sb = new();
        Exception? current = ex;

        while (current != null)
        {
            sb.Append(Environment.NewLine).Append("    ").Append(current.GetType().FullName).Append(": ").Append(current.Message);

            if (current.StackTrace != null)
            {
                foreach (string frame in current.StackTrace.Split('\n'))
                    sb.Append(Environment.NewLine).Append("        ").Append(frame.TrimEnd('\r'));
            }

            current = current.InnerException;
            if (current != null)
                sb.Append(Environment.NewLine).Append("    ---> Inner Exception:");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Deletes log files older than the current retention window. Best-effort: a single
    /// unreadable/locked file is skipped rather than aborting the whole prune pass.
    /// </summary>
    private static void PruneOldLogs()
    {
        try
        {
            DateTime cutoff = DateTime.Now.Date.AddDays(-_retentionDays);

            foreach (string file in Directory.EnumerateFiles(LogDirectoryPath, "app-*.log"))
            {
                try
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    string datePart = name.Length > 4 ? name[4..] : "";

                    if (DateTime.TryParseExact(datePart, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fileDate)
                        && fileDate < cutoff)
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    private static string SafeGetVersion()
    {
        try
        {
            return Utility.GetDisplayVersion();
        }
        catch
        {
            return "N/A";
        }
    }
}
