using RustyPainter.ViewModels.Mvvm;
using RustyPainter.Service.Logging;
using RustyPainter.Interface;
using System.Threading.Tasks;
using System;

namespace RustyPainter.ViewModels;

/// <summary>
/// Drives the Settings page: update behaviour, manual update check, and log tools. Settings persist
/// the moment they change - there is no separate Save button.
/// </summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private readonly IAppSettingsService _settings;
    private readonly IUpdateService _updates;
    private readonly IDialogService _dialogs;

    private bool _checkForUpdatesOnStartup;
    private bool _autoUpdate;
    private bool _verboseLogging;

    private string _updateStatusText = "";
    private bool _isUpdateStatusVisible;
    private bool _isCheckingForUpdates;

    public SettingsViewModel(IAppSettingsService settings, IUpdateService updates, IDialogService dialogs)
    {
        _settings = settings;
        _updates = updates;
        _dialogs = dialogs;

        _checkForUpdatesOnStartup = settings.Current.CheckForUpdatesOnStartup;
        _autoUpdate = settings.Current.AutoUpdate;
        _verboseLogging = settings.Current.VerboseLogging;

        VersionText = Utility.GetDisplayVersion();
        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync);
        OpenLogsCommand = new RelayCommand(AppLog.OpenLogDirectory);
    }

    /// <summary>The running version, including any nightly suffix.</summary>
    public string VersionText { get; }

    public AsyncRelayCommand CheckForUpdatesCommand { get; }
    public RelayCommand OpenLogsCommand { get; }

    /// <summary>Whether the app looks for a newer release each time it opens.</summary>
    public bool CheckForUpdatesOnStartup
    {
        get => _checkForUpdatesOnStartup;
        set
        {
            if (!SetProperty(ref _checkForUpdatesOnStartup, value))
                return;

            _settings.Current.CheckForUpdatesOnStartup = value;
            _settings.Save();
            OnPropertyChanged(nameof(CanAutoUpdate));
        }
    }

    /// <summary>Whether an available update is downloaded and applied without asking.</summary>
    public bool AutoUpdate
    {
        get => _autoUpdate;
        set
        {
            if (!SetProperty(ref _autoUpdate, value))
                return;

            _settings.Current.AutoUpdate = value;
            _settings.Save();
        }
    }

    /// <summary>
    /// Whether the auto-update toggle is usable. Automatic updates only happen off the back of the
    /// startup check, so they need it enabled.
    /// </summary>
    public bool CanAutoUpdate => CheckForUpdatesOnStartup;

    /// <summary>Whether every log level is written, including Debug.</summary>
    public bool VerboseLogging
    {
        get => _verboseLogging;
        set
        {
            if (!SetProperty(ref _verboseLogging, value))
                return;

            _settings.Current.VerboseLogging = value;
            _settings.Save();
            AppLog.ApplyVerbose(value);
        }
    }

    public string UpdateStatusText { get => _updateStatusText; private set => SetProperty(ref _updateStatusText, value); }
    public bool IsUpdateStatusVisible { get => _isUpdateStatusVisible; private set => SetProperty(ref _isUpdateStatusVisible, value); }
    public bool IsCheckingForUpdates { get => _isCheckingForUpdates; private set => SetProperty(ref _isCheckingForUpdates, value); }

    /// <summary>Checks GitHub now, and either reports "up to date" or opens the update prompt.</summary>
    private async Task CheckForUpdatesAsync()
    {
        IsCheckingForUpdates = true;
        UpdateStatusText = "Checking for updates...";
        IsUpdateStatusVisible = true;

        UpdateInfo? update;
        try
        {
            update = await _updates.CheckForUpdateAsync();
        }
        catch (Exception ex)
        {
            AppLog.Warn("SettingsViewModel", "Manual update check failed.", ex);
            UpdateStatusText = $"Could not check for updates: {ex.Message}";
            IsCheckingForUpdates = false;
            return;
        }

        if (update is null)
        {
            UpdateStatusText = "You're up to date.";
            IsCheckingForUpdates = false;
            return;
        }

        string changes;
        try
        {
            changes = await RemoteChangelog.GetChangesSinceAsync(Utility.GetDisplayVersion());
        }
        catch
        {
            changes = "";
        }

        IsUpdateStatusVisible = false;
        IsCheckingForUpdates = false;

        await _dialogs.ShowUpdateAvailableAsync(_updates, update, changes);
    }
}
