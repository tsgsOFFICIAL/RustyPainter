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

[StructLayout(LayoutKind.Sequential)]
internal struct KeyboardHookData
{
    public uint VirtualKey;
    public uint ScanCode;
    public uint Flags;
    public uint Time;
    public nint ExtraInfo;
}

/// <summary>The Win32 calls behind the keyboard hook that listens for the hotkeys.</summary>
internal static class HotkeyNativeMethods
{
    internal const int HookKeyboardLowLevel = 13;
    internal const uint MessageKeyDown = 0x0100;
    internal const uint MessageKeyUp = 0x0101;
    internal const uint MessageSysKeyDown = 0x0104;
    internal const uint MessageSysKeyUp = 0x0105;
    internal const uint MessageQuit = 0x0012;
    internal const uint FlagInjected = 0x10;

    internal const int KeyShift = 0x10;
    internal const int KeyControl = 0x11;
    internal const int KeyAlt = 0x12;

    internal delegate nint HookProc(int code, nuint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SetWindowsHookEx(int hookId, HookProc callback, nint module, uint thread);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    internal static extern nint CallNextHookEx(nint hook, int code, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    internal static extern int GetMessage(out NativeMessage message, nint window, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    internal static extern bool PeekMessage(out NativeMessage message, nint window, uint filterMin, uint filterMax, uint remove);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool PostThreadMessage(uint thread, uint message, nuint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint GetModuleHandle(string? name);
}
