using System.Collections.Generic;
using System.Linq;
using System;

namespace RustyPainter.Imaging;

/// <summary>One brush stroke over cells: a drag along a row or a column, or a single click.</summary>
/// <param name="X">The column of the first cell.</param>
/// <param name="Y">The row of the first cell.</param>
/// <param name="Length">How many cells the stroke covers. 1 is a click.</param>
/// <param name="Vertical">Whether the stroke runs down a column instead of along a row.</param>
public readonly record struct Stroke(int X, int Y, int Length, bool Vertical)
{
    /// <summary>A single cell, painted with a click instead of a drag.</summary>
    public bool IsClick => Length == 1;

    /// <summary>The column of the last cell.</summary>
    public int EndX => Vertical ? X : X + Length - 1;

    /// <summary>The row of the last cell.</summary>
    public int EndY => Vertical ? Y + Length - 1 : Y;
}

/// <summary>Everything painted in one color, in the order it is painted.</summary>
/// <param name="PaletteIndex">Which color of the plan's palette this is.</param>
/// <param name="Color">The color.</param>
/// <param name="Strokes">The strokes, ordered so the mouse travels as little as possible.</param>
public sealed record ColorPass(int PaletteIndex, Rgb Color, IReadOnlyList<Stroke> Strokes);

/// <summary>
/// A <see cref="PaintPlan"/> turned into brush strokes: cells of one color that touch in a row or a column are painted
/// with one drag, a lone cell with a click, and each color is painted once, so the color is only changed as often as
/// there are colors.
/// </summary>
public sealed class StrokePlan(int width, int height, IReadOnlyList<ColorPass> passes)
{
    /// <summary>Cells across the canvas.</summary>
    public int Width { get; } = width;

    /// <summary>Cells down the canvas.</summary>
    public int Height { get; } = height;

    /// <summary>The colors in the order they are painted. The most used color comes first.</summary>
    public IReadOnlyList<ColorPass> Passes { get; } = passes;

    /// <summary>Every drag and click.</summary>
    public int StrokeCount { get; } = passes.Sum(pass => pass.Strokes.Count);

    /// <summary>How many of the strokes are single cells.</summary>
    public int ClickCount { get; } = passes.Sum(pass => pass.Strokes.Count(stroke => stroke.IsClick));

    /// <summary>How many cells are covered. Every painted cell is covered exactly once.</summary>
    public int CellCount { get; } = passes.Sum(pass => pass.Strokes.Sum(stroke => stroke.Length));
}

public static class StrokePlanner
{
    /// <summary>
    /// Groups the painted cells into strokes. Going through the cells from the top left, each cell that is not
    /// covered yet starts the longer of the run to its right and the run below it.
    /// </summary>
    public static StrokePlan Plan(PaintPlan plan)
    {
        int w = plan.Width, h = plan.Height;
        int[] cells = plan.Cells;

        // How far the color goes to the right and downwards from each cell, whatever has been covered already.
        int[] right = new int[cells.Length];
        int[] down = new int[cells.Length];

        for (int y = h - 1; y >= 0; y--)
        {
            for (int x = w - 1; x >= 0; x--)
            {
                int i = y * w + x;
                int color = cells[i];
                if (color == Quantizer.Unpainted)
                    continue;

                right[i] = x + 1 < w && cells[i + 1] == color ? right[i + 1] + 1 : 1;
                down[i] = y + 1 < h && cells[i + w] == color ? down[i + w] + 1 : 1;
            }
        }

        List<Stroke>[] strokes = new List<Stroke>[plan.Palette.Count];
        int[] counts = new int[plan.Palette.Count];
        bool[] covered = new bool[cells.Length];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                int color = cells[i];
                if (color == Quantizer.Unpainted || covered[i])
                    continue;

                // Nothing below can be covered yet, because strokes only ever start at or above the cell they cover.
                int vertical = down[i];
                int horizontal = right[i];

                if (vertical > horizontal)
                {
                    horizontal = 0;
                }
                else
                {
                    // Earlier strokes down a column may have covered cells to the right, so count what is really left.
                    int length = 1;
                    while (length < horizontal && !covered[i + length])
                        length++;

                    horizontal = length;
                }

                Stroke stroke;
                if (vertical > horizontal)
                {
                    stroke = new Stroke(x, y, vertical, Vertical: true);
                    for (int k = 0; k < vertical; k++)
                        covered[i + k * w] = true;
                }
                else
                {
                    stroke = new Stroke(x, y, horizontal, Vertical: false);
                    for (int k = 0; k < horizontal; k++)
                        covered[i + k] = true;
                }

                (strokes[color] ??= []).Add(stroke);
                counts[color] += stroke.Length;
            }
        }

        List<ColorPass> passes = [];
        foreach (int color in Enumerable.Range(0, plan.Palette.Count).Where(c => counts[c] > 0).OrderByDescending(c => counts[c]))
            passes.Add(new ColorPass(color, plan.Palette[color], Serpentine(strokes[color])));

        return new StrokePlan(w, h, passes);
    }

    /// <summary>The strokes arrive row by row, left to right. Every other row goes right to left instead, so the mouse does not jump back.</summary>
    private static List<Stroke> Serpentine(List<Stroke> strokes)
    {
        List<Stroke> ordered = new(strokes.Count);

        for (int start = 0; start < strokes.Count;)
        {
            int end = start;
            while (end < strokes.Count && strokes[end].Y == strokes[start].Y)
                end++;

            if (strokes[start].Y % 2 == 0)
                ordered.AddRange(strokes.GetRange(start, end - start));
            else
                for (int k = end - 1; k >= start; k--)
                    ordered.Add(strokes[k]);

            start = end;
        }

        return ordered;
    }
}
