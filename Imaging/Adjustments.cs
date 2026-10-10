using System;

namespace RustyPainter.Imaging;

/// <summary>A one-click look applied before the colours are matched.</summary>
public enum ImageFilter
{
    None,
    BlackAndWhite,
    Sepia,
    Invert,
    Brighten,
    Posterize
}

/// <summary>Tweaks applied to the picture before the colours are matched.</summary>
/// <param name="Brightness">-1 (much darker) to 1 (much lighter).</param>
/// <param name="Contrast">-1 (flat grey) to 1 (very punchy).</param>
/// <param name="Saturation">-1 (no colour) to 1 (very vivid).</param>
/// <param name="Filter">A one-click look, applied first.</param>
public sealed record AdjustmentSettings(double Brightness = 0, double Contrast = 0, double Saturation = 0, ImageFilter Filter = ImageFilter.None)
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

            (r, g, b) = ApplyFilter(settings.Filter, r, g, b);

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
