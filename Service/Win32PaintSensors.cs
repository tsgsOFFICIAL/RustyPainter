using RustyPainter.Imaging;
using RustyPainter.Interface;
using RustyPainter.Painting;
using RustyPainter.Service.Native;
using System;

namespace RustyPainter.Service;

/// <summary>Looks at the real machine: which window is in front, where the cursor is and what color a pixel has.</summary>
public sealed class Win32PaintSensors(IGameWindowService game) : IPaintSensors
{
    private GameWindow? _window;

    public bool IsGameInFront()
    {
        nint front = NativeMethods.GetForegroundWindow();

        // The window is looked up again only when something else is in front, so asking is cheap while painting.
        if (_window is null || _window.Handle != front)
            _window = game.Find();

        return _window is { IsMinimized: false } window && window.Handle == front;
    }

    public ScreenPoint CursorPosition
    {
        get
        {
            InputNativeMethods.GetCursorPos(out NativePoint point);
            return new ScreenPoint(point.X, point.Y);
        }
    }

    public Rgb? ReadColor(ScreenPoint point)
    {
        nint context = InputNativeMethods.GetDC(0);
        if (context == 0)
            return null;

        try
        {
            uint pixel = InputNativeMethods.GetPixel(context, (int)Math.Round(point.X), (int)Math.Round(point.Y));
            if (pixel == 0xFFFFFFFF)
                return null;

            // The system returns red in the lowest byte, then green, then blue.
            return new Rgb((byte)(pixel & 0xFF), (byte)((pixel >> 8) & 0xFF), (byte)((pixel >> 16) & 0xFF));
        }
        finally
        {
            InputNativeMethods.ReleaseDC(0, context);
        }
    }
}
