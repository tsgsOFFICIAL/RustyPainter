using System.Linq;
using RustyPainter.ViewModels.Mvvm;
using RustyPainter.Localization;
using RustyPainter.Service.Logging;
using RustyPainter.Interface;
using RustyPainter.Painting;
using Avalonia.Threading;
using System.Diagnostics;
using System.Threading.Tasks;
using System;

namespace RustyPainter.ViewModels;

/// <summary>
/// The page shown while painting: how far it has come, why it stopped if it did, and the buttons for pausing and
/// stopping. The hotkeys do the same, and work while Rust is in front.
/// </summary>
public sealed class PaintingViewModel : ViewModelBase, IHotkeyTarget, IDisposable
{
    private readonly PaintSession _session;
    private readonly IGameWindowService _game;
    private readonly ISurfaceService _surfaces;
    private readonly IPaintEngineFactory _factory;
    private readonly Stopwatch _active = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly TimeSpan _estimate;
    private readonly Hotkey _startKey, _abortKey;

    private PaintEngine? _engine;
    private Task _run = Task.CompletedTask;
    private PaintState _state = PaintState.Idle;
    private PaintProgress _progress;
    private string _detail = "";
    private string _hotkeyNotice = "";

    public PaintingViewModel(PaintSession session, IGameWindowService game, ISurfaceService surfaces, IPaintEngineFactory factory, Hotkey startKey, Hotkey abortKey)
    {
        _session = session;
        _game = game;
        _surfaces = surfaces;
        _factory = factory;
        _startKey = startKey;
        _abortKey = abortKey;

        _estimate = PaintTimeEstimator.Estimate(session.Request.Plans.Select(plan => plan.Strokes).ToList(), session.Timing, session.Request.Mode,
            session.Request.Surface.ScrollOut, session.Options.ClearFirst);

        _progress = new PaintProgress(0, 0, 0, session.Request.StrokeCount);

        PauseResumeCommand = new RelayCommand(OnPauseResume, () => State is PaintState.Running or PaintState.Paused);
        AbortCommand = new RelayCommand(Abort, () => State is PaintState.Running or PaintState.Paused);
        DoneCommand = new RelayCommand(() => DoneRequested?.Invoke(), () => IsOver);

        _timer.Tick += (_, _) => RefreshTimes();
    }

    /// <summary>Raised when the user leaves the page after painting ended.</summary>
    public event Action? DoneRequested;

    public RelayCommand PauseResumeCommand { get; }
    public RelayCommand AbortCommand { get; }
    public RelayCommand DoneCommand { get; }

    public string PictureName => _session.Request.PictureName;
    public string SurfaceName => Loc.SurfaceName(_session.Request.Surface);
    public string StartKeyText => _startKey.Localized;
    public string AbortKeyText => _abortKey.Localized;

    /// <summary>What is being painted and where, under the title.</summary>
    public string Subtitle => Loc.Format("PictureOnSurface", Loc.PictureName(PictureName), SurfaceName);

    public PaintState State
    {
        get => _state;
        private set
        {
            if (!SetProperty(ref _state, value))
                return;

            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsPaused));
            OnPropertyChanged(nameof(IsOver));
            OnPropertyChanged(nameof(IsFailed));
            OnPropertyChanged(nameof(PauseResumeText));
            OnPropertyChanged(nameof(OverlayTitle));
            OnPropertyChanged(nameof(OverlayLine));
            OnPropertyChanged(nameof(Hint));
            PauseResumeCommand.RaiseCanExecuteChanged();
            AbortCommand.RaiseCanExecuteChanged();
            DoneCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsRunning => State == PaintState.Running;
    public bool IsPaused => State == PaintState.Paused;
    public bool IsFailed => State == PaintState.Failed;

    /// <summary>Whether painting has ended, one way or another.</summary>
    public bool IsOver => State is PaintState.Finished or PaintState.Aborted or PaintState.Failed;

    public string StatusText => State switch
    {
        PaintState.Idle => Loc.Get("PaintStarting"),
        PaintState.Running => Loc.Get("PaintPainting"),
        PaintState.Paused => Loc.Get("PaintPaused"),
        PaintState.Finished => Loc.Get("PaintFinished"),
        PaintState.Aborted => Loc.Get("PaintStopped"),
        PaintState.Failed => Loc.Get("PaintFailed"),
        _ => ""
    };

    /// <summary>Why painting is paused or failed, in plain words.</summary>
    public string Detail
    {
        get => _detail;
        private set
        {
            if (SetProperty(ref _detail, value))
            {
                OnPropertyChanged(nameof(HasDetail));
                OnPropertyChanged(nameof(OverlayLine));
            }
        }
    }

    public bool HasDetail => Detail.Length > 0;

    /// <summary>Says the hotkeys could not be set up, when that is so.</summary>
    public string HotkeyNotice
    {
        get => _hotkeyNotice;
        set
        {
            if (SetProperty(ref _hotkeyNotice, value))
                OnPropertyChanged(nameof(HasHotkeyNotice));
        }
    }

    public bool HasHotkeyNotice => HotkeyNotice.Length > 0;

    /// <summary>What to do next, for the current state.</summary>
    public string Hint => State switch
    {
        PaintState.Paused => Loc.Format("PaintHintPaused", StartKeyText, AbortKeyText),
        PaintState.Running => Loc.Format("PaintHintRunning", StartKeyText, AbortKeyText),
        PaintState.Finished => Loc.Get("PaintHintFinished"),
        PaintState.Aborted => Loc.Get("PaintHintStopped"),
        PaintState.Failed => Loc.Get("PaintHintStopped"),
        _ => ""
    };

    public string PauseResumeText => IsPaused ? Loc.Get("Resume") : Loc.Get("Pause");

    // ---- the box over the game ----

    /// <summary>The headline of the box over the game, such as "Painting 42%".</summary>
    public string OverlayTitle => State switch
    {
        PaintState.Running => Loc.Format("PaintTitleRunning", Fraction * 100),
        PaintState.Paused => Loc.Format("PaintTitlePaused", Fraction * 100),
        PaintState.Finished => Loc.Get("PaintFinished"),
        PaintState.Aborted => Loc.Get("PaintStopped"),
        PaintState.Failed => Loc.Get("PaintFailed"),
        _ => Loc.Get("PaintStarting")
    };

    /// <summary>The line under the progress bar of the box over the game: what the keys do, or why it stopped.</summary>
    public string OverlayLine => State switch
    {
        PaintState.Running => Loc.Format("PaintLineRunning", StartKeyText, AbortKeyText, RemainingText),
        PaintState.Paused => HasDetail ? $"{Detail} {Loc.Format("PaintLinePaused", StartKeyText, AbortKeyText)}" : Loc.Format("PaintLinePaused", StartKeyText, AbortKeyText),
        PaintState.Finished => Loc.Get("PaintLineFinished"),
        PaintState.Aborted => Loc.Get("PaintHintStopped"),
        PaintState.Failed => Detail,
        _ => ""
    };

    // ---- progress ----

    public double Fraction => _progress.Fraction;

    public string ProgressText => Loc.Format("PaintProgress", _progress.StrokesDone, _progress.StrokeCount);

    public string ElapsedText => Format(_active.Elapsed);

    public string RemainingText
    {
        get
        {
            if (IsOver)
                return "";

            TimeSpan left = _progress.StrokesDone >= 25
                ? TimeSpan.FromSeconds(_active.Elapsed.TotalSeconds * (_progress.StrokeCount - _progress.StrokesDone) / _progress.StrokesDone)
                : _estimate * (1 - Fraction);

            return Format(left);
        }
    }

    private static string Format(TimeSpan time) => time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{time.Minutes}:{time.Seconds:00}";

    private void RefreshTimes()
    {
        OnPropertyChanged(nameof(ElapsedText));
        OnPropertyChanged(nameof(RemainingText));
        OnPropertyChanged(nameof(OverlayLine));
    }

    // ---- running ----

    /// <summary>
    /// Builds the steps for the game's current resolution and starts working through them. Nothing is sent until Rust is
    /// the window in front, so starting from this app just waits for the user to switch to the game.
    /// </summary>
    /// <returns>The task that ends when painting does, which tests wait for.</returns>
    public Task StartAsync()
    {
        GameWindow? window = _game.Find();
        if (window is null)
        {
            Detail = Loc.Get("PaintRustNotRunning");
            State = PaintState.Failed;
            return Task.CompletedTask;
        }

        PaintScript script;
        try
        {
            ResolvedLayout layout = _surfaces.Resolve(_session.Request.Surface, window);
            script = PaintScriptBuilder.Build(_session.Request.Plans, layout, _session.Options);
        }
        catch (Exception ex)
        {
            AppLog.Error("PaintingViewModel", "Could not prepare the painting.", ex);
            Detail = Loc.Get("PaintCouldNotPrepare");
            State = PaintState.Failed;
            return Task.CompletedTask;
        }

        PaintEngine engine = _factory.Create(_session.Timing);
        _engine = engine;
        engine.StateChanged += state => Dispatcher.UIThread.Post(() => OnEngineState(engine, state));
        engine.ProgressChanged += progress => Dispatcher.UIThread.Post(() => OnProgress(progress));

        _timer.Start();
        _run = Task.Run(() => engine.RunAsync(script));
        return _run;
    }

    private void OnEngineState(PaintEngine engine, PaintState state)
    {
        State = state;

        if (state == PaintState.Running)
            _active.Start();
        else
            _active.Stop();

        Detail = state switch
        {
            PaintState.Paused => engine.PauseReason,
            PaintState.Failed => engine.FailureMessage,
            _ => ""
        };

        if (state is PaintState.Finished or PaintState.Aborted or PaintState.Failed)
        {
            _timer.Stop();
            RefreshTimes();
        }
    }

    private void OnProgress(PaintProgress progress)
    {
        _progress = progress;
        OnPropertyChanged(nameof(Fraction));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(OverlayTitle));
        RefreshTimes();
    }

    public void OnHotkey(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.StartPause:
                OnPauseResume();
                break;
            case HotkeyAction.Abort:
                Abort();
                break;
        }
    }

    private void OnPauseResume()
    {
        if (_engine is null)
            return;

        if (IsPaused)
            _engine.Resume();
        else if (IsRunning)
            _engine.Pause(Loc.Format("PaintPausedWith", StartKeyText));
    }

    private void Abort() => _engine?.Abort();

    public void Dispose()
    {
        _timer.Stop();
        _engine?.Abort();
    }
}
