using RustyPainter.Service.Native;
using RustyPainter.Interface;
using System.Collections.Generic;
using System.Diagnostics;
using System;

namespace RustyPainter.Service;

/// <inheritdoc cref="IGameWindowService" />
/// <remarks>
/// The numbers are physical pixels because the app runs per-monitor DPI aware (Avalonia sets that up), which is
/// what mouse input needs as well. The client area is used rather than the monitor, so a windowed game is
/// handled and not only a fullscreen one.
/// </remarks>
public sealed class GameWindowService : IGameWindowService
{
    public GameWindow? Find(string processName = IGameWindowService.RustProcessName)
    {
        Process[] processes = Process.GetProcessesByName(processName);

        try
        {
            if (processes.Length == 0)
                return null;

            HashSet<uint> processIds = [];
            foreach (Process process in processes)
                processIds.Add((uint)process.Id);

            nint foreground = NativeMethods.GetForegroundWindow();
            GameWindow? best = null;

            NativeMethods.EnumWindows((handle, _) =>
            {
                NativeMethods.GetWindowThreadProcessId(handle, out uint owner);
                if (!processIds.Contains(owner) || !NativeMethods.IsWindowVisible(handle))
                    return true;

                GameWindow candidate = Describe(handle, (int)owner, foreground);

                // Prefer the biggest window, so a small helper window never wins over the game itself.
                if (best is null || Area(candidate) > Area(best))
                    best = candidate;

                return true;
            }, 0);

            return best;
        }
        finally
        {
            foreach (Process process in processes)
                process.Dispose();
        }
    }

    private static GameWindow Describe(nint handle, int processId, nint foreground)
    {
        bool minimized = NativeMethods.IsIconic(handle);
        ScreenRect client = default;

        if (!minimized && NativeMethods.GetClientRect(handle, out NativeRect rect))
        {
            // GetClientRect is relative to the window itself, so its origin has to be moved to screen space.
            NativePoint origin = default;
            if (NativeMethods.ClientToScreen(handle, ref origin))
                client = new ScreenRect(origin.X, origin.Y, origin.X + rect.Right - rect.Left, origin.Y + rect.Bottom - rect.Top);
        }

        return new GameWindow(handle, processId, client, minimized, handle == foreground);
    }

    private static double Area(GameWindow window) => window.Client.Width * window.Client.Height;
}
