using System;

namespace RustyPainter.Imaging;

/// <summary>A one-click look applied before the colors are matched.</summary>
public enum ImageFilter
{
    None,
    BlackAndWhite,
    Sepia,
    Invert,
    Brighten,
    Posterize,
    OilPainting
}

/// <summary>Tweaks applied to the picture before the colors are matched.</summary>
/// <param name="Brightness">-1 (much darker) to 1 (much lighter).</param>
/// <param name="Contrast">-1 (flat grey) to 1 (very punchy).</param>
/// <param name="Saturation">-1 (no color) to 1 (very vivid).</param>
/// <param name="Filter">A one-click look, applied first.</param>
/// <param name="FilterStrength">0 (the filter does nothing) to 1 (the full look).</param>
public sealed record AdjustmentSettings(double Brightness = 0, double Contrast = 0, double Saturation = 0, ImageFilter Filter = ImageFilter.None, double FilterStrength = 1)
{
    /// <summary>Whether these settings leave the picture alone.</summary>
    public bool IsNeutral => Brightness == 0 && Contrast == 0 && Saturation == 0 && Filter == ImageFilter.None;
}

public static class Adjustments
{
    /// <summary>Applies the filter, then saturation, contrast and brightness. Transparent pixels are left transparent.</summary>
    public static PixelBuffer Apply(PixelBuffer source, AdjustmentSettings settings)
    {
        if (settings.IsNeutral)
            return source;

        double strength = Math.Clamp(settings.FilterStrength, 0, 1);

        // Oil painting looks at the neighbours of every pixel, so it is done on the whole picture before the per-pixel steps.
        if (settings.Filter == ImageFilter.OilPainting)
            source = OilPaint(source, strength);

        PixelBuffer result = new(source.Width, source.Height);
        double saturation = 1 + Math.Clamp(settings.Saturation, -1, 1);
        double contrast = 1 + Math.Clamp(settings.Contrast, -1, 1);
        double brightness = Math.Clamp(settings.Brightness, -1, 1);

        for (int i = 0; i < source.Data.Length; i += 4)
        {
            byte alpha = source.Data[i + 3];
            result.Data[i + 3] = alpha;
            if (alpha == 0)
                continue;

            double r = source.Data[i] / 255.0, g = source.Data[i + 1] / 255.0, b = source.Data[i + 2] / 255.0;

            (double fr, double fg, double fb) = ApplyFilter(settings.Filter, r, g, b);
            r += (fr - r) * strength;
            g += (fg - g) * strength;
            b += (fb - b) * strength;

            double luma = 0.299 * r + 0.587 * g + 0.114 * b;
            r = luma + (r - luma) * saturation;
            g = luma + (g - luma) * saturation;
            b = luma + (b - luma) * saturation;

            r = (r - 0.5) * contrast + 0.5 + brightness;
            g = (g - 0.5) * contrast + 0.5 + brightness;
            b = (b - 0.5) * contrast + 0.5 + brightness;

            result.Data[i] = ToByte(r);
            result.Data[i + 1] = ToByte(g);
            result.Data[i + 2] = ToByte(b);
        }

        return result;
    }

    /// <summary>The brightness steps the oil painting filter sorts the neighbours into. Fewer steps give broader strokes.</summary>
    private const int OilLevels = 32;

    /// <summary>
    /// Gives the picture the flat daubs of an oil painting: every pixel becomes the average color of the most common
    /// brightness step among the pixels around it. The reach grows with the picture, so a big grid is not left looking untouched.
    /// Transparent pixels stay transparent and are not counted.
    /// </summary>
    public static PixelBuffer OilPaint(PixelBuffer source, double strength = 1)
    {
        // The daubs grow with the strength: a weak oil painting only smooths the finest detail.
        int radius = (int)Math.Round(Math.Clamp(Math.Max(source.Width, source.Height) / 200.0, 1, 3) * Math.Clamp(strength, 0, 1));
        if (radius == 0)
            return source;

        PixelBuffer result = new(source.Width, source.Height);

        int[] count = new int[OilLevels];
        int[] sumR = new int[OilLevels], sumG = new int[OilLevels], sumB = new int[OilLevels];

        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                int o = source.OffsetOf(x, y);
                result.Data[o + 3] = source.Data[o + 3];
                if (source.Data[o + 3] == 0)
                    continue;

                Array.Clear(count);
                Array.Clear(sumR);
                Array.Clear(sumG);
                Array.Clear(sumB);

                for (int ny = Math.Max(0, y - radius); ny <= Math.Min(source.Height - 1, y + radius); ny++)
                {
                    for (int nx = Math.Max(0, x - radius); nx <= Math.Min(source.Width - 1, x + radius); nx++)
                    {
                        int n = source.OffsetOf(nx, ny);
                        if (source.Data[n + 3] == 0)
                            continue;

                        int r = source.Data[n], g = source.Data[n + 1], b = source.Data[n + 2];
                        int level = Math.Min(OilLevels - 1, (r * 299 + g * 587 + b * 114) * OilLevels / 255000);

                        count[level]++;
                        sumR[level] += r;
                        sumG[level] += g;
                        sumB[level] += b;
                    }
                }

                int best = 0;
                for (int level = 1; level < OilLevels; level++)
                    if (count[level] > count[best])
                        best = level;

                result.Data[o] = (byte)(sumR[best] / count[best]);
                result.Data[o + 1] = (byte)(sumG[best] / count[best]);
                result.Data[o + 2] = (byte)(sumB[best] / count[best]);
            }
        }

        return result;
    }

    private static (double R, double G, double B) ApplyFilter(ImageFilter filter, double r, double g, double b)
    {
        switch (filter)
        {
            case ImageFilter.BlackAndWhite:
                double luma = 0.299 * r + 0.587 * g + 0.114 * b;
                return (luma, luma, luma);

            case ImageFilter.Sepia:
                return (
                    Math.Min(1, 0.393 * r + 0.769 * g + 0.189 * b),
                    Math.Min(1, 0.349 * r + 0.686 * g + 0.168 * b),
                    Math.Min(1, 0.272 * r + 0.534 * g + 0.131 * b));

            case ImageFilter.Invert:
                return (1 - r, 1 - g, 1 - b);

            case ImageFilter.Brighten:
                return (Math.Min(1, r * 1.2 + 0.08), Math.Min(1, g * 1.2 + 0.08), Math.Min(1, b * 1.2 + 0.08));

            case ImageFilter.Posterize:
                const double levels = 4;
                return (Math.Round(r * (levels - 1)) / (levels - 1), Math.Round(g * (levels - 1)) / (levels - 1), Math.Round(b * (levels - 1)) / (levels - 1));

            default:
                return (r, g, b);
        }
    }

    private static byte ToByte(double unit) => (byte)Math.Clamp(Math.Round(unit * 255), 0, 255);
}
