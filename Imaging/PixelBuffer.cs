using System;

namespace RustyPainter.Imaging;

/// <summary>A color with 8 bits per channel.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    /// <summary>The color as "#RRGGBB".</summary>
    public string Hex => $"#{R:X2}{G:X2}{B:X2}";

    /// <summary>Reads "#RRGGBB" (the leading # is optional).</summary>
    public static Rgb Parse(string hex)
    {
        ReadOnlySpan<char> digits = hex.AsSpan().TrimStart('#');
        if (digits.Length != 6)
            throw new FormatException($"'{hex}' is not a #RRGGBB color.");

        return new Rgb(
            byte.Parse(digits[..2], System.Globalization.NumberStyles.HexNumber),
            byte.Parse(digits[2..4], System.Globalization.NumberStyles.HexNumber),
            byte.Parse(digits[4..6], System.Globalization.NumberStyles.HexNumber));
    }
}

/// <summary>
/// A picture as plain bytes: red, green, blue, alpha for every pixel, row by row, with straight (not
/// premultiplied) alpha. The image engine works on these so it needs no UI types and is easy to test.
/// </summary>
public sealed class PixelBuffer
{
    public PixelBuffer(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), $"A picture needs a size, got {width}x{height}.");

        Width = width;
        Height = height;
        Data = new byte[width * height * 4];
    }

    public PixelBuffer(int width, int height, byte[] data)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), $"A picture needs a size, got {width}x{height}.");

        if (data.Length != width * height * 4)
            throw new ArgumentException($"Expected {width * height * 4} bytes for {width}x{height} pixels, got {data.Length}.", nameof(data));

        Width = width;
        Height = height;
        Data = data;
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>The pixel bytes, four per pixel in the order red, green, blue, alpha.</summary>
    public byte[] Data { get; }

    /// <summary>The index in <see cref="Data"/> of the first byte of the pixel at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public int OffsetOf(int x, int y) => (y * Width + x) * 4;

    /// <summary>A picture filled with one color.</summary>
    public static PixelBuffer Solid(int width, int height, byte r, byte g, byte b, byte a = 255)
    {
        PixelBuffer buffer = new(width, height);
        for (int i = 0; i < buffer.Data.Length; i += 4)
        {
            buffer.Data[i] = r;
            buffer.Data[i + 1] = g;
            buffer.Data[i + 2] = b;
            buffer.Data[i + 3] = a;
        }

        return buffer;
    }

    /// <summary>Sets one pixel.</summary>
    public void Set(int x, int y, byte r, byte g, byte b, byte a = 255)
    {
        int o = OffsetOf(x, y);
        Data[o] = r;
        Data[o + 1] = g;
        Data[o + 2] = b;
        Data[o + 3] = a;
    }
}
