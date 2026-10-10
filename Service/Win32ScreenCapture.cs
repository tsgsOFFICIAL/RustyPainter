using RustyPainter.Imaging;
using RustyPainter.Interface;
using RustyPainter.Painting;
using RustyPainter.Service.Native;
using System;

namespace RustyPainter.Service;

/// <inheritdoc cref="IScreenCapture" />
public sealed class Win32ScreenCapture : IScreenCapture
{
    public PixelBuffer? Capture(ScreenRect region)
    {
        int left = (int)Math.Floor(region.X1), top = (int)Math.Floor(region.Y1);
        int width = (int)Math.Ceiling(region.X2) - left, height = (int)Math.Ceiling(region.Y2) - top;
        if (width <= 0 || height <= 0 || width > 8192 || height > 8192)
            return null;

        nint screen = InputNativeMethods.GetDC(0);
        if (screen == 0)
            return null;

        nint memory = CaptureNativeMethods.CreateCompatibleDC(screen);
        nint bitmap = CaptureNativeMethods.CreateCompatibleBitmap(screen, width, height);

        try
        {
            if (memory == 0 || bitmap == 0)
                return null;

            nint previous = CaptureNativeMethods.SelectObject(memory, bitmap);
            bool copied = CaptureNativeMethods.BitBlt(memory, 0, 0, width, height, screen, left, top, CaptureNativeMethods.SourceCopy);
            CaptureNativeMethods.SelectObject(memory, previous);

            if (!copied)
                return null;

            // A negative height asks for the rows from the top down, 32 bits per pixel as blue, green, red and a spare byte.
            BitmapInfoHeader info = new()
            {
                Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<BitmapInfoHeader>(),
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = 32
            };

            byte[] bits = new byte[width * height * 4];
            if (CaptureNativeMethods.GetDIBits(memory, bitmap, 0, (uint)height, bits, ref info, 0) == 0)
                return null;

            PixelBuffer picture = new(width, height);
            for (int i = 0; i < width * height; i++)
            {
                picture.Data[i * 4] = bits[i * 4 + 2];
                picture.Data[i * 4 + 1] = bits[i * 4 + 1];
                picture.Data[i * 4 + 2] = bits[i * 4];
                picture.Data[i * 4 + 3] = 255;
            }

            return picture;
        }
        finally
        {
            if (bitmap != 0)
                CaptureNativeMethods.DeleteObject(bitmap);

            if (memory != 0)
                CaptureNativeMethods.DeleteDC(memory);

            InputNativeMethods.ReleaseDC(0, screen);
        }
    }
}
