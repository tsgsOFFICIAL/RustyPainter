using Avalonia.Controls;
using RustyPainter.Service.Native;

namespace RustyPainter.Windows;

/// <summary>Makes a window that floats over the game behave like it is not there: no clicks, no focus, no taskbar button.</summary>
public static class ClickThrough
{
    /// <summary>
    /// Lets every click and key through to whatever is below the window, and keeps the window from ever becoming the
    /// active one, which would take the focus from the game. Does nothing for a window that has no handle yet.
    /// </summary>
    public static void Apply(Window window)
    {
        if (window.TryGetPlatformHandle()?.Handle is not { } handle || handle == 0)
            return;

        nint style = WindowStyleNativeMethods.GetWindowLongPtr(handle, WindowStyleNativeMethods.ExtendedStyle);
        style |= WindowStyleNativeMethods.Layered | WindowStyleNativeMethods.Transparent
               | WindowStyleNativeMethods.NoActivate | WindowStyleNativeMethods.ToolWindow;
        WindowStyleNativeMethods.SetWindowLongPtr(handle, WindowStyleNativeMethods.ExtendedStyle, style);
    }
}
