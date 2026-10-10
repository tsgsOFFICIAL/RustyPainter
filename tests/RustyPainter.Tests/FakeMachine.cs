using RustyPainter.Imaging;
using RustyPainter.Interface;
using RustyPainter.Painting;

namespace RustyPainter.Tests;

/// <summary>Records everything the painter does and plays the part of the machine, so no real input is ever sent.</summary>
internal sealed class FakeMachine : IPaintInput, IPaintSensors
{
    public List<string> Log { get; } = [];
    public bool GameInFront { get; set; } = true;
    public ScreenPoint Cursor { get; set; }
    public bool ButtonDown { get; private set; }
    public Rgb? ToggleColor { get; set; } = new Rgb(120, 120, 120);

    /// <summary>Runs before each recorded action, so a test can interfere at an exact moment.</summary>
    public Action<string>? Before { get; set; }

    public ScreenPoint CursorPosition => Cursor;
    public bool IsGameInFront() => GameInFront;
    public Rgb? ReadColor(ScreenPoint point) => ToggleColor;

    private void Record(string what)
    {
        Before?.Invoke(what);
        Log.Add(what);
    }

    public void MoveTo(ScreenPoint point)
    {
        Record($"move {point.X:0},{point.Y:0}");
        Cursor = point;
    }

    public void LeftDown()
    {
        Record("down");
        ButtonDown = true;
    }

    public void LeftUp()
    {
        Record("up");
        ButtonDown = false;
    }

    public void Scroll(int notches) => Record($"scroll {notches}");

    public void PressKey(char key) => Record($"key {key}");
}

/// <summary>A game window that is always there, or never.</summary>
internal sealed class FakeGame(GameWindow? window) : IGameWindowService
{
    public GameWindow? Window { get; set; } = window;

    public GameWindow? Find(string processName = IGameWindowService.RustProcessName) => Window;

    public static GameWindow Rust(int width = 2560, int height = 1440) => new(1, 1, new ScreenRect(0, 0, width, height), false, true);
}

/// <summary>Remembers the hotkeys it was asked for, and lets a test press them.</summary>
internal sealed class FakeHotkeys : IHotkeyService
{
    public event Action<HotkeyAction>? Pressed;
    public bool IsActive { get; private set; }
    public int Starts { get; private set; }
    public IReadOnlyDictionary<HotkeyAction, Hotkey>? Bindings { get; private set; }
    public List<HotkeyAction> Refuse { get; } = [];

    public IReadOnlyList<HotkeyAction> Start(IReadOnlyDictionary<HotkeyAction, Hotkey> bindings)
    {
        Starts++;
        IsActive = true;
        Bindings = bindings;
        return Refuse.ToList();
    }

    public void Stop() => IsActive = false;

    public void Press(HotkeyAction action) => Pressed?.Invoke(action);

    public void Dispose() => Stop();
}

/// <summary>Makes painters that work on a <see cref="FakeMachine"/> and never wait.</summary>
internal sealed class FakeEngines(FakeMachine machine) : IPaintEngineFactory
{
    public PaintEngine? Last { get; private set; }

    public PaintEngine Create(PaintTiming timing)
        => Last = new PaintEngine(machine, machine, timing, (_, token) =>
        {
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        });
}

/// <summary>Remembers what would be shown over the game.</summary>
internal sealed class FakeHologram : IHologramService
{
    public bool IsShown { get; private set; }
    public PixelBuffer? Picture { get; private set; }
    public ScreenRect Area { get; private set; }
    public double Opacity { get; private set; }
    public int Shows { get; private set; }
    public int Hides { get; private set; }

    public void Show(PixelBuffer picture, ScreenRect area, double opacity)
    {
        IsShown = true;
        Picture = picture;
        Area = area;
        Opacity = opacity;
        Shows++;
    }

    public void Hide()
    {
        IsShown = false;
        Hides++;
    }
}

/// <summary>Remembers what would be drawn over the game while painting.</summary>
internal sealed class FakeOverlay : IStatusOverlayService
{
    public bool IsShown { get; private set; }
    public object? Content { get; private set; }
    public ScreenRect Area { get; private set; }
    public int Shows { get; private set; }

    public void Show(object content, ScreenRect area)
    {
        IsShown = true;
        Content = content;
        Area = area;
        Shows++;
    }

    public void Hide() => IsShown = false;
}
