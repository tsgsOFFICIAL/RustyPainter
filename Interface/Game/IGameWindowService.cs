using System;

namespace RustyPainter.Interface;

/// <summary>
/// The game's window as found on screen.
/// </summary>
/// <param name="Handle">The native window handle.</param>
/// <param name="ProcessId">The id of the game process.</param>
/// <param name="Client">
/// The drawable area of the window in screen pixels, which is the resolution the game renders at and where
/// it sits on the desktop. Empty while the window is minimized.
/// </param>
/// <param name="IsMinimized">Whether the window is minimized, so there is nothing on screen to click.</param>
/// <param name="IsForeground">Whether the window currently has focus.</param>
public sealed record GameWindow(nint Handle, int ProcessId, ScreenRect Client, bool IsMinimized, bool IsForeground)
{
    /// <summary>The game resolution in pixels (the width of <see cref="Client"/>).</summary>
    public int Width => (int)Math.Round(Client.Width);

    /// <summary>The game resolution in pixels (the height of <see cref="Client"/>).</summary>
    public int Height => (int)Math.Round(Client.Height);
}

/// <summary>
/// Finds the running game and reports its window size and position.
/// </summary>
public interface IGameWindowService
{
    /// <summary>The process name of the Rust client, without ".exe".</summary>
    const string RustProcessName = "RustClient";

    /// <summary>
    /// Looks for a visible window belonging to the process called <paramref name="processName"/> and returns
    /// the largest one, or <see langword="null"/> if the process is not running or has no window.
    /// </summary>
    GameWindow? Find(string processName = RustProcessName);
}
