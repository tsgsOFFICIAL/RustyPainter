using System;

namespace RustyPainter.Imaging;

/// <summary>A colour in CIE L*a*b*, where distance roughly matches how different two colours look.</summary>
public readonly record struct Lab(double L, double A, double B)
{
    /// <summary>The squared distance to another colour. Cheaper than the distance and just as good for finding the nearest.</summary>
    public double DistanceSquaredTo(Lab other)
    {
        double dl = L - other.L, da = A - other.A, db = B - other.B;
        return dl * dl + da * da + db * db;
    }
}

public static class ColorMath
{
    private const double WhiteX = 0.95047, WhiteY = 1.0, WhiteZ = 1.08883;

    /// <summary>Converts an sRGB colour (channels 0 to 255, fractions allowed) to L*a*b*.</summary>
    public static Lab ToLab(double r, double g, double b)
    {
        double lr = Linear(r), lg = Linear(g), lb = Linear(b);

        double x = (0.4124564 * lr + 0.3575761 * lg + 0.1804375 * lb) / WhiteX;
        double y = (0.2126729 * lr + 0.7151522 * lg + 0.0721750 * lb) / WhiteY;
        double z = (0.0193339 * lr + 0.1191920 * lg + 0.9503041 * lb) / WhiteZ;

        double fx = F(x), fy = F(y), fz = F(z);
        return new Lab(116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    public static Lab ToLab(Rgb color) => ToLab(color.R, color.G, color.B);

    /// <summary>How bright a colour looks, from 0 to 255.</summary>
    public static double Luma(double r, double g, double b) => 0.299 * r + 0.587 * g + 0.114 * b;

    private static double Linear(double channel)
    {
        double c = Math.Clamp(channel, 0, 255) / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static double F(double t) => t > 0.008856 ? Math.Cbrt(t) : 7.787 * t + 16.0 / 116.0;
}
