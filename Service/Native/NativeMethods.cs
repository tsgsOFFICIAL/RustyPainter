using System.Runtime.InteropServices;

namespace RustyPainter.Service.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeRect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativePoint
{
    public int X;
    public int Y;
}

/// <summary>The few Win32 calls needed to locate another process's window.</summary>
internal static class NativeMethods
{
    internal delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll")]
    internal static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [DllImport("user32.dll")]
    internal static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    internal static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    internal static extern bool GetClientRect(nint hWnd, out NativeRect rect);

    [DllImport("user32.dll")]
    internal static extern bool ClientToScreen(nint hWnd, ref NativePoint point);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();
}
