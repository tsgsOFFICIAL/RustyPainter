using RustyPainter.ViewModels.Mvvm;
using RustyPainter.Interface;
using RustyPainter.Painting;
using Avalonia.Threading;
using System;

namespace RustyPainter.ViewModels;

/// <summary>A page that reacts to the global hotkeys while it is shown.</summary>
public interface IHotkeyTarget
{
    void OnHotkey(HotkeyAction action);
}

/// <summary>What painting has been set up to do.</summary>
/// <param name="Request">What to paint and where.</param>
/// <param name="Options">How to paint it.</param>
/// <param name="Timing">The pace.</param>
public sealed record PaintSession(PaintRequest Request, PaintOptions Options, PaintTiming Timing);

/// <summary>
/// The page between Studio and the painting itself: it says what is about to happen, checks that Rust is running,
/// lets the user choose the pace, and waits for the start key.
/// </summary>
public sealed class GetReadyViewModel : ViewModelBase, IHotkeyTarget, IDisposable
{
    private readonly IGameWindowService _game;
    private readonly IAppSettingsService _settings;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    private GameWindow? _window;
    private bool _clearFirst;
    private double _speed;
    private double _nudgeX, _nudgeY;
    private string _hotkeyProblem = "";

    public GetReadyViewModel(PaintRequest request, IGameWindowService game, IAppSettingsService settings, Hotkey startKey, Hotkey abortKey)
    {
        Request = request;
        _game = game;
        _settings = settings;
        StartKeyText = startKey.Display;
        AbortKeyText = abortKey.Display;

        _clearFirst = settings.Current.ClearBeforePainting;
        _speed = Math.Clamp(settings.Current.PaintSpeed, 1, 100);

        StartCommand = new RelayCommand(Start, () => IsRustRunning);
        BackCommand = new RelayCommand(() => BackRequested?.Invoke());

        Refresh();
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
    }

    /// <summary>What is going to be painted.</summary>
    public PaintRequest Request { get; }

    /// <summary>Raised when the user goes back to Studio.</summary>
    public event Action? BackRequested;

    /// <summary>Raised when painting is to begin.</summary>
    public event Action<PaintSession>? StartRequested;

    public RelayCommand StartCommand { get; }
    public RelayCommand BackCommand { get; }

    public string PictureName => Request.PictureName;
    public string SurfaceName => Request.Surface.Name;
    public string StartKeyText { get; }
    public string AbortKeyText { get; }

    public string StrokesText => $"{Request.Plan.StrokeCount:N0} strokes";
    public string ColorsText => Request.Mode == Imaging.ColorMode.Swatches
        ? $"{Request.Plan.ColorsUsed} swatches"
        : $"{Request.Plan.ColorsUsed} typed colors";

    /// <summary>A rough time for the whole painting at the chosen pace.</summary>
    public string TimeText => PaintTimeEstimator.Describe(
        PaintTimeEstimator.Estimate(Request.Plan.Strokes, PaintTiming.FromSpeed((int)Speed), Request.Mode, Request.Surface.ScrollOut, ClearFirst));

    /// <summary>How the game has to be set up before the start key is pressed.</summary>
    public string SetupText => Request.Surface.ScrollOut > 0
        ? $"Open the paint screen of the {SurfaceName} in Rust. Scrolling out {Request.Surface.ScrollOut} times is done for you."
        : $"Open the paint screen of the {SurfaceName} in Rust.";

    // ---- Rust ----

    public bool IsRustRunning => _window is not null;

    public string RustText => _window is { } window
        ? $"Rust is running at {window.Width} x {window.Height}."
        : "Rust is not running, or its window cannot be found. Start the game first.";

    /// <summary>Looks for the game again. Runs once a second while the page is shown.</summary>
    public void Refresh()
    {
        GameWindow? found = _game.Find();
        bool changed = (found is null) != (_window is null) || found?.Width != _window?.Width || found?.Height != _window?.Height;
        _window = found;

        if (!changed)
            return;

        OnPropertyChanged(nameof(IsRustRunning));
        OnPropertyChanged(nameof(RustText));
        StartCommand.RaiseCanExecuteChanged();
    }

    // ---- options ----

    /// <summary>Whether the canvas is cleared first. Leave it off to paint over what is there.</summary>
    public bool ClearFirst
    {
        get => _clearFirst;
        set
        {
            if (!SetProperty(ref _clearFirst, value))
                return;

            _settings.Current.ClearBeforePainting = value;
            _settings.Save();
            OnPropertyChanged(nameof(TimeText));
        }
    }

    /// <summary>The pace from 1 (slowest and most reliable) to 100 (fastest).</summary>
    public double Speed
    {
        get => _speed;
        set
        {
            if (!SetProperty(ref _speed, Math.Round(Math.Clamp(value, 1, 100))))
                return;

            _settings.Current.PaintSpeed = (int)_speed;
            _settings.Save();
            OnPropertyChanged(nameof(SpeedText));
            OnPropertyChanged(nameof(TimeText));
        }
    }

    public string SpeedText => $"{Speed:0}%";

    /// <summary>Moves the painting this many screen pixels to the right, for lining it up exactly. From -30 to 30.</summary>
    public double NudgeX
    {
        get => _nudgeX;
        set
        {
            if (SetProperty(ref _nudgeX, Math.Round(Math.Clamp(value, -30, 30))))
                OnPropertyChanged(nameof(NudgeXText));
        }
    }

    /// <summary>Moves the painting this many screen pixels down. From -30 to 30.</summary>
    public double NudgeY
    {
        get => _nudgeY;
        set
        {
            if (SetProperty(ref _nudgeY, Math.Round(Math.Clamp(value, -30, 30))))
                OnPropertyChanged(nameof(NudgeYText));
        }
    }

    public string NudgeXText => $"{NudgeX:+0;-0;0} px";
    public string NudgeYText => $"{NudgeY:+0;-0;0} px";

    /// <summary>Says a hotkey could not be registered, for example because another program has it.</summary>
    public string HotkeyProblem
    {
        get => _hotkeyProblem;
        set
        {
            if (SetProperty(ref _hotkeyProblem, value))
                OnPropertyChanged(nameof(HasHotkeyProblem));
        }
    }

    public bool HasHotkeyProblem => HotkeyProblem.Length > 0;

    // ---- starting ----

    public void OnHotkey(HotkeyAction action)
    {
        if (action == HotkeyAction.StartPause && StartCommand.CanExecute(null))
            Start();
    }

    private void Start()
    {
        PaintOptions options = new(Request.Mode, Request.BlockSize, ClearFirst, NudgeX: NudgeX, NudgeY: NudgeY);
        StartRequested?.Invoke(new PaintSession(Request, options, PaintTiming.FromSpeed((int)Speed)));
    }

    public void Dispose() => _timer.Stop();
}
