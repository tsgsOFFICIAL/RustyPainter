using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;
using System.Runtime.InteropServices;

namespace RustyPainter.Imaging;

/// <summary>Converts between Avalonia pictures and the engine's <see cref="PixelBuffer"/>.</summary>
public static class BitmapPixels
{
    /// <summary>Reads every pixel of a decoded picture, whichever byte order and alpha kind the platform used.</summary>
    /// <exception cref="NotSupportedException">The picture uses a pixel format other than 8 bit BGRA or RGBA.</exception>
    public static PixelBuffer FromBitmap(Bitmap bitmap)
    {
        PixelSize size = bitmap.PixelSize;
        int stride = size.Width * 4;
        byte[] raw = new byte[stride * size.Height];

        IntPtr buffer = Marshal.AllocHGlobal(raw.Length);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, size.Width, size.Height), buffer, raw.Length, stride);
            Marshal.Copy(buffer, raw, 0, raw.Length);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        PixelFormat format = bitmap.Format ?? PixelFormat.Bgra8888;
        if (format != PixelFormat.Bgra8888 && format != PixelFormat.Rgba8888)
            throw new NotSupportedException($"The picture uses the pixel format {format}, which is not supported.");

        bool bgra = format == PixelFormat.Bgra8888;
        bool premultiplied = bitmap.AlphaFormat == AlphaFormat.Premul;

        PixelBuffer result = new(size.Width, size.Height);
        for (int i = 0; i < raw.Length; i += 4)
        {
            byte c0 = raw[i], c1 = raw[i + 1], c2 = raw[i + 2], alpha = raw[i + 3];
            byte r = bgra ? c2 : c0, g = c1, b = bgra ? c0 : c2;

            if (premultiplied && alpha is > 0 and < 255)
            {
                r = Straighten(r, alpha);
                g = Straighten(g, alpha);
                b = Straighten(b, alpha);
            }

            result.Data[i] = r;
            result.Data[i + 1] = g;
            result.Data[i + 2] = b;
            result.Data[i + 3] = alpha;
        }

        return result;
    }

    /// <summary>Makes a picture Avalonia can draw from the engine's pixels.</summary>
    public static WriteableBitmap ToBitmap(PixelBuffer pixels)
    {
        WriteableBitmap bitmap = new(new PixelSize(pixels.Width, pixels.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);

        using ILockedFramebuffer frame = bitmap.Lock();
        byte[] row = new byte[pixels.Width * 4];

        for (int y = 0; y < pixels.Height; y++)
        {
            for (int x = 0; x < pixels.Width; x++)
            {
                int source = pixels.OffsetOf(x, y);
                byte alpha = pixels.Data[source + 3];

                row[x * 4] = Premultiply(pixels.Data[source + 2], alpha);
                row[x * 4 + 1] = Premultiply(pixels.Data[source + 1], alpha);
                row[x * 4 + 2] = Premultiply(pixels.Data[source], alpha);
                row[x * 4 + 3] = alpha;
            }

            Marshal.Copy(row, 0, frame.Address + y * frame.RowBytes, row.Length);
        }

        return bitmap;
    }

    private static byte Straighten(byte channel, byte alpha) => (byte)Math.Min(255, (channel * 255 + alpha / 2) / alpha);

    private static byte Premultiply(byte channel, byte alpha) => (byte)((channel * alpha + 127) / 255);
}
