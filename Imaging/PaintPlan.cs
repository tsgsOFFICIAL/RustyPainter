using System.Collections.Generic;
using System.Linq;

namespace RustyPainter.Imaging;

/// <summary>
/// The result of preparing a picture for a canvas: a grid of cells, each one a color from the palette or
/// left unpainted, plus the numbers that say how much work painting it will be.
/// </summary>
public sealed class PaintPlan
{
    public PaintPlan(int width, int height, IReadOnlyList<Rgb> palette, int[] cells)
    {
        Width = width;
        Height = height;
        Palette = palette;
        Cells = cells;

        int painted = 0, strokes = 0;
        int[] counts = new int[palette.Count];

        for (int y = 0; y < height; y++)
        {
            int previous = Quantizer.Unpainted;

            for (int x = 0; x < width; x++)
            {
                int index = cells[y * width + x];

                if (index != Quantizer.Unpainted)
                {
                    painted++;
                    counts[index]++;

                    // A run of the same color along a row is painted with one drag.
                    if (index != previous)
                        strokes++;
                }

                previous = index;
            }
        }

        PaintedCells = painted;
        StrokeCount = strokes;
        UsedColors = Enumerable.Range(0, palette.Count)
            .Where(i => counts[i] > 0)
            .OrderByDescending(i => counts[i])
            .Select(i => palette[i])
            .ToList();
    }

    /// <summary>Cells across.</summary>
    public int Width { get; }

    /// <summary>Cells down.</summary>
    public int Height { get; }

    /// <summary>The colors the cells refer to.</summary>
    public IReadOnlyList<Rgb> Palette { get; }

    /// <summary>One palette index per cell, row by row, or <see cref="Quantizer.Unpainted"/>.</summary>
    public int[] Cells { get; }

    /// <summary>How many cells get paint.</summary>
    public int PaintedCells { get; }

    /// <summary>How many separate drags painting row by row needs.</summary>
    public int StrokeCount { get; }

    /// <summary>The colors that are really used, most used first.</summary>
    public IReadOnlyList<Rgb> UsedColors { get; }

    /// <summary>How many different colors are used, which is how many times the color has to be changed.</summary>
    public int ColorsUsed => UsedColors.Count;

    /// <summary>The plan as a picture, one pixel per cell, with unpainted cells transparent.</summary>
    public PixelBuffer Render()
    {
        PixelBuffer image = new(Width, Height);

        for (int i = 0; i < Cells.Length; i++)
        {
            if (Cells[i] == Quantizer.Unpainted)
                continue;

            Rgb color = Palette[Cells[i]];
            int o = i * 4;
            image.Data[o] = color.R;
            image.Data[o + 1] = color.G;
            image.Data[o + 2] = color.B;
            image.Data[o + 3] = 255;
        }

        return image;
    }
}
