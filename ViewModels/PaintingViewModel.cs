using RustyPainter.ViewModels.Mvvm;
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

    private PaintEngine? _engine;
    private Task _run = Task.CompletedTask;
    private PaintState _state = PaintState.Idle;
    private PaintProgress _progress;
    private string _detail = "";

    public PaintingViewModel(PaintSession session, IGameWindowService game, ISurfaceService surfaces, IPaintEngineFactory factory, string startKey, string abortKey)
    {
        _session = session;
        _game = game;
        _surfaces = surfaces;
        _factory = factory;
        StartKeyText = startKey;
        AbortKeyText = abortKey;

        _estimate = PaintTimeEstimator.Estimate(session.Request.Plan.Strokes, session.Timing, session.Request.Mode,
            session.Request.Surface.ScrollOut, session.Options.ClearFirst);

        _progress = new PaintProgress(0, 0, 0, session.Request.Plan.StrokeCount);

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
    public string SurfaceName => _session.Request.Surface.Name;
    public string StartKeyText { get; }
    public string AbortKeyText { get; }

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
        PaintState.Idle => "Starting",
        PaintState.Running => "Painting",
        PaintState.Paused => "Paused",
        PaintState.Finished => "Finished",
        PaintState.Aborted => "Stopped",
        PaintState.Failed => "Could not continue",
        _ => ""
    };

    /// <summary>Why painting is paused or failed, in plain words.</summary>
    public string Detail
    {
        get => _detail;
        private set
        {
            if (SetProperty(ref _detail, value))
                OnPropertyChanged(nameof(HasDetail));
        }
    }

    public bool HasDetail => Detail.Length > 0;

    /// <summary>What to do next, for the current state.</summary>
    public string Hint => State switch
    {
        PaintState.Paused => $"Switch to Rust and press {StartKeyText} to carry on, or {AbortKeyText} to stop.",
        PaintState.Running => $"Press {StartKeyText} to pause or {AbortKeyText} to stop. Moving the mouse also pauses.",
        PaintState.Finished => "The picture is on the canvas. Save it in the game.",
        PaintState.Aborted => "What was painted so far is still on the canvas.",
        PaintState.Failed => "What was painted so far is still on the canvas.",
        _ => ""
    };

    public string PauseResumeText => IsPaused ? "Resume" : "Pause";

    // ---- progress ----

    public double Fraction => _progress.Fraction;

    public string ProgressText => $"{_progress.StrokesDone:N0} of {_progress.StrokeCount:N0} strokes";

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
            Detail = "Rust is not running, so there is nothing to paint on.";
            State = PaintState.Failed;
            return Task.CompletedTask;
        }

        PaintScript script;
        try
        {
            ResolvedLayout layout = _surfaces.Resolve(_session.Request.Surface, window);
            script = PaintScriptBuilder.Build(_session.Request.Plan, layout, _session.Options);
        }
        catch (Exception ex)
        {
            AppLog.Error("PaintingViewModel", "Could not prepare the painting.", ex);
            Detail = "The painting could not be prepared for this game resolution.";
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
            _engine.Pause($"Paused with {StartKeyText}.");
    }

    private void Abort() => _engine?.Abort();

    public void Dispose()
    {
        _timer.Stop();
        _engine?.Abort();
    }
}
