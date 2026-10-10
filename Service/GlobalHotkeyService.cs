using RustyPainter.Interface;
using RustyPainter.Painting;
using RustyPainter.Service.Logging;
using RustyPainter.Service.Native;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System;

namespace RustyPainter.Service;

/// <inheritdoc cref="IHotkeyService" />
/// <remarks>
/// A keyboard hook is used instead of registering the keys with Windows. Registering would take a key such as Space or
/// Escape from every program for as long as the page is open, while the hook can leave a key alone unless the game is the
/// window in front. The hook runs on a thread of its own that does nothing else, so it answers at once whatever
/// the user interface is doing, and it passes on every key it does not use. Keys that other programs send
/// to the system, such as the painter's own typing, are never taken.
/// </remarks>
public sealed class GlobalHotkeyService(Func<bool> isGameInFront, bool acceptInjectedKeys = false) : IHotkeyService
{
    private readonly object _lock = new();
    private readonly HashSet<uint> _held = [];
    private readonly HashSet<uint> _swallowed = [];

    // The hook has to stay referenced for as long as it is installed, or the garbage collector would remove it.
    private HotkeyNativeMethods.HookProc? _callback;
    private HotkeyMatcher _matcher = new(new Dictionary<HotkeyAction, Hotkey>());
    private Thread? _thread;
    private uint _threadId;

    /// <summary>Uses the real game window to tell when the game is in front.</summary>
    public GlobalHotkeyService(IGameWindowService game) : this(new ForegroundGameCheck(game).IsGameInFront)
    {
    }

    public event Action<HotkeyAction>? Pressed;

    public bool IsActive
    {
        get { lock (_lock) return _thread is not null; }
    }

    public IReadOnlyList<HotkeyAction> Start(IReadOnlyDictionary<HotkeyAction, Hotkey> bindings)
    {
        Stop();

        using ManualResetEventSlim ready = new();
        bool installed = false;

        _matcher = new HotkeyMatcher(bindings);
        _held.Clear();
        _swallowed.Clear();

        Thread thread = new(() => Listen(ready, ok => installed = ok)) { IsBackground = true, Name = "Hotkeys" };

        lock (_lock)
            _thread = thread;

        thread.Start();
        ready.Wait(TimeSpan.FromSeconds(5));

        if (installed)
        {
            AppLog.Info("GlobalHotkeyService", $"Listening for {string.Join(", ", bindings.Select(pair => $"{pair.Value.Display} ({pair.Key})"))}.");
            return [];
        }

        lock (_lock)
            _thread = null;

        return bindings.Keys.ToList();
    }

    public void Stop()
    {
        Thread? thread;
        uint id;

        lock (_lock)
        {
            thread = _thread;
            id = _threadId;
            _thread = null;
            _threadId = 0;
        }

        if (thread is null)
            return;

        if (id != 0)
            HotkeyNativeMethods.PostThreadMessage(id, HotkeyNativeMethods.MessageQuit, 0, 0);

        thread.Join(TimeSpan.FromSeconds(2));
    }

    public void Dispose() => Stop();

    private void Listen(ManualResetEventSlim ready, Action<bool> report)
    {
        // Looking at the queue once makes Windows create it, so a quit message sent right away is not lost.
        HotkeyNativeMethods.PeekMessage(out _, 0, 0, 0, 0);

        _callback = OnKey;
        nint hook = HotkeyNativeMethods.SetWindowsHookEx(HotkeyNativeMethods.HookKeyboardLowLevel, _callback, HotkeyNativeMethods.GetModuleHandle(null), 0);

        if (hook == 0)
        {
            AppLog.Warn("GlobalHotkeyService", $"Could not install the keyboard hook ({Marshal.GetLastWin32Error()}).");
            report(false);
            ready.Set();
            return;
        }

        try
        {
            lock (_lock)
                _threadId = HotkeyNativeMethods.GetCurrentThreadId();

            report(true);
            ready.Set();

            while (HotkeyNativeMethods.GetMessage(out _, 0, 0, 0) > 0)
            {
            }
        }
        finally
        {
            HotkeyNativeMethods.UnhookWindowsHookEx(hook);
            _callback = null;
            ready.Set();
        }
    }

    private nint OnKey(int code, nuint wParam, nint lParam)
    {
        if (code < 0)
            return HotkeyNativeMethods.CallNextHookEx(0, code, wParam, lParam);

        try
        {
            KeyboardHookData key = Marshal.PtrToStructure<KeyboardHookData>(lParam);
            uint message = (uint)wParam;
            bool down = message is HotkeyNativeMethods.MessageKeyDown or HotkeyNativeMethods.MessageSysKeyDown;
            bool up = message is HotkeyNativeMethods.MessageKeyUp or HotkeyNativeMethods.MessageSysKeyUp;

            // Input sent by programs, the painter included, is never a hotkey.
            if (!acceptInjectedKeys && (key.Flags & HotkeyNativeMethods.FlagInjected) != 0)
                return HotkeyNativeMethods.CallNextHookEx(0, code, wParam, lParam);

            if (down)
            {
                HotkeyAction? action = _matcher.Match(key.VirtualKey, HeldModifiers(), isGameInFront());

                if (action is { } matched)
                {
                    AppLog.Debug("GlobalHotkeyService", $"{matched} pressed.");

                    // A key that is held down repeats, which must not press the hotkey again.
                    if (_held.Add(key.VirtualKey))
                        Pressed?.Invoke(matched);

                    _swallowed.Add(key.VirtualKey);
                    return 1;
                }
            }
            else if (up)
            {
                _held.Remove(key.VirtualKey);

                // The game saw no key go down, so it must not see one come up.
                if (_swallowed.Remove(key.VirtualKey))
                    return 1;
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("GlobalHotkeyService", "Handling a key failed.", ex);
        }

        return HotkeyNativeMethods.CallNextHookEx(0, code, wParam, lParam);
    }

    private static uint HeldModifiers()
    {
        uint modifiers = 0;

        if ((HotkeyNativeMethods.GetAsyncKeyState(HotkeyNativeMethods.KeyControl) & 0x8000) != 0)
            modifiers |= Hotkey.Ctrl;

        if ((HotkeyNativeMethods.GetAsyncKeyState(HotkeyNativeMethods.KeyAlt) & 0x8000) != 0)
            modifiers |= Hotkey.Alt;

        if ((HotkeyNativeMethods.GetAsyncKeyState(HotkeyNativeMethods.KeyShift) & 0x8000) != 0)
            modifiers |= Hotkey.Shift;

        return modifiers;
    }
}

/// <summary>
/// Tells whether the game is in front without searching for it every time. It runs for every key anywhere on the
/// machine, so it must be cheap: the game's process is only looked up again every few seconds.
/// </summary>
public sealed class ForegroundGameCheck
{
    /// <summary>How long a lookup of the game is trusted before it is made again.</summary>
    public static readonly TimeSpan LookupEvery = TimeSpan.FromSeconds(2);

    private readonly IGameWindowService _game;
    private readonly Func<int> _foregroundProcess;
    private readonly Func<TimeSpan> _clock;

    private int _processId;
    private TimeSpan? _lookedUpAt;

    public ForegroundGameCheck(IGameWindowService game) : this(game, ForegroundProcessId, () => Stopwatch.GetElapsedTime(0))
    {
    }

    /// <param name="game">Finds the game's window.</param>
    /// <param name="foregroundProcess">The process that owns the window in front, or 0. Tests give their own.</param>
    /// <param name="clock">A clock that only goes forward. Tests give their own.</param>
    public ForegroundGameCheck(IGameWindowService game, Func<int> foregroundProcess, Func<TimeSpan> clock)
    {
        _game = game;
        _foregroundProcess = foregroundProcess;
        _clock = clock;
    }

    public bool IsGameInFront()
    {
        int front = _foregroundProcess();
        if (front == 0)
            return false;

        if (_processId != 0 && front == _processId)
            return true;

        // Not the game as far as is known. Look the game up again, but not for every key that is pressed in another program.
        TimeSpan now = _clock();
        if (_lookedUpAt is not { } last || now - last >= LookupEvery)
        {
            _lookedUpAt = now;
            _processId = _game.Find()?.ProcessId ?? 0;
        }

        return _processId != 0 && front == _processId;
    }

    private static int ForegroundProcessId()
    {
        nint window = NativeMethods.GetForegroundWindow();
        if (window == 0)
            return 0;

        NativeMethods.GetWindowThreadProcessId(window, out uint pid);
        return (int)pid;
    }
}
