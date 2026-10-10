using System.Collections.Generic;
using System;

namespace RustyPainter.Imaging;

/// <summary>A rectangle of pixels in an image, with how many pixels in it are set.</summary>
/// <param name="X1">The first column.</param>
/// <param name="Y1">The first row.</param>
/// <param name="X2">The last column.</param>
/// <param name="Y2">The last row.</param>
/// <param name="Pixels">How many pixels inside it were painted.</param>
public readonly record struct PixelBox(int X1, int Y1, int X2, int Y2, int Pixels)
{
    public int Width => X2 - X1 + 1;
    public int Height => Y2 - Y1 + 1;
    public double CenterX => (X1 + X2) / 2.0;
    public double CenterY => (Y1 + Y2) / 2.0;
}

/// <summary>
/// Reads what a test painting did to the canvas, by comparing a picture of the canvas before and after. Pure, so it can be
/// checked on made up pictures. Nothing here looks at the screen.
/// </summary>
public static class CanvasMeasure
{
    /// <summary>
    /// Marks every pixel that is clearly different between two pictures of the same size. Pictures of the game are never
    /// exactly equal twice, because the sign shimmers a little, so a small difference is not a change.
    /// </summary>
    /// <param name="before">The canvas before painting.</param>
    /// <param name="after">The canvas after painting.</param>
    /// <param name="threshold">How far the three color values together have to move to count.</param>
    public static bool[] Changed(PixelBuffer before, PixelBuffer after, int threshold = 90)
    {
        if (before.Width != after.Width || before.Height != after.Height)
            throw new ArgumentException("The two pictures have to be the same size.");

        bool[] mask = new bool[before.Width * before.Height];

        for (int i = 0; i < mask.Length; i++)
        {
            int o = i * 4;
            int difference = Math.Abs(before.Data[o] - after.Data[o]) + Math.Abs(before.Data[o + 1] - after.Data[o + 1]) + Math.Abs(before.Data[o + 2] - after.Data[o + 2]);
            mask[i] = difference >= threshold;
        }

        return mask;
    }

    /// <summary>The smallest rectangle that holds every marked pixel, or <see langword="null"/> when none is marked.</summary>
    public static PixelBox? Bounds(bool[] mask, int width)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1, count = 0;

        for (int i = 0; i < mask.Length; i++)
        {
            if (!mask[i])
                continue;

            int x = i % width, y = i / width;
            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
            count++;
        }

        return count == 0 ? null : new PixelBox(minX, minY, maxX, maxY, count);
    }

    /// <summary>
    /// Finds each separate blob of marked pixels, left to right. A blob smaller than <paramref name="minimum"/> pixels
    /// is noise and is dropped.
    /// </summary>
    public static IReadOnlyList<PixelBox> Blobs(bool[] mask, int width, int minimum = 4)
    {
        int height = mask.Length / width;
        bool[] seen = new bool[mask.Length];
        List<PixelBox> found = [];
        Stack<int> stack = new();

        for (int start = 0; start < mask.Length; start++)
        {
            if (!mask[start] || seen[start])
                continue;

            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1, count = 0;
            stack.Push(start);
            seen[start] = true;

            while (stack.Count > 0)
            {
                int p = stack.Pop();
                int x = p % width, y = p / width;
                count++;
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);

                foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                        continue;

                    int n = ny * width + nx;
                    if (mask[n] && !seen[n])
                    {
                        seen[n] = true;
                        stack.Push(n);
                    }
                }
            }

            if (count >= minimum)
                found.Add(new PixelBox(minX, minY, maxX, maxY, count));
        }

        found.Sort((a, b) => a.X1.CompareTo(b.X1));
        return found;
    }

    /// <summary>
    /// How many screen pixels one brush unit is, from the blob a square dot of a known size left behind. The width and the
    /// height are given on their own, because a texture that is stretched onto the canvas can differ between them.
    /// </summary>
    /// <param name="dot">The blob the dot left.</param>
    /// <param name="brushSize">The brush size that was typed in.</param>
    public static (double X, double Y) PixelsPerUnit(PixelBox dot, int brushSize)
        => ((double)dot.Width / brushSize, (double)dot.Height / brushSize);
}
