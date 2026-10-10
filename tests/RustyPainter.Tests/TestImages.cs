using RustyPainter.Imaging;

namespace RustyPainter.Tests;

/// <summary>Makes picture files for tests, so no test depends on a memorised blob of base64.</summary>
internal static class TestImages
{
    /// <summary>Writes the pixels as a PNG file. Needs the headless Avalonia application to be running.</summary>
    public static void SavePng(PixelBuffer pixels, string path)
    {
        using var bitmap = BitmapPixels.ToBitmap(pixels);
        bitmap.Save(path);
    }

    /// <summary>A picture whose left half is red and right half is green.</summary>
    public static PixelBuffer RedAndGreen(int width = 2, int height = 1)
    {
        PixelBuffer p = new(width, height);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if (x < width / 2) p.Set(x, y, 220, 20, 20);
                else p.Set(x, y, 20, 220, 20);

        return p;
    }

    /// <summary>A smooth ramp through many different colors.</summary>
    public static PixelBuffer Rainbow(int width = 64, int height = 8)
    {
        PixelBuffer p = new(width, height);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                p.Set(x, y, (byte)(x * 255 / (width - 1)), (byte)(255 - x * 255 / (width - 1)), (byte)(y * 255 / (height - 1)));

        return p;
    }
}
