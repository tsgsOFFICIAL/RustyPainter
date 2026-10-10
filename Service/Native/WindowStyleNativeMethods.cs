using System.Runtime.InteropServices;

namespace RustyPainter.Service.Native;

/// <summary>The Win32 calls that change how a window behaves, used to make the hologram click-through.</summary>
internal static class WindowStyleNativeMethods
{
    internal const int ExtendedStyle = -20;
    internal const nint Layered = 0x00080000;
    internal const nint Transparent = 0x00000020;
    internal const nint NoActivate = 0x08000000;
    internal const nint ToolWindow = 0x00000080;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    internal static extern nint SetWindowLongPtr(nint window, int index, nint value);
}
