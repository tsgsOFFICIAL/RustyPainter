using System;

namespace RustyPainter.Imaging;

/// <summary>How the picture is sized against the canvas.</summary>
public enum FitMode
{
    /// <summary>The whole picture is visible; the rest of the canvas stays empty.</summary>
    Fit,

    /// <summary>The picture covers the whole canvas; whatever sticks out is cropped.</summary>
    Fill,

    /// <summary>The picture is squashed or stretched to exactly the canvas.</summary>
    Stretch
}

/// <summary>Where and how the picture sits on the canvas.</summary>
/// <param name="Mode">How the picture is sized against the canvas.</param>
/// <param name="Zoom">Extra scale on top of <paramref name="Mode"/>; 1 is no change.</param>
/// <param name="OffsetX">Horizontal shift as a fraction of the canvas width; 0.5 moves it half a canvas to the right.</param>
/// <param name="OffsetY">Vertical shift as a fraction of the canvas height.</param>
public sealed record PlacementSettings(FitMode Mode = FitMode.Fill, double Zoom = 1, double OffsetX = 0, double OffsetY = 0);

public static class PlacementEngine
{
    /// <summary>
    /// Flips and then turns a picture clockwise by whole quarter turns. Returns the same instance when
    /// there is nothing to do.
    /// </summary>
    public static PixelBuffer Orient(PixelBuffer source, int quarterTurns, bool flipHorizontal, bool flipVertical)
    {
        int turns = ((quarterTurns % 4) + 4) % 4;
        if (turns == 0 && !flipHorizontal && !flipVertical)
            return source;

        int w = source.Width, h = source.Height;
        bool swap = turns % 2 == 1;
        PixelBuffer result = new(swap ? h : w, swap ? w : h);

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int fx = flipHorizontal ? w - 1 - x : x;
                int fy = flipVertical ? h - 1 - y : y;

                (int dx, int dy) = turns switch
                {
                    0 => (fx, fy),
                    1 => (h - 1 - fy, fx),
                    2 => (w - 1 - fx, h - 1 - fy),
                    _ => (fy, w - 1 - fx)
                };

                Buffer.BlockCopy(source.Data, source.OffsetOf(x, y), result.Data, result.OffsetOf(dx, dy), 4);
            }
        }

        return result;
    }

    /// <summary>
    /// Lays the picture onto a grid of <paramref name="gridWidth"/> by <paramref name="gridHeight"/> cells and
    /// returns one pixel per cell. Cells the picture does not reach are fully transparent, which later means
    /// "leave this cell unpainted". Shrinking averages the pixels under each cell, enlarging blends smoothly.
    /// </summary>
    public static PixelBuffer Resample(PixelBuffer source, PlacementSettings placement, int gridWidth, int gridHeight)
    {
        double sw = source.Width, sh = source.Height;
        double fit = Math.Min(gridWidth / sw, gridHeight / sh);
        double fill = Math.Max(gridWidth / sw, gridHeight / sh);

        (double sx, double sy) = placement.Mode switch
        {
            FitMode.Fit => (fit, fit),
            FitMode.Fill => (fill, fill),
            _ => (gridWidth / sw, gridHeight / sh)
        };

        double zoom = Math.Clamp(placement.Zoom, 0.05, 20);
        sx *= zoom;
        sy *= zoom;

        double left = (gridWidth - sw * sx) / 2 + placement.OffsetX * gridWidth;
        double top = (gridHeight - sh * sy) / 2 + placement.OffsetY * gridHeight;
        double footprintX = 1 / sx, footprintY = 1 / sy;

        PixelBuffer grid = new(gridWidth, gridHeight);

        for (int cy = 0; cy < gridHeight; cy++)
        {
            double v = (cy + 0.5 - top) / sy;
            if (v < 0 || v >= sh)
                continue;

            for (int cx = 0; cx < gridWidth; cx++)
            {
                double u = (cx + 0.5 - left) / sx;
                if (u < 0 || u >= sw)
                    continue;

                (double r, double g, double b, double a) = footprintX <= 1 && footprintY <= 1
                    ? Bilinear(source, u, v)
                    : Average(source, u, v, footprintX, footprintY);

                int o = grid.OffsetOf(cx, cy);
                grid.Data[o] = ToByte(r);
                grid.Data[o + 1] = ToByte(g);
                grid.Data[o + 2] = ToByte(b);
                grid.Data[o + 3] = ToByte(a * 255);
            }
        }

        return grid;
    }

    private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);

    /// <summary>Smooth blend of the four pixels around (<paramref name="u"/>, <paramref name="v"/>), weighted by alpha so transparent pixels do not darken edges.</summary>
    private static (double R, double G, double B, double A) Bilinear(PixelBuffer p, double u, double v)
    {
        double fx = u - 0.5, fy = v - 0.5;
        int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
        double tx = fx - x0, ty = fy - y0;

        double sumA = 0, sumR = 0, sumG = 0, sumB = 0;

        for (int j = 0; j < 2; j++)
        {
            for (int i = 0; i < 2; i++)
            {
                int x = Math.Clamp(x0 + i, 0, p.Width - 1), y = Math.Clamp(y0 + j, 0, p.Height - 1);
                double weight = (i == 0 ? 1 - tx : tx) * (j == 0 ? 1 - ty : ty);
                int o = p.OffsetOf(x, y);
                double alpha = p.Data[o + 3] / 255.0;

                sumA += weight * alpha;
                sumR += weight * alpha * p.Data[o];
                sumG += weight * alpha * p.Data[o + 1];
                sumB += weight * alpha * p.Data[o + 2];
            }
        }

        return sumA <= 0 ? (0, 0, 0, 0) : (sumR / sumA, sumG / sumA, sumB / sumA, sumA);
    }

    /// <summary>Average of all source pixels inside a box centred on (<paramref name="u"/>, <paramref name="v"/>), with partial weight at the box edges.</summary>
    private static (double R, double G, double B, double A) Average(PixelBuffer p, double u, double v, double width, double height)
    {
        double x0 = Math.Max(0, u - width / 2), x1 = Math.Min(p.Width, u + width / 2);
        double y0 = Math.Max(0, v - height / 2), y1 = Math.Min(p.Height, v + height / 2);

        double sumW = 0, sumA = 0, sumR = 0, sumG = 0, sumB = 0;

        for (int y = (int)Math.Floor(y0); y < (int)Math.Ceiling(y1); y++)
        {
            double wy = Math.Min(y + 1, y1) - Math.Max(y, y0);

            for (int x = (int)Math.Floor(x0); x < (int)Math.Ceiling(x1); x++)
            {
                double weight = wy * (Math.Min(x + 1, x1) - Math.Max(x, x0));
                int o = p.OffsetOf(x, y);
                double alpha = p.Data[o + 3] / 255.0;

                sumW += weight;
                sumA += weight * alpha;
                sumR += weight * alpha * p.Data[o];
                sumG += weight * alpha * p.Data[o + 1];
                sumB += weight * alpha * p.Data[o + 2];
            }
        }

        return sumW <= 0 || sumA <= 0 ? (0, 0, 0, 0) : (sumR / sumA, sumG / sumA, sumB / sumA, sumA / sumW);
    }
}
