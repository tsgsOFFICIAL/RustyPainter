using RustyPainter.Interface;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RustyPainter.Imaging;

/// <summary>Which colors the game offers while painting.</summary>
public enum ColorMode
{
    /// <summary>The fixed swatches the game shows. Quick to pick, every color is exact.</summary>
    Swatches,

    /// <summary>Any color, typed in as hex. Better pictures, one color change per color used.</summary>
    Free
}

/// <summary>Everything the user can change about how a picture becomes paint.</summary>
/// <param name="Placement">Where the picture sits on the canvas.</param>
/// <param name="Adjustments">Filter, brightness, contrast and saturation.</param>
/// <param name="Mode">Swatch colors or free colors.</param>
/// <param name="MaxColors">In free mode, how many colors the picture is reduced to.</param>
/// <param name="Dithering">How color differences are spread around.</param>
/// <param name="Background">A color painted under the whole picture, so no cell is left unpainted. Null leaves empty areas as they are.</param>
public sealed record StudioSettings(
    PlacementSettings Placement,
    AdjustmentSettings Adjustments,
    ColorMode Mode = ColorMode.Swatches,
    int MaxColors = 16,
    Dithering Dithering = Dithering.Smooth,
    Rgb? Background = null);

/// <summary>How big the grid of cells is for a surface and a brush size.</summary>
public static class StudioGeometry
{
    /// <summary>Screen pixels per texture unit measured on the medium wooden sign at 2560x1440, used to guess the texture of surfaces that have not been measured.</summary>
    public const double EstimatedPixelsPerUnit = 1.69;

    /// <summary>The biggest brush the game allows.</summary>
    public const int MaxBlockSize = 100;

    /// <summary>Whether the texture size of the surface is known, as opposed to estimated.</summary>
    public static bool IsTextureKnown(Surface surface) => surface.Texture is not null;

    /// <summary>The texture size in game units: the real one when it is known, otherwise an estimate from the canvas on screen.</summary>
    public static (int Width, int Height) Texture(Surface surface)
    {
        if (surface.Texture is { } texture)
            return (texture.Width, texture.Height);

        return (
            Math.Max(1, (int)Math.Round(surface.Canvas.Width / EstimatedPixelsPerUnit)),
            Math.Max(1, (int)Math.Round(surface.Canvas.Height / EstimatedPixelsPerUnit)));
    }

    /// <summary>The number of cells across and down when every cell is one brush stroke of <paramref name="blockSize"/> units.</summary>
    public static (int Width, int Height) Grid(Surface surface, int blockSize)
    {
        int block = Math.Clamp(blockSize, 1, MaxBlockSize);
        (int width, int height) = Texture(surface);

        return ((int)Math.Ceiling(width / (double)block), (int)Math.Ceiling(height / (double)block));
    }
}

public static class StudioPipeline
{
    /// <summary>How many pixels along its longer side the reference picture has at least, however coarse the grid is.</summary>
    public const int ReferenceDetail = 1024;

    /// <summary>
    /// Turns an already oriented picture into a plan: place it on the grid, adjust it, choose the colors
    /// (the game's swatches or an adaptive palette), and match every cell to a color.
    /// </summary>
    /// <param name="oriented">The picture after rotating and flipping.</param>
    /// <param name="settings">The user's choices.</param>
    /// <param name="gridWidth">Cells across.</param>
    /// <param name="gridHeight">Cells down.</param>
    /// <param name="swatches">The colors the game offers in swatch mode for this surface.</param>
    public static PaintPlan Run(PixelBuffer oriented, StudioSettings settings, int gridWidth, int gridHeight, IReadOnlyList<Rgb> swatches)
    {
        PixelBuffer cells = PlacementEngine.Resample(oriented, settings.Placement, gridWidth, gridHeight);
        cells = Adjustments.Apply(cells, settings.Adjustments);

        // The background goes under the picture after the adjustments, so a filter never changes the color that was chosen.
        if (settings.Background is { } background)
            cells = Underlay(cells, background);

        IReadOnlyList<Rgb> palette = settings.Mode == ColorMode.Swatches
            ? swatches
            : MedianCut.BuildPalette(cells, settings.MaxColors);

        // The background should come out exactly as chosen, not as a near miss.
        if (settings.Background is { } exact && settings.Mode == ColorMode.Free)
            palette = WithExact(palette, exact);

        int[] indices = Quantizer.Map(cells, palette, settings.Dithering);
        PaintPlan plan = new(gridWidth, gridHeight, palette, indices, PlaceOriginal(oriented, settings.Placement, gridWidth, gridHeight, settings.Background));

        // Group the cells into strokes here, on the worker thread, so asking for the count later is instant.
        _ = plan.Strokes;
        return plan;
    }

    /// <summary>Lays the picture over a solid color, so every pixel ends up fully opaque.</summary>
    public static PixelBuffer Underlay(PixelBuffer picture, Rgb background)
    {
        PixelBuffer result = new(picture.Width, picture.Height);

        for (int i = 0; i < picture.Data.Length; i += 4)
        {
            int alpha = picture.Data[i + 3];
            result.Data[i] = (byte)((picture.Data[i] * alpha + background.R * (255 - alpha) + 127) / 255);
            result.Data[i + 1] = (byte)((picture.Data[i + 1] * alpha + background.G * (255 - alpha) + 127) / 255);
            result.Data[i + 2] = (byte)((picture.Data[i + 2] * alpha + background.B * (255 - alpha) + 127) / 255);
            result.Data[i + 3] = 255;
        }

        return result;
    }

    /// <summary>Makes sure the palette holds <paramref name="color"/> itself, by turning its nearest entry into it.</summary>
    private static IReadOnlyList<Rgb> WithExact(IReadOnlyList<Rgb> palette, Rgb color)
    {
        if (palette.Contains(color))
            return palette;

        Lab target = ColorMath.ToLab(color.R, color.G, color.B);
        int nearest = 0;
        double best = double.MaxValue;

        for (int i = 0; i < palette.Count; i++)
        {
            double distance = target.DistanceSquaredTo(ColorMath.ToLab(palette[i].R, palette[i].G, palette[i].B));
            if (distance < best)
            {
                best = distance;
                nearest = i;
            }
        }

        List<Rgb> result = [.. palette];
        result[nearest] = color;
        return result;
    }

    /// <summary>
    /// The picture placed on the canvas the same way as the plan, but untouched: no filter, no adjustments, no
    /// colors matched and not reduced to the grid, so the brush size does not change how it looks. It has the same
    /// shape as the grid, so it lines up with the plan when both are stretched over the canvas.
    /// </summary>
    private static PixelBuffer PlaceOriginal(PixelBuffer oriented, PlacementSettings placement, int gridWidth, int gridHeight, Rgb? background)
    {
        double scale = Math.Max(1, ReferenceDetail / (double)Math.Max(gridWidth, gridHeight));
        int width = Math.Max(1, (int)Math.Round(gridWidth * scale));
        int height = Math.Max(1, (int)Math.Round(gridHeight * scale));

        PixelBuffer placed = PlacementEngine.Resample(oriented, placement, width, height);

        // With a background chosen the picture sits on it, as it will on the canvas.
        return background is { } color ? Underlay(placed, color) : placed;
    }
}
