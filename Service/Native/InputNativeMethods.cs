using System.Runtime.InteropServices;

namespace RustyPainter.Service.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct MouseInput
{
    public int Dx;
    public int Dy;
    public uint MouseData;
    public uint Flags;
    public uint Time;
    public nint ExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct KeyboardInput
{
    public ushort VirtualKey;
    public ushort ScanCode;
    public uint Flags;
    public uint Time;
    public nint ExtraInfo;
}

[StructLayout(LayoutKind.Explicit)]
internal struct InputUnion
{
    [FieldOffset(0)] public MouseInput Mouse;
    [FieldOffset(0)] public KeyboardInput Keyboard;
}

[StructLayout(LayoutKind.Sequential)]
internal struct InputRecord
{
    public uint Type;
    public InputUnion Union;
}

/// <summary>The Win32 calls that send input and look at the screen.</summary>
internal static class InputNativeMethods
{
    internal const uint InputMouse = 0;
    internal const uint InputKeyboard = 1;

    internal const uint MouseMove = 0x0001;
    internal const uint MouseLeftDown = 0x0002;
    internal const uint MouseLeftUp = 0x0004;
    internal const uint MouseWheel = 0x0800;
    internal const uint MouseVirtualDesk = 0x4000;
    internal const uint MouseAbsolute = 0x8000;

    internal const uint KeyUp = 0x0002;

    internal const int VirtualScreenX = 76;
    internal const int VirtualScreenY = 77;
    internal const int VirtualScreenWidth = 78;
    internal const int VirtualScreenHeight = 79;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint count, InputRecord[] inputs, int size);

    [DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    internal static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    internal static extern short VkKeyScan(char character);

    [DllImport("user32.dll")]
    internal static extern uint MapVirtualKey(uint code, uint mapType);

    [DllImport("user32.dll")]
    internal static extern nint GetDC(nint window);

    [DllImport("user32.dll")]
    internal static extern int ReleaseDC(nint window, nint context);

    [DllImport("gdi32.dll")]
    internal static extern uint GetPixel(nint context, int x, int y);
}
