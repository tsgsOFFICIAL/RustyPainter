using System;
using System.Collections.Generic;
using System.Linq;

namespace RustyPainter.Imaging;

/// <summary>How the difference between a color and its nearest palette color is spread around.</summary>
public enum Dithering
{
    /// <summary>Every cell takes its nearest color. Flat and clean, but gradients turn into bands.</summary>
    None,

    /// <summary>Error diffusion: the leftover of each cell is pushed onto its neighbours, so gradients look smooth.</summary>
    Smooth,

    /// <summary>A fixed pattern of small offsets; a regular, retro look that never smears.</summary>
    Ordered
}

public static class Quantizer
{
    /// <summary>Cells with less alpha than this are left unpainted.</summary>
    public const byte AlphaThreshold = 128;

    /// <summary>Marks a cell that is not painted.</summary>
    public const int Unpainted = -1;

    private const double OrderedStrength = 40;

    private static readonly int[] Bayer8 =
    [
        0, 32, 8, 40, 2, 34, 10, 42,
        48, 16, 56, 24, 50, 18, 58, 26,
        12, 44, 4, 36, 14, 46, 6, 38,
        60, 28, 52, 20, 62, 30, 54, 22,
        3, 35, 11, 43, 1, 33, 9, 41,
        51, 19, 59, 27, 49, 17, 57, 25,
        15, 47, 7, 39, 13, 45, 5, 37,
        63, 31, 55, 23, 61, 29, 53, 21
    ];

    /// <summary>
    /// Picks a palette color for every pixel. Returns one index into <paramref name="palette"/> per pixel,
    /// or <see cref="Unpainted"/> for transparent ones. Colors are compared the way the eye does (L*a*b*).
    /// </summary>
    public static int[] Map(PixelBuffer image, IReadOnlyList<Rgb> palette, Dithering dithering)
    {
        if (palette.Count == 0)
            throw new ArgumentException("The palette needs at least one color.", nameof(palette));

        int w = image.Width, h = image.Height;
        int[] result = new int[w * h];
        Lab[] paletteLab = palette.Select(ColorMath.ToLab).ToArray();

        int Nearest(double r, double g, double b)
        {
            Lab color = ColorMath.ToLab(r, g, b);
            int best = 0;
            double bestDistance = double.MaxValue;

            for (int i = 0; i < paletteLab.Length; i++)
            {
                double distance = color.DistanceSquaredTo(paletteLab[i]);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            return best;
        }

        bool Painted(int pixel) => image.Data[pixel * 4 + 3] >= AlphaThreshold;

        if (dithering == Dithering.Smooth)
        {
            float[] work = new float[w * h * 3];
            for (int p = 0; p < w * h; p++)
            {
                work[p * 3] = image.Data[p * 4];
                work[p * 3 + 1] = image.Data[p * 4 + 1];
                work[p * 3 + 2] = image.Data[p * 4 + 2];
            }

            void Spread(int x, int y, float er, float eg, float eb, float share)
            {
                if (x < 0 || x >= w || y >= h)
                    return;

                int p = y * w + x;
                if (!Painted(p))
                    return;

                work[p * 3] += er * share;
                work[p * 3 + 1] += eg * share;
                work[p * 3 + 2] += eb * share;
            }

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int p = y * w + x;
                    if (!Painted(p))
                    {
                        result[p] = Unpainted;
                        continue;
                    }

                    float r = Math.Clamp(work[p * 3], 0, 255), g = Math.Clamp(work[p * 3 + 1], 0, 255), b = Math.Clamp(work[p * 3 + 2], 0, 255);
                    int index = Nearest(r, g, b);
                    result[p] = index;

                    Rgb chosen = palette[index];
                    float er = r - chosen.R, eg = g - chosen.G, eb = b - chosen.B;

                    Spread(x + 1, y, er, eg, eb, 7f / 16);
                    Spread(x - 1, y + 1, er, eg, eb, 3f / 16);
                    Spread(x, y + 1, er, eg, eb, 5f / 16);
                    Spread(x + 1, y + 1, er, eg, eb, 1f / 16);
                }
            }

            return result;
        }

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int p = y * w + x;
                if (!Painted(p))
                {
                    result[p] = Unpainted;
                    continue;
                }

                double r = image.Data[p * 4], g = image.Data[p * 4 + 1], b = image.Data[p * 4 + 2];

                if (dithering == Dithering.Ordered)
                {
                    double offset = ((Bayer8[(y & 7) * 8 + (x & 7)] + 0.5) / 64 - 0.5) * OrderedStrength;
                    r = Math.Clamp(r + offset, 0, 255);
                    g = Math.Clamp(g + offset, 0, 255);
                    b = Math.Clamp(b + offset, 0, 255);
                }

                result[p] = Nearest(r, g, b);
            }
        }

        return result;
    }
}
