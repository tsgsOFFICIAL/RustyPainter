namespace RustyPainter.Interface;

/// <summary>
/// Every persisted application preference, serialized as one JSON document. Properties carry their
/// own defaults so a missing or partial file still produces a sensible configuration rather than
/// zeroes and nulls.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Whether the app looks for a newer release each time it opens.</summary>
    public bool CheckForUpdatesOnStartup { get; set; } = true;

    /// <summary>
    /// Whether an available update is downloaded and applied without asking. Off by default -
    /// applying an update replaces the running app and restarts it, which shouldn't happen to
    /// someone who never opted in.
    /// </summary>
    public bool AutoUpdate { get; set; }

    /// <summary>How many days of log files to keep before older ones are pruned.</summary>
    public int LogRetentionDays { get; set; } = 30;

    /// <summary>
    /// Whether every log level is written, including Debug. Off by default - normal runs log at
    /// Info and above. Turned on when diagnosing a problem for someone.
    /// </summary>
    public bool VerboseLogging { get; set; }

    /// <summary>How fast painting is, from 1 (slowest, most reliable) to 100 (fastest).</summary>
    public int PaintSpeed { get; set; } = 60;

    /// <summary>Whether the picture is shown over the game on the get-ready page.</summary>
    public bool ShowHologram { get; set; } = true;

    /// <summary>How solid the picture over the game is, from 10 to 90 percent.</summary>
    public int HologramStrength { get; set; } = 55;

    /// <summary>Whether the box with the progress is drawn over the game while painting.</summary>
    public bool ShowStatusBox { get; set; } = true;

    /// <summary>The key that starts, pauses and resumes painting, such as "Space" or "Ctrl+F8". It works while Rust is in front.</summary>
    public string StartPauseHotkey { get; set; } = "Space";

    /// <summary>The key that stops painting for good.</summary>
    public string AbortHotkey { get; set; } = "Esc";
}

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as JSON in the user's roaming app-data folder.
/// </summary>
public interface IAppSettingsService
{
    /// <summary>The live settings. Mutate it, then call <see cref="Save"/> to persist.</summary>
    AppSettings Current { get; }

    /// <summary>Loads settings from disk, leaving defaults in place if there is no (valid) file.</summary>
    void Initialize();

    /// <summary>Writes <see cref="Current"/> to disk. Never throws.</summary>
    void Save();
}
