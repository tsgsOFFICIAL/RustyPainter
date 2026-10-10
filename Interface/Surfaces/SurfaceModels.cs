using System.Collections.Generic;
using System.Linq;

namespace RustyPainter.Interface;

/// <summary>A point in screen pixels.</summary>
public readonly record struct ScreenPoint(double X, double Y);

/// <summary>A rectangle in screen pixels, given by its top-left and bottom-right corners.</summary>
public readonly record struct ScreenRect(double X1, double Y1, double X2, double Y2)
{
    public double Width => X2 - X1;
    public double Height => Y2 - Y1;
    public ScreenPoint Center => new(X1 + Width / 2, Y1 + Height / 2);

    /// <summary>Returns this rectangle with x values multiplied by <paramref name="sx"/> and y values by <paramref name="sy"/>.</summary>
    public ScreenRect Scale(double sx, double sy) => new(X1 * sx, Y1 * sy, X2 * sx, Y2 * sy);

    /// <summary>Returns this rectangle moved by <paramref name="dx"/> and <paramref name="dy"/>.</summary>
    public ScreenRect Offset(double dx, double dy) => new(X1 + dx, Y1 + dy, X2 + dx, Y2 + dy);
}

/// <summary>
/// The grid of color swatches shown in swatch mode, together with the color of each swatch. Swatches are
/// numbered left to right, then top to bottom, which is also the order of <see cref="Colors"/>. Different
/// surfaces have different swatches (most have 64, neon signs have 8).
/// </summary>
/// <param name="Area">The whole grid.</param>
/// <param name="Columns">Swatches per row.</param>
/// <param name="Rows">Number of rows.</param>
/// <param name="CellWidth">Width of one swatch.</param>
/// <param name="CellHeight">Height of one swatch.</param>
/// <param name="GapX">Horizontal gap between swatches.</param>
/// <param name="GapY">Vertical gap between swatches.</param>
/// <param name="Colors">The color of each swatch as #RRGGBB.</param>
public sealed record ColorGrid(ScreenRect Area, int Columns, int Rows, double CellWidth, double CellHeight, double GapX, double GapY, string[] Colors)
{
    /// <summary>The number of swatches in the grid.</summary>
    public int Count => Columns * Rows;

    /// <summary>Gets the point to click for the swatch at <paramref name="index"/>.</summary>
    public ScreenPoint CellCenter(int index)
    {
        int row = index / Columns;
        int column = index % Columns;

        return new ScreenPoint(
            Area.X1 + column * (CellWidth + GapX) + CellWidth / 2,
            Area.Y1 + row * (CellHeight + GapY) + CellHeight / 2);
    }

    /// <summary>Scales every size and position in the grid, then moves it by the given offset.</summary>
    public ColorGrid Place(double sx, double sy, double dx, double dy)
        => new(Area.Scale(sx, sy).Offset(dx, dy), Columns, Rows, CellWidth * sx, CellHeight * sy, GapX * sx, GapY * sy, Colors);
}

/// <summary>The controls shown in free color mode, where any color can be typed or picked.</summary>
/// <param name="ColorHex">The hex color field.</param>
/// <param name="ColorArea">The saturation and brightness square.</param>
/// <param name="HueBar">The hue bar.</param>
/// <param name="SavedColors">The saved color slots, top to bottom.</param>
/// <param name="AddColor">The button that saves the current color into a slot.</param>
public sealed record FreeColorControls(ScreenRect ColorHex, ScreenRect ColorArea, ScreenRect HueBar, ScreenRect[] SavedColors, ScreenRect AddColor)
{
    /// <summary>Scales every rectangle, then moves it by the given offset.</summary>
    public FreeColorControls Place(double sx, double sy, double dx, double dy)
    {
        ScreenRect Move(ScreenRect rect) => rect.Scale(sx, sy).Offset(dx, dy);
        return new(Move(ColorHex), Move(ColorArea), Move(HueBar), SavedColors.Select(Move).ToArray(), Move(AddColor));
    }
}

/// <summary>
/// Where the clickable parts of Rust's paint screen are, for one color mode of one family of surfaces.
/// Switching color mode changes the height of the right hand panel, so every item in it moves.
/// </summary>
/// <param name="Clear">The button that clears the canvas.</param>
/// <param name="ResetCamera">The button that resets the view of the canvas.</param>
/// <param name="Undo">The undo button.</param>
/// <param name="Redo">The redo button.</param>
/// <param name="ToolBrush">The paint brush tool.</param>
/// <param name="ToolEraser">The eraser tool.</param>
/// <param name="ToolPicker">The color picker tool.</param>
/// <param name="BrushShapes">The brush shape buttons (or presets, on surfaces that only have a few), left to right.</param>
/// <param name="ColorModeToggle">The button in the color header that switches between swatch mode and free color mode.</param>
/// <param name="SaveChanges">The button that saves the painting and leaves the paint screen.</param>
/// <param name="Cancel">The button that leaves the paint screen without saving.</param>
/// <param name="Size">The brush size value, which can be clicked to type a number. Absent on surfaces with fixed brush presets.</param>
/// <param name="Spacing">The brush spacing value. Absent on surfaces with fixed brush presets.</param>
/// <param name="Opacity">The brush opacity value. Absent on surfaces with fixed brush presets.</param>
/// <param name="ColorGrid">The swatch grid, present in swatch mode only.</param>
/// <param name="FreeColor">The free color controls, present in free color mode only.</param>
public sealed record PaintLayout(
    ScreenRect Clear,
    ScreenRect ResetCamera,
    ScreenRect Undo,
    ScreenRect Redo,
    ScreenRect ToolBrush,
    ScreenRect ToolEraser,
    ScreenRect ToolPicker,
    ScreenRect[] BrushShapes,
    ScreenRect ColorModeToggle,
    ScreenRect SaveChanges,
    ScreenRect Cancel,
    ScreenRect? Size = null,
    ScreenRect? Spacing = null,
    ScreenRect? Opacity = null,
    ColorGrid? ColorGrid = null,
    FreeColorControls? FreeColor = null)
{
    /// <summary>Every plain rectangle in the layout, for checks that apply to all of them.</summary>
    public ScreenRect[] AllRects()
    {
        List<ScreenRect> all =
        [
            Clear, ResetCamera, Undo, Redo, ToolBrush, ToolEraser, ToolPicker,
            .. BrushShapes, ColorModeToggle, SaveChanges, Cancel
        ];

        if (Size is { } size)
            all.Add(size);

        if (Spacing is { } spacing)
            all.Add(spacing);

        if (Opacity is { } opacity)
            all.Add(opacity);

        if (ColorGrid is not null)
            all.Add(ColorGrid.Area);

        if (FreeColor is not null)
            all.AddRange([FreeColor.ColorHex, FreeColor.ColorArea, FreeColor.HueBar, .. FreeColor.SavedColors, FreeColor.AddColor]);

        return [.. all];
    }

    /// <summary>Scales the whole layout, then moves it by the given offset.</summary>
    public PaintLayout Place(double sx, double sy, double dx, double dy)
    {
        ScreenRect Move(ScreenRect rect) => rect.Scale(sx, sy).Offset(dx, dy);

        return new PaintLayout(
            Move(Clear), Move(ResetCamera), Move(Undo), Move(Redo),
            Move(ToolBrush), Move(ToolEraser), Move(ToolPicker),
            BrushShapes.Select(Move).ToArray(),
            Move(ColorModeToggle), Move(SaveChanges), Move(Cancel),
            Size is { } size ? Move(size) : null,
            Spacing is { } spacing ? Move(spacing) : null,
            Opacity is { } opacity ? Move(opacity) : null,
            ColorGrid?.Place(sx, sy, dx, dy),
            FreeColor?.Place(sx, sy, dx, dy));
    }
}

/// <summary>The two arrangements of one family of paint screens, one per color mode.</summary>
/// <param name="Swatches">The layout while the game shows the fixed swatch grid.</param>
/// <param name="Free">The layout while the game shows the free color picker.</param>
public sealed record LayoutFamily(PaintLayout Swatches, PaintLayout Free);

/// <summary>The size of a surface's paint texture in the game's own units. One unit is what a brush of size 1 paints.</summary>
/// <param name="Width">Units across.</param>
/// <param name="Height">Units down.</param>
public sealed record TextureSize(int Width, int Height);

/// <summary>One paintable surface and where its canvas sits on screen.</summary>
/// <param name="Id">A stable identifier, used in saved projects.</param>
/// <param name="Name">The name shown in the UI.</param>
/// <param name="Layout">The key of the <see cref="LayoutFamily"/> this surface's paint screen uses.</param>
/// <param name="Canvas">The canvas rectangle at the catalog's reference resolution, measured after <paramref name="ScrollOut"/> scrolls.</param>
/// <param name="ScrollOut">
/// How many times the mouse wheel has to be scrolled out after opening the paint screen before the whole
/// canvas is visible. Some canvases open zoomed in and cropped, which is a game quirk.
/// </param>
/// <param name="Texture">
/// The paint texture size, when it is known. Without it the size is estimated from the canvas on screen.
/// </param>
public sealed record Surface(string Id, string Name, string Layout, ScreenRect Canvas, int ScrollOut = 0, TextureSize? Texture = null);

/// <summary>
/// Everything known about Rust's paint screen, recorded once at a reference resolution and loaded from
/// <c>Data/surfaces.json</c>. Use <see cref="ISurfaceService.Resolve(Surface, int, int)"/> to get positions for the real game resolution.
/// </summary>
/// <param name="ReferenceWidth">Width of the screen the coordinates were recorded on.</param>
/// <param name="ReferenceHeight">Height of the screen the coordinates were recorded on.</param>
/// <param name="Layouts">The paint screen arrangements, by key.</param>
/// <param name="Surfaces">The paintable surfaces.</param>
public sealed record SurfaceCatalog(
    int ReferenceWidth,
    int ReferenceHeight,
    Dictionary<string, LayoutFamily> Layouts,
    Surface[] Surfaces);

/// <summary>
/// A surface and its paint screen controls for one specific game resolution: every coordinate here is in
/// real screen pixels and can be used directly for mouse input.
/// </summary>
public sealed class ResolvedLayout
{
    public ResolvedLayout(Surface surface, int screenWidth, int screenHeight, ScreenRect canvas, PaintLayout swatches, PaintLayout free)
    {
        Surface = surface;
        ScreenWidth = screenWidth;
        ScreenHeight = screenHeight;
        Canvas = canvas;
        Swatches = swatches;
        Free = free;
    }

    public Surface Surface { get; }
    public int ScreenWidth { get; }
    public int ScreenHeight { get; }

    /// <summary>The surface's canvas in screen pixels.</summary>
    public ScreenRect Canvas { get; }

    /// <summary>The controls in screen pixels while the game is in swatch mode.</summary>
    public PaintLayout Swatches { get; }

    /// <summary>The controls in screen pixels while the game is in free color mode.</summary>
    public PaintLayout Free { get; }
}
