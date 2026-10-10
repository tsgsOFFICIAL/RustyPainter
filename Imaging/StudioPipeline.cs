using RustyPainter.Interface;
using System;
using System.Collections.Generic;

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
public sealed record StudioSettings(
    PlacementSettings Placement,
    AdjustmentSettings Adjustments,
    ColorMode Mode = ColorMode.Swatches,
    int MaxColors = 16,
    Dithering Dithering = Dithering.Smooth);

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

        IReadOnlyList<Rgb> palette = settings.Mode == ColorMode.Swatches
            ? swatches
            : MedianCut.BuildPalette(cells, settings.MaxColors);

        int[] indices = Quantizer.Map(cells, palette, settings.Dithering);
        return new PaintPlan(gridWidth, gridHeight, palette, indices, cells);
    }
}
