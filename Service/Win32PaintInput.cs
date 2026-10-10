using RustyPainter.Interface;
using RustyPainter.Painting;
using RustyPainter.Service.Native;
using System.Runtime.InteropServices;
using System;

namespace RustyPainter.Service;

/// <summary>
/// Sends real mouse and keyboard input to Windows. The app is per-monitor DPI aware, so the pixels given here are the
/// physical pixels the game draws in. Windows refuses input for a window that runs as administrator when this app
/// does not, which shows up as nothing happening in the game.
/// </summary>
public sealed class Win32PaintInput : IPaintInput
{
    private static readonly int InputSize = Marshal.SizeOf<InputRecord>();

    public void MoveTo(ScreenPoint point)
    {
        int left = InputNativeMethods.GetSystemMetrics(InputNativeMethods.VirtualScreenX);
        int top = InputNativeMethods.GetSystemMetrics(InputNativeMethods.VirtualScreenY);
        int width = Math.Max(2, InputNativeMethods.GetSystemMetrics(InputNativeMethods.VirtualScreenWidth));
        int height = Math.Max(2, InputNativeMethods.GetSystemMetrics(InputNativeMethods.VirtualScreenHeight));

        // The system wants the position as a fraction of the whole desktop, from 0 to 65535.
        int dx = (int)Math.Round((point.X - left) * 65535.0 / (width - 1));
        int dy = (int)Math.Round((point.Y - top) * 65535.0 / (height - 1));

        Send(MouseRecord(dx, dy, 0, InputNativeMethods.MouseMove | InputNativeMethods.MouseAbsolute | InputNativeMethods.MouseVirtualDesk));
    }

    public void LeftDown() => Send(MouseRecord(0, 0, 0, InputNativeMethods.MouseLeftDown));

    public void LeftUp() => Send(MouseRecord(0, 0, 0, InputNativeMethods.MouseLeftUp));

    public void Scroll(int notches) => Send(MouseRecord(0, 0, unchecked((uint)(notches * 120)), InputNativeMethods.MouseWheel));

    public void PressKey(char key)
    {
        short scan = InputNativeMethods.VkKeyScan(key);
        if (scan == -1)
            throw new PaintException($"The key {key} cannot be typed with this keyboard layout.");

        ushort virtualKey = (ushort)(scan & 0xFF);
        ushort scanCode = (ushort)InputNativeMethods.MapVirtualKey(virtualKey, 0);

        Send(KeyRecord(virtualKey, scanCode, 0), KeyRecord(virtualKey, scanCode, InputNativeMethods.KeyUp));
    }

    private static InputRecord MouseRecord(int dx, int dy, uint data, uint flags) => new()
    {
        Type = InputNativeMethods.InputMouse,
        Union = new InputUnion { Mouse = new MouseInput { Dx = dx, Dy = dy, MouseData = data, Flags = flags } }
    };

    private static InputRecord KeyRecord(ushort virtualKey, ushort scanCode, uint flags) => new()
    {
        Type = InputNativeMethods.InputKeyboard,
        Union = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = virtualKey, ScanCode = scanCode, Flags = flags } }
    };

    private static void Send(params InputRecord[] records)
    {
        uint sent = InputNativeMethods.SendInput((uint)records.Length, records, InputSize);

        if (sent != records.Length)
            throw new PaintException("Windows did not accept the input. The game may be running as administrator; start this app as administrator too.");
    }
}
