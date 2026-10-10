using RustyPainter.ViewModels.Mvvm;
using RustyPainter.Service.Logging;
using RustyPainter.Interface;
using RustyPainter.Controls;
using RustyPainter.Painting;
using RustyPainter.Service;
using Avalonia.Threading;
using System.Collections.Generic;
using System.Linq;
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
    private readonly IGameWindowService _game;
    private readonly IHotkeyService _hotkeys;
    private readonly IPaintEngineFactory _engines;
    private readonly IHologramService _hologram;

    private HomeViewModel? _home;
    private StudioViewModel? _studio;
    private SettingsViewModel? _settingsPage;
    private ComingSoonViewModel? _comingSoon;
    private GetReadyViewModel? _ready;
    private PaintingViewModel? _painting;

    private ViewModelBase? _currentPage;

    /// <summary>Creates the shell, its sidebar, and the initial Home page.</summary>
    public MainWindowViewModel(IUpdateService updates, IDialogService dialogs, IAppSettingsService settings, ISurfaceService surfaces,
        IGameWindowService game, IHotkeyService hotkeys, IPaintEngineFactory engines, IHologramService hologram)
    {
        _settings = settings;
        _game = game;
        _hotkeys = hotkeys;
        _engines = engines;
        _hologram = hologram;
        _hotkeys.Pressed += action => Dispatcher.UIThread.Post(() => RouteHotkey(action));
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
        // Painting is not interrupted by clicking around. The page stays until painting is over or stopped.
        if (_painting is { IsOver: false })
        {
            CurrentPage = _painting;
            Sidebar.Highlight(SidebarPage.Studio);
            return;
        }

        CloseSession();

        CurrentPage = page switch
        {
            SidebarPage.Home => _home ??= new HomeViewModel(Sidebar),
            SidebarPage.Studio => GetStudio(),
            SidebarPage.Settings => _settingsPage ??= new SettingsViewModel(_settings, _updates, _dialogs),
            _ => ShowComingSoon(page)
        };
    }

    // ---- painting ----

    private StudioViewModel GetStudio()
    {
        if (_studio is not null)
            return _studio;

        _studio = new StudioViewModel(_surfaces) { PaintSpeed = _settings.Current.PaintSpeed };
        _studio.PaintRequested += OpenGetReady;
        return _studio;
    }

    /// <summary>Shows the get-ready page for what Studio prepared and switches the hotkeys on.</summary>
    private void OpenGetReady(PaintRequest request)
    {
        CloseSession();

        IReadOnlyDictionary<HotkeyAction, Hotkey> keys = HotkeyDefaults.FromSettings(_settings.Current.StartPauseHotkey, _settings.Current.AbortHotkey);
        GetReadyViewModel ready = new(request, _game, _surfaces, _settings, _hologram, keys[HotkeyAction.StartPause], keys[HotkeyAction.Abort]);
        ready.BackRequested += () => Navigate(SidebarPage.Studio);
        ready.StartRequested += StartPainting;

        _ready = ready;
        CurrentPage = ready;
        // Only the start key is taken while waiting, so Escape and the others still work in the game's own screens.
        ready.HotkeyProblem = HotkeyProblem(_hotkeys.Start(new Dictionary<HotkeyAction, Hotkey> { [HotkeyAction.StartPause] = keys[HotkeyAction.StartPause] }), keys);
    }

    private void StartPainting(PaintSession session)
    {
        IReadOnlyDictionary<HotkeyAction, Hotkey> keys = HotkeyDefaults.FromSettings(_settings.Current.StartPauseHotkey, _settings.Current.AbortHotkey);

        _ready?.Dispose();
        _ready = null;

        PaintingViewModel painting = new(session, _game, _surfaces, _engines, keys[HotkeyAction.StartPause].Display, keys[HotkeyAction.Abort].Display);
        painting.DoneRequested += () =>
        {
            CloseSession();
            CurrentPage = GetStudio();
        };

        _painting = painting;
        CurrentPage = painting;

        // Painting uses both keys. They are given back the moment painting is over, so Escape works in the game again.
        painting.HotkeyNotice = HotkeyProblem(_hotkeys.Start(keys), keys);
        painting.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PaintingViewModel.IsOver) && painting.IsOver)
                _hotkeys.Stop();
        };

        _ = painting.StartAsync();
    }

    private static string HotkeyProblem(IReadOnlyList<HotkeyAction> failed, IReadOnlyDictionary<HotkeyAction, Hotkey> keys)
        => failed.Count == 0
            ? ""
            : $"{string.Join(" and ", failed.Select(action => keys[action].Display))} could not be set up. You can still start, pause and stop with the buttons.";

    /// <summary>Ends the get-ready or painting page, gives the hotkeys back and stops any painting that is still going.</summary>
    private void CloseSession()
    {
        _hotkeys.Stop();

        _ready?.Dispose();
        _ready = null;

        _painting?.Dispose();
        _painting = null;
    }

    private void RouteHotkey(HotkeyAction action)
    {
        if (_painting is { } painting)
            painting.OnHotkey(action);
        else
            _ready?.OnHotkey(action);
    }

    /// <summary>Returns the shared "coming soon" placeholder, retitled for the given page.</summary>
    private ComingSoonViewModel ShowComingSoon(SidebarPage page)
    {
        ComingSoonViewModel comingSoon = _comingSoon ??= new ComingSoonViewModel();
        comingSoon.Title = page.ToString();
        return comingSoon;
    }
}
