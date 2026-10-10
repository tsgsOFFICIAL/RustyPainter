using RustyPainter.ViewModels.Mvvm;
using RustyPainter.Service.Logging;
using RustyPainter.Interface;
using RustyPainter.Controls;
using RustyPainter.Service;
using System.Threading.Tasks;
using System;

namespace RustyPainter.ViewModels;

/// <summary>
/// The application shell: owns the sidebar, swaps <see cref="CurrentPage"/> in response to its
/// navigation requests, and checks for updates once the window has opened.
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly IAppSettingsService _settings;
    private readonly IUpdateService _updates;
    private readonly IDialogService _dialogs;
    private readonly ISurfaceService _surfaces;

    private HomeViewModel? _home;
    private StudioViewModel? _studio;
    private SettingsViewModel? _settingsPage;
    private ComingSoonViewModel? _comingSoon;

    private ViewModelBase? _currentPage;

    /// <summary>Creates the shell, its sidebar, and the initial Home page.</summary>
    public MainWindowViewModel(IUpdateService updates, IDialogService dialogs, IAppSettingsService settings, ISurfaceService surfaces)
    {
        _settings = settings;
        _updates = updates;
        _dialogs = dialogs;
        _surfaces = surfaces;

        Sidebar = new SidebarViewModel();
        Sidebar.NavigationRequested += (_, page) => Navigate(page);

        VersionText = Utility.GetDisplayVersion();
        OpenGitHubCommand = new RelayCommand(() => Utility.OpenUrl(ProjectLinks.GitHub));
        OpenDiscordCommand = new RelayCommand(() => Utility.OpenUrl(ProjectLinks.Discord));
        OpenKofiCommand = new RelayCommand(() => Utility.OpenUrl(ProjectLinks.KoFi));

        CurrentPage = _home = new HomeViewModel(Sidebar);
    }

    /// <summary>The nav rail's view model.</summary>
    public SidebarViewModel Sidebar { get; }

    /// <summary>The app's display version, shown in the footer.</summary>
    public string VersionText { get; }

    /// <summary>Opens the project's GitHub page.</summary>
    public RelayCommand OpenGitHubCommand { get; }

    /// <summary>Opens the project's Discord server.</summary>
    public RelayCommand OpenDiscordCommand { get; }

    /// <summary>Opens the project's Ko-fi page. Lives in the footer so it's reachable from any page.</summary>
    public RelayCommand OpenKofiCommand { get; }

    /// <summary>The view model of the page currently shown in the main content area.</summary>
    public ViewModelBase? CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    /// <summary>
    /// Checks GitHub for a newer release and, if one exists, prompts the user via
    /// <see cref="IDialogService"/>. Fails silently (e.g. offline) so it never blocks startup.
    /// Call once, after the window has opened.
    /// </summary>
    public async Task CheckForUpdatesOnStartupAsync()
    {
        // After a self-update the new process is launched with --updated; make sure Windows' Installed
        // apps list shows the version that is actually running.
        _ = Task.Run(InstallRegistration.SyncRegisteredVersion);

        if (!_settings.Current.CheckForUpdatesOnStartup)
            return;

        UpdateInfo? update;
        try
        {
            update = await _updates.CheckForUpdateAsync();
        }
        catch (Exception ex)
        {
            AppLog.Warn("MainWindowViewModel", "Startup update check failed.", ex);
            return;
        }

        if (update is null)
            return;

        // Auto-update skips the prompt entirely: ApplyUpdateAsync swaps the install and terminates
        // this process, so there's nothing to show afterwards. Opt-in only - see AppSettings.AutoUpdate.
        if (_settings.Current.AutoUpdate)
        {
            try
            {
                await _updates.ApplyUpdateAsync(update);
            }
            catch (Exception ex)
            {
                AppLog.Warn("MainWindowViewModel", "Automatic update failed; falling back to the prompt.", ex);
            }
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

        await _dialogs.ShowUpdateAvailableAsync(_updates, update, changes);
    }

    /// <summary>Swaps <see cref="CurrentPage"/> to match the sidebar selection.</summary>
    private void Navigate(SidebarPage page)
    {
        CurrentPage = page switch
        {
            SidebarPage.Home => _home ??= new HomeViewModel(Sidebar),
            SidebarPage.Studio => _studio ??= new StudioViewModel(_surfaces),
            SidebarPage.Settings => _settingsPage ??= new SettingsViewModel(_settings, _updates, _dialogs),
            _ => ShowComingSoon(page)
        };
    }

    /// <summary>Returns the shared "coming soon" placeholder, retitled for the given page.</summary>
    private ComingSoonViewModel ShowComingSoon(SidebarPage page)
    {
        ComingSoonViewModel comingSoon = _comingSoon ??= new ComingSoonViewModel();
        comingSoon.Title = page.ToString();
        return comingSoon;
    }
}
