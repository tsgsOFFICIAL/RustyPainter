using RustyPainter.ViewModels.Mvvm;
using RustyPainter.Imaging;
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
/// shows the picture over the game where it will be painted, lets the user choose the pace and nudge the position,
/// and waits for the start key.
/// </summary>
public sealed class GetReadyViewModel : ViewModelBase, IHotkeyTarget, IDisposable
{
    private readonly IGameWindowService _game;
    private readonly ISurfaceService _surfaces;
    private readonly IAppSettingsService _settings;
    private readonly IHologramService _hologram;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly PixelBuffer _picture;

    private GameWindow? _window;
    private bool _clearFirst = true;
    private bool _showHologram = true;
    private double _hologramOpacity = 55;
    private double _speed;
    private double _nudgeX, _nudgeY;
    private string _hotkeyProblem = "";

    public GetReadyViewModel(PaintRequest request, IGameWindowService game, ISurfaceService surfaces, IAppSettingsService settings,
        IHologramService hologram, Hotkey startKey, Hotkey abortKey)
    {
        Request = request;
        _game = game;
        _surfaces = surfaces;
        _settings = settings;
        _hologram = hologram;
        _picture = request.Plan.Render();
        StartKeyText = startKey.Display;
        AbortKeyText = abortKey.Display;

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
    public string ColorsText => Request.Mode == ColorMode.Swatches
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
        bool changed = (found is null) != (_window is null) || found?.Width != _window?.Width || found?.Height != _window?.Height
            || found?.Client != _window?.Client;
        _window = found;

        if (!changed)
            return;

        OnPropertyChanged(nameof(IsRustRunning));
        OnPropertyChanged(nameof(RustText));
        StartCommand.RaiseCanExecuteChanged();
        UpdateHologram();
    }

    // ---- the picture over the game ----

    /// <summary>Whether the picture is shown over the game where it will be painted.</summary>
    public bool ShowHologram
    {
        get => _showHologram;
        set
        {
            if (SetProperty(ref _showHologram, value))
                UpdateHologram();
        }
    }

    /// <summary>How solid the picture over the game is, from 10 to 90 percent.</summary>
    public double HologramOpacity
    {
        get => _hologramOpacity;
        set
        {
            if (!SetProperty(ref _hologramOpacity, Math.Round(Math.Clamp(value, 10, 90))))
                return;

            OnPropertyChanged(nameof(HologramOpacityText));
            UpdateHologram();
        }
    }

    public string HologramOpacityText => $"{HologramOpacity:0}%";

    /// <summary>The place on screen the painting goes, with the fine tuning counted in. Empty while Rust is not running.</summary>
    public ScreenRect? PaintArea
    {
        get
        {
            if (_window is null)
                return null;

            try
            {
                return _surfaces.Resolve(Request.Surface, _window).Canvas.Offset(NudgeX, NudgeY);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }

    private void UpdateHologram()
    {
        if (ShowHologram && PaintArea is { } area)
            _hologram.Show(_picture, area, HologramOpacity / 100);
        else
            _hologram.Hide();
    }

    // ---- options ----

    /// <summary>
    /// Whether the canvas is cleared first. It is on every time, because painting over an old picture is rarely wanted.
    /// </summary>
    public bool ClearFirst
    {
        get => _clearFirst;
        set
        {
            if (SetProperty(ref _clearFirst, value))
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
            if (!SetProperty(ref _nudgeX, Math.Round(Math.Clamp(value, -30, 30))))
                return;

            OnPropertyChanged(nameof(NudgeXText));
            UpdateHologram();
        }
    }

    /// <summary>Moves the painting this many screen pixels down. From -30 to 30.</summary>
    public double NudgeY
    {
        get => _nudgeY;
        set
        {
            if (!SetProperty(ref _nudgeY, Math.Round(Math.Clamp(value, -30, 30))))
                return;

            OnPropertyChanged(nameof(NudgeYText));
            UpdateHologram();
        }
    }

    public string NudgeXText => $"{NudgeX:+0;-0;0} px";
    public string NudgeYText => $"{NudgeY:+0;-0;0} px";

    /// <summary>Says a hotkey could not be set up, for example because another program has it.</summary>
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
        // The picture must be gone before the first stroke, or it would be painted over.
        _hologram.Hide();

        PaintOptions options = new(Request.Mode, Request.BlockSize, ClearFirst, NudgeX: NudgeX, NudgeY: NudgeY);
        StartRequested?.Invoke(new PaintSession(Request, options, PaintTiming.FromSpeed((int)Speed)));
    }

    public void Dispose()
    {
        _timer.Stop();
        _hologram.Hide();
    }
}
