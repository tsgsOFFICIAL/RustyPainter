using System.Runtime.InteropServices;

namespace RustyPainter.Service.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeMessage
{
    public nint Window;
    public uint Message;
    public nuint WParam;
    public nint LParam;
    public uint Time;
    public NativePoint Point;
}

/// <summary>The Win32 calls behind global hotkeys.</summary>
internal static class HotkeyNativeMethods
{
    internal const uint ModifierNoRepeat = 0x4000;
    internal const uint MessageHotkey = 0x0312;
    internal const uint MessageQuit = 0x0012;
    internal const uint PeekRemove = 0x0001;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UnregisterHotKey(nint window, int id);

    [DllImport("user32.dll")]
    internal static extern int GetMessage(out NativeMessage message, nint window, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    internal static extern bool PeekMessage(out NativeMessage message, nint window, uint filterMin, uint filterMax, uint remove);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool PostThreadMessage(uint thread, uint message, nuint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();
}
