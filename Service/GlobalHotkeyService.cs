using RustyPainter.Painting;
using RustyPainter.Service.Logging;
using RustyPainter.Service.Native;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System;

namespace RustyPainter.Service;

/// <inheritdoc cref="IHotkeyService" />
/// <remarks>
/// Windows delivers a hotkey to the thread that registered it, so the keys live on a thread of their own that does
/// nothing but wait for them. That keeps the keys working whatever the user interface is doing, and whichever window is in front.
/// </remarks>
public sealed class GlobalHotkeyService : IHotkeyService
{
    private readonly object _lock = new();
    private Thread? _thread;
    private uint _threadId;

    public event Action<HotkeyAction>? Pressed;

    public bool IsActive
    {
        get { lock (_lock) return _thread is not null; }
    }

    public IReadOnlyList<HotkeyAction> Start(IReadOnlyDictionary<HotkeyAction, Hotkey> bindings)
    {
        Stop();

        List<HotkeyAction> failed = [];
        using ManualResetEventSlim ready = new();

        Thread thread = new(() => Listen(bindings, failed, ready)) { IsBackground = true, Name = "Hotkeys" };

        lock (_lock)
            _thread = thread;

        thread.Start();
        ready.Wait(TimeSpan.FromSeconds(5));

        return failed;
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

    private void Listen(IReadOnlyDictionary<HotkeyAction, Hotkey> bindings, List<HotkeyAction> failed, ManualResetEventSlim ready)
    {
        // Looking at the queue once makes Windows create it, so a quit message sent right away is not lost.
        HotkeyNativeMethods.PeekMessage(out _, 0, 0, 0, 0);

        List<int> registered = [];

        try
        {
            foreach ((HotkeyAction action, Hotkey key) in bindings)
            {
                int id = (int)action + 1;

                if (HotkeyNativeMethods.RegisterHotKey(0, id, key.Modifiers | HotkeyNativeMethods.ModifierNoRepeat, key.VirtualKey))
                {
                    registered.Add(id);
                }
                else
                {
                    AppLog.Warn("GlobalHotkeyService", $"Could not register {key.Display} for {action}.");
                    failed.Add(action);
                }
            }

            lock (_lock)
                _threadId = HotkeyNativeMethods.GetCurrentThreadId();

            ready.Set();

            while (HotkeyNativeMethods.GetMessage(out NativeMessage message, 0, 0, 0) > 0)
            {
                if (message.Message != HotkeyNativeMethods.MessageHotkey)
                    continue;

                HotkeyAction action = (HotkeyAction)((int)message.WParam - 1);

                try
                {
                    Pressed?.Invoke(action);
                }
                catch (Exception ex)
                {
                    AppLog.Error("GlobalHotkeyService", $"A handler for {action} failed.", ex);
                }
            }
        }
        finally
        {
            foreach (int id in registered)
                HotkeyNativeMethods.UnregisterHotKey(0, id);

            ready.Set();
        }
    }
}
