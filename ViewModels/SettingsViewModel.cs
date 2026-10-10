using RustyPainter.ViewModels.Mvvm;
using RustyPainter.Localization;
using RustyPainter.Service.Logging;
using RustyPainter.Interface;
using RustyPainter.Painting;
using System.Collections.Generic;
using System.IO;
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

    private readonly ILocalizationService _localization;
    private readonly IThemeService? _theme;
    private Func<string> _updateStatus = () => "";
    private bool _isUpdateStatusVisible;
    private bool _isCheckingForUpdates;
    private readonly string _libraryFolder;

    /// <param name="libraryFolder">The folder the Library keeps projects in, to open from the page. Empty when there is none.</param>
    public SettingsViewModel(IAppSettingsService settings, IUpdateService updates, IDialogService dialogs, string libraryFolder = "", ILocalizationService? localization = null, IThemeService? theme = null)
    {
        _localization = localization ?? Loc.Service;
        _theme = theme;
        _libraryFolder = libraryFolder;
        _settings = settings;
        _updates = updates;
        _dialogs = dialogs;

        _checkForUpdatesOnStartup = settings.Current.CheckForUpdatesOnStartup;
        _autoUpdate = settings.Current.AutoUpdate;
        _verboseLogging = settings.Current.VerboseLogging;

        VersionText = Utility.GetDisplayVersion();
        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync);
        OpenLogsCommand = new RelayCommand(AppLog.OpenLogDirectory);
        CaptureStartPauseCommand = new RelayCommand(() => BeginCapture(HotkeyAction.StartPause));
        CaptureAbortCommand = new RelayCommand(() => BeginCapture(HotkeyAction.Abort));
        ResetHotkeysCommand = new RelayCommand(ResetHotkeys);
        ResetPaintingCommand = new RelayCommand(ResetPainting);
        OpenLibraryFolderCommand = new RelayCommand(OpenLibraryFolder, () => _libraryFolder.Length > 0);
    }

    /// <summary>The running version, including any nightly suffix.</summary>
    public string VersionText { get; }

    /// <summary>The line under the Updates title.</summary>
    public string RunningVersionText => Loc.Format("SettingsRunningVersionLine", VersionText);

    // ---- language and theme ----

    /// <summary>The languages to pick from, each named in its own language.</summary>
    public LanguageOption[] LanguageOptions { get; } =
    [
        LanguageOption.Create(AppLanguage.English, "English", "gb"),
        LanguageOption.Create(AppLanguage.Danish, "Dansk", "dk"),
        LanguageOption.Create(AppLanguage.Russian, "Русский", "ru")
    ];

    /// <summary>The language in use. Picking another one changes every text at once and is remembered.</summary>
    public LanguageOption SelectedLanguage
    {
        get => Array.Find(LanguageOptions, option => option.Language == _localization.Current) ?? LanguageOptions[0];
        set
        {
            if (value is null || value.Language == _localization.Current)
                return;

            _localization.SetLanguage(value.Language);
        }
    }

    /// <summary>The looks to pick from. A new list each time, so the names follow the language.</summary>
    public ThemeOption[] ThemeOptions => [new(AppTheme.System), new(AppTheme.Light), new(AppTheme.Dark)];

    /// <summary>The look in use. System follows Windows.</summary>
    public ThemeOption SelectedTheme
    {
        get => new(_settings.Current.Theme);
        set
        {
            if (value is null || value.Theme == _settings.Current.Theme)
                return;

            if (_theme is not null)
                _theme.SetTheme(value.Theme);
            else
            {
                _settings.Current.Theme = value.Theme;
                _settings.Save();
            }

            OnPropertyChanged();
        }
    }

    public AsyncRelayCommand CheckForUpdatesCommand { get; }
    public RelayCommand OpenLogsCommand { get; }
    public RelayCommand CaptureStartPauseCommand { get; }
    public RelayCommand CaptureAbortCommand { get; }
    public RelayCommand ResetHotkeysCommand { get; }

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

    // ---- painting ----

    /// <summary>How fast painting is to begin with, from 1 (slowest, most reliable) to 100 (fastest).</summary>
    public double PaintSpeed
    {
        get => _settings.Current.PaintSpeed;
        set
        {
            int speed = (int)Math.Round(Math.Clamp(value, 1, 100));
            if (speed == _settings.Current.PaintSpeed)
                return;

            _settings.Current.PaintSpeed = speed;
            _settings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(PaintSpeedText));
        }
    }

    public string PaintSpeedText => $"{_settings.Current.PaintSpeed}%";

    /// <summary>Whether the get-ready page starts by showing the picture over the game.</summary>
    public bool ShowHologram
    {
        get => _settings.Current.ShowHologram;
        set
        {
            if (value == _settings.Current.ShowHologram)
                return;

            _settings.Current.ShowHologram = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>How solid the picture over the game is to begin with, from 10 to 90 percent.</summary>
    public double HologramStrength
    {
        get => _settings.Current.HologramStrength;
        set
        {
            int strength = (int)Math.Round(Math.Clamp(value, 10, 90));
            if (strength == _settings.Current.HologramStrength)
                return;

            _settings.Current.HologramStrength = strength;
            _settings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(HologramStrengthText));
        }
    }

    public string HologramStrengthText => $"{_settings.Current.HologramStrength}%";

    /// <summary>Whether the box with the progress is drawn over the game while painting.</summary>
    public bool ShowStatusBox
    {
        get => _settings.Current.ShowStatusBox;
        set
        {
            if (value == _settings.Current.ShowStatusBox)
                return;

            _settings.Current.ShowStatusBox = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    public RelayCommand ResetPaintingCommand { get; }
    public RelayCommand OpenLibraryFolderCommand { get; }

    /// <summary>Puts the speed, the picture over the game, the status box and the painting keys back as they were at the start.</summary>
    private void ResetPainting()
    {
        AppSettings defaults = new();
        _settings.Current.PaintSpeed = defaults.PaintSpeed;
        _settings.Current.ShowHologram = defaults.ShowHologram;
        _settings.Current.HologramStrength = defaults.HologramStrength;
        _settings.Current.ShowStatusBox = defaults.ShowStatusBox;
        ResetHotkeys();

        OnPropertyChanged(nameof(PaintSpeed));
        OnPropertyChanged(nameof(PaintSpeedText));
        OnPropertyChanged(nameof(ShowHologram));
        OnPropertyChanged(nameof(HologramStrength));
        OnPropertyChanged(nameof(HologramStrengthText));
        OnPropertyChanged(nameof(ShowStatusBox));
    }

    private void OpenLibraryFolder()
    {
        Directory.CreateDirectory(_libraryFolder);
        Utility.OpenUrl(_libraryFolder);
    }

    // ---- painting hotkeys ----

    private HotkeyAction? _capturing;
    private string _hotkeyMessage = "";

    /// <summary>The key that starts, pauses and resumes painting, as shown to the user.</summary>
    public string StartPauseKeyText => CurrentKeys()[HotkeyAction.StartPause].Localized;

    /// <summary>The key that stops painting, as shown to the user.</summary>
    public string AbortKeyText => CurrentKeys()[HotkeyAction.Abort].Localized;

    /// <summary>What the start and pause button says: the key, or a prompt while it is waiting for a new one.</summary>
    public string StartPauseButtonText => _capturing == HotkeyAction.StartPause ? Loc.Get("SettingsPressAKey") : StartPauseKeyText;

    /// <summary>What the stop button says: the key, or a prompt while it is waiting for a new one.</summary>
    public string AbortButtonText => _capturing == HotkeyAction.Abort ? Loc.Get("SettingsPressAKey") : AbortKeyText;

    /// <summary>The key that is being chosen, or <see langword="null"/> when none is.</summary>
    public HotkeyAction? CapturingAction => _capturing;

    /// <summary>Why the last key could not be used, or an empty string.</summary>
    public string HotkeyMessage
    {
        get => _hotkeyMessage;
        private set
        {
            if (SetProperty(ref _hotkeyMessage, value))
                OnPropertyChanged(nameof(HasHotkeyMessage));
        }
    }

    public bool HasHotkeyMessage => HotkeyMessage.Length > 0;

    private IReadOnlyDictionary<HotkeyAction, Hotkey> CurrentKeys()
        => HotkeyDefaults.FromSettings(_settings.Current.StartPauseHotkey, _settings.Current.AbortHotkey);

    /// <summary>Waits for the next key press to become the key for <paramref name="action"/>.</summary>
    public void BeginCapture(HotkeyAction action)
    {
        _capturing = action;
        HotkeyMessage = "";
        RaiseHotkeyTexts();
    }

    /// <summary>Stops waiting for a key and leaves things as they were.</summary>
    public void CancelCapture()
    {
        if (_capturing is null)
            return;

        _capturing = null;
        RaiseHotkeyTexts();
    }

    /// <summary>
    /// Offers a pressed key to the choice that is waiting for one.
    /// </summary>
    /// <param name="virtualKey">The Windows virtual key code.</param>
    /// <param name="modifiers">The modifiers held, as <see cref="Hotkey.Alt"/>, <see cref="Hotkey.Ctrl"/> and <see cref="Hotkey.Shift"/>.</param>
    /// <returns>Whether a choice was waiting, so the key press was used up.</returns>
    public bool CaptureKey(uint virtualKey, uint modifiers)
    {
        if (_capturing is not { } action)
            return false;

        if (!Hotkey.IsUsableKey(virtualKey))
        {
            HotkeyMessage = Loc.Get("SettingsKeyNotUsable");
            return true;
        }

        Hotkey chosen = new(modifiers, virtualKey);
        IReadOnlyDictionary<HotkeyAction, Hotkey> keys = CurrentKeys();
        HotkeyAction other = action == HotkeyAction.StartPause ? HotkeyAction.Abort : HotkeyAction.StartPause;

        if (keys[other] == chosen)
        {
            HotkeyMessage = Loc.Format("SettingsKeyInUse", chosen.Localized, Loc.Get(other == HotkeyAction.Abort ? "SettingsKeyInUseStop" : "SettingsKeyInUsePause"));
            return true;
        }

        if (action == HotkeyAction.StartPause)
            _settings.Current.StartPauseHotkey = chosen.Display;
        else
            _settings.Current.AbortHotkey = chosen.Display;

        _settings.Save();
        _capturing = null;
        HotkeyMessage = "";
        RaiseHotkeyTexts();
        return true;
    }

    private void ResetHotkeys()
    {
        _capturing = null;
        _settings.Current.StartPauseHotkey = HotkeyDefaults.StartPause.Display;
        _settings.Current.AbortHotkey = HotkeyDefaults.Abort.Display;
        _settings.Save();
        HotkeyMessage = "";
        RaiseHotkeyTexts();
    }

    private void RaiseHotkeyTexts()
    {
        OnPropertyChanged(nameof(StartPauseKeyText));
        OnPropertyChanged(nameof(AbortKeyText));
        OnPropertyChanged(nameof(StartPauseButtonText));
        OnPropertyChanged(nameof(AbortButtonText));
        OnPropertyChanged(nameof(CapturingAction));
    }

    public string UpdateStatusText => _updateStatus();

    private void SetUpdateStatus(Func<string> status)
    {
        _updateStatus = status;
        OnPropertyChanged(nameof(UpdateStatusText));
    }
    public bool IsUpdateStatusVisible { get => _isUpdateStatusVisible; private set => SetProperty(ref _isUpdateStatusVisible, value); }
    public bool IsCheckingForUpdates { get => _isCheckingForUpdates; private set => SetProperty(ref _isCheckingForUpdates, value); }

    /// <summary>Checks GitHub now, and either reports "up to date" or opens the update prompt.</summary>
    private async Task CheckForUpdatesAsync()
    {
        IsCheckingForUpdates = true;
        SetUpdateStatus(() => Loc.Get("SettingsChecking"));
        IsUpdateStatusVisible = true;

        UpdateInfo? update;
        try
        {
            update = await _updates.CheckForUpdateAsync();
        }
        catch (Exception ex)
        {
            AppLog.Warn("SettingsViewModel", "Manual update check failed.", ex);
            string reason = ex.Message;
            SetUpdateStatus(() => Loc.Format("SettingsCheckFailed", reason));
            IsCheckingForUpdates = false;
            return;
        }

        if (update is null)
        {
            SetUpdateStatus(() => Loc.Get("SettingsUpToDate"));
            IsCheckingForUpdates = false;
            return;
        }

        string changes;
        try
        {
            changes = await RemoteChangelog.GetChangesSinceAsync(Loc.Service.Current, Utility.GetDisplayVersion());
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
