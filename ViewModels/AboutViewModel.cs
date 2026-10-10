using RustyPainter.ViewModels.Mvvm;
using RustyPainter.Service.Logging;
using RustyPainter.Localization;
using RustyPainter.Interface;
using System.Globalization;
using System.Threading.Tasks;
using System.Reflection;
using System.Linq;
using System.IO;
using System;

namespace RustyPainter.ViewModels;

/// <summary>Drives the About page: what the app is, which build is running, the changelog, the update check and the project links.</summary>
public sealed class AboutViewModel : ViewModelBase
{
    private const string Unknown = "N/A";

    private readonly IUpdateService _updates;
    private readonly IDialogService _dialogs;

    private Func<string> _updateStatus = () => "";
    private bool _isUpdateStatusVisible;
    private bool _isCheckingForUpdates;

    public AboutViewModel(IUpdateService updates, IDialogService dialogs)
    {
        _updates = updates;
        _dialogs = dialogs;

        VersionText = Utility.GetDisplayVersion();
        BuildText = GetBuildText();
        OsText = $"{Utility.GetFriendlyOsName()} ({Environment.OSVersion.Version})";

        ViewChangelogCommand = new AsyncRelayCommand(() => _dialogs.ShowChangelogAsync(BundledChangelog.Load()));
        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync, () => !IsCheckingForUpdates);
        OpenGitHubCommand = new RelayCommand(() => Utility.OpenUrl(ProjectLinks.GitHub));
        OpenDiscordCommand = new RelayCommand(() => Utility.OpenUrl(ProjectLinks.Discord));
        OpenKofiCommand = new RelayCommand(() => Utility.OpenUrl(ProjectLinks.KoFi));
    }

    /// <summary>The version that is running, including any nightly suffix.</summary>
    public string VersionText { get; }

    /// <summary>When this build was made. Two installs with the same version and another build explain a lot of bug reports.</summary>
    public string BuildText { get; }

    /// <summary>The operating system and its build number.</summary>
    public string OsText { get; }

    /// <summary>The licence, which is the same free one for everyone.</summary>
    public string LicenseText => Loc.Get("AboutLicenseFree");

    public AsyncRelayCommand ViewChangelogCommand { get; }
    public AsyncRelayCommand CheckForUpdatesCommand { get; }
    public RelayCommand OpenGitHubCommand { get; }
    public RelayCommand OpenDiscordCommand { get; }
    public RelayCommand OpenKofiCommand { get; }

    public string UpdateStatusText => _updateStatus();
    public bool IsUpdateStatusVisible { get => _isUpdateStatusVisible; private set => SetProperty(ref _isUpdateStatusVisible, value); }

    public bool IsCheckingForUpdates
    {
        get => _isCheckingForUpdates;
        private set
        {
            if (SetProperty(ref _isCheckingForUpdates, value))
                CheckForUpdatesCommand.RaiseCanExecuteChanged();
        }
    }

    private void SetStatus(Func<string> status)
    {
        _updateStatus = status;
        OnPropertyChanged(nameof(UpdateStatusText));
    }

    /// <summary>Reads the build time that was stamped into the program when it was built, or the time its file was written.</summary>
    private static string GetBuildText()
    {
        try
        {
            string? stamped = typeof(AboutViewModel).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => attribute.Key == "BuildTimestamp")?.Value;

            if (!string.IsNullOrWhiteSpace(stamped))
                return stamped;

            string exePath = Utility.GetExePath();
            return File.Exists(exePath) ? File.GetLastWriteTime(exePath).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : Unknown;
        }
        catch (Exception)
        {
            return Unknown;
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        IsCheckingForUpdates = true;
        SetStatus(() => Loc.Get("SettingsChecking"));
        IsUpdateStatusVisible = true;

        UpdateInfo? update;
        try
        {
            update = await _updates.CheckForUpdateAsync();
        }
        catch (Exception ex)
        {
            AppLog.Warn("AboutViewModel", "Manual update check failed.", ex);
            string reason = ex.Message;
            SetStatus(() => Loc.Format("SettingsCheckFailed", reason));
            IsCheckingForUpdates = false;
            return;
        }

        if (update is null)
        {
            SetStatus(() => Loc.Get("SettingsUpToDate"));
            IsCheckingForUpdates = false;
            return;
        }

        IsUpdateStatusVisible = false;
        IsCheckingForUpdates = false;

        string changes;
        try
        {
            changes = await RemoteChangelog.GetChangesSinceAsync(Utility.GetDisplayVersion());
        }
        catch
        {
            changes = "";
        }

        await _dialogs.ShowUpdateAvailableAsync(_updates, update, changes);
    }
}
