using System;

namespace RustyPainter.Imaging;

/// <summary>
/// Draws what a bare surface looks like in the game (planks, cloth, a dark neon panel, a pumpkin skin, stone), so the
/// preview shows the picture on the item and not on a flat gray. Everything is made from a few noise functions, so there
/// are no pictures to ship and the same size always gives the same result.
/// </summary>
public static class SurfaceBackdrop
{
    /// <summary>The kinds that <see cref="Create"/> knows. Anything else gives a plain gray.</summary>
    public static readonly string[] Kinds = ["wood", "canvas", "frame", "neon", "pumpkin", "stone"];

    /// <summary>Draws a bare surface of the given kind. The picture is fully opaque.</summary>
    public static PixelBuffer Create(string? kind, int width, int height)
    {
        PixelBuffer buffer = new(width, height);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                (double r, double g, double b) = Shade(kind, x, y, width, height);
                buffer.Set(x, y, ToByte(r), ToByte(g), ToByte(b));
            }
        }

        return buffer;
    }

    private static (double R, double G, double B) Shade(string? kind, int x, int y, int width, int height)
    {
        switch (kind)
        {
            case "wood":
            {
                // Planks stacked top to bottom: long grain along x, a darker seam between planks, and each plank a little different.
                double plankHeight = Math.Max(8, height / 5.0);
                int plank = (int)(y / plankHeight);
                double inPlank = y / plankHeight - plank;
                double tone = 0.85 + 0.3 * Hash(plank, 7);
                double grain = Fractal(x * 0.035 + plank * 31, y * 0.9, 3) * 0.5 + Fractal(x * 0.008 + plank * 13, y * 0.25, 2) * 0.5;
                double light = tone * (0.78 + 0.42 * grain);
                if (inPlank < 0.025 || inPlank > 0.985)
                    light *= 0.55;

                return (0.62 * light, 0.45 * light, 0.27 * light);
            }

            case "canvas":
            case "frame":
            {
                // Linen: a fine weave plus a slow cloud so it is not perfectly flat.
                double weave = 0.5 + 0.25 * Math.Sin(x * 1.9) * Math.Sin(y * 1.9) + 0.25 * Math.Sin(x * 0.95 + 1) * Math.Sin(y * 0.95);
                double cloud = Fractal(x * 0.02, y * 0.02, 3);
                double light = 0.9 + 0.06 * weave + 0.05 * (cloud - 0.5);
                double edge = kind == "frame" ? Edge(x, y, width, height, 0.04) : 1;
                light *= edge;

                return (0.93 * light, 0.89 * light, 0.80 * light);
            }

            case "neon":
            {
                // The dark acrylic panel that neon tubes are bent in front of, with a faint sheen from the top left.
                double sheen = 0.5 * (1 - (x / (double)width + y / (double)height) / 2);
                double speckle = Hash(x, y) * 0.02;
                double light = 0.07 + 0.05 * sheen + speckle;

                return (light, light, light * 1.15);
            }

            case "pumpkin":
            {
                // Ribs run top to bottom and are lit from the front; the sides fall off into shadow.
                double across = x / (double)Math.Max(1, width - 1) * 2 - 1;
                double rib = 0.5 + 0.5 * Math.Cos(across * Math.PI * 3.5);
                double roundness = 1 - 0.35 * across * across;
                double skin = Fractal(x * 0.05, y * 0.05, 3);
                double light = (0.62 + 0.3 * rib) * roundness * (0.92 + 0.16 * skin);

                return (0.93 * light, 0.45 * light, 0.1 * light);
            }

            case "stone":
            {
                double cloud = Fractal(x * 0.03, y * 0.03, 4);
                double fine = Hash(x, y) * 0.06;
                double light = 0.5 + 0.22 * (cloud - 0.5) + fine - 0.03;

                return (light, light, light * 1.02);
            }

            default:
                return (0.4, 0.4, 0.42);
        }
    }

    /// <summary>A soft darkening towards the edges, as a frame's inner lip casts.</summary>
    private static double Edge(int x, int y, int width, int height, double reach)
    {
        double distance = Math.Min(Math.Min(x, width - 1 - x) / (double)width, Math.Min(y, height - 1 - y) / (double)height);
        return distance >= reach ? 1 : 0.78 + 0.22 * (distance / reach);
    }

    /// <summary>Smooth noise from 0 to 1 made of several layers, each twice as fine and half as strong as the one before.</summary>
    private static double Fractal(double x, double y, int octaves)
    {
        double sum = 0, weight = 0, amplitude = 1, frequency = 1;
        for (int i = 0; i < octaves; i++)
        {
            sum += amplitude * Value(x * frequency, y * frequency, i);
            weight += amplitude;
            amplitude /= 2;
            frequency *= 2;
        }

        return sum / weight;
    }

    private static double Value(double x, double y, int seed)
    {
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
        double fx = x - x0, fy = y - y0;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);

        double top = Lerp(Hash(x0, y0, seed), Hash(x0 + 1, y0, seed), fx);
        double bottom = Lerp(Hash(x0, y0 + 1, seed), Hash(x0 + 1, y0 + 1, seed), fx);

        return Lerp(top, bottom, fy);
    }

    /// <summary>A repeatable pseudo random number from 0 to 1 for a pair of whole numbers.</summary>
    private static double Hash(int x, int y, int seed = 0)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return h / (double)uint.MaxValue;
        }
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private static byte ToByte(double unit) => (byte)Math.Clamp(Math.Round(unit * 255), 0, 255);
}
