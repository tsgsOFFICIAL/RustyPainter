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
/// Where the clickable parts of Rust's paint screen are for one family of surfaces. Surfaces that share a
/// panel arrangement (the wooden signs, for example) share a layout.
/// </summary>
/// <param name="Clear">The button that clears the canvas.</param>
/// <param name="ResetCamera">The button that resets the view of the canvas.</param>
/// <param name="Undo">The undo button.</param>
/// <param name="Redo">The redo button.</param>
/// <param name="ToolBrush">The paint brush tool.</param>
/// <param name="ToolEraser">The eraser tool.</param>
/// <param name="ToolPicker">The colour picker tool.</param>
/// <param name="BrushShapes">The brush shape buttons, left to right.</param>
/// <param name="Size">The brush size value, which can be clicked to type a number.</param>
/// <param name="Spacing">The brush spacing value.</param>
/// <param name="Opacity">The brush opacity value.</param>
/// <param name="ColorHex">The hex colour field.</param>
/// <param name="ColorArea">The saturation and brightness square of the colour picker.</param>
/// <param name="HueBar">The hue bar of the colour picker.</param>
/// <param name="SavedColors">The saved colour slots, top to bottom.</param>
/// <param name="AddColor">The button that saves the current colour into a slot.</param>
/// <param name="SaveChanges">The button that saves the painting and leaves the paint screen.</param>
/// <param name="Cancel">The button that leaves the paint screen without saving.</param>
public sealed record PaintLayout(
    ScreenRect Clear,
    ScreenRect ResetCamera,
    ScreenRect Undo,
    ScreenRect Redo,
    ScreenRect ToolBrush,
    ScreenRect ToolEraser,
    ScreenRect ToolPicker,
    ScreenRect[] BrushShapes,
    ScreenRect Size,
    ScreenRect Spacing,
    ScreenRect Opacity,
    ScreenRect ColorHex,
    ScreenRect ColorArea,
    ScreenRect HueBar,
    ScreenRect[] SavedColors,
    ScreenRect AddColor,
    ScreenRect SaveChanges,
    ScreenRect Cancel)
{
    /// <summary>Returns a copy of this layout with <paramref name="transform"/> applied to every rectangle.</summary>
    public PaintLayout Map(System.Func<ScreenRect, ScreenRect> transform) => new(
        transform(Clear), transform(ResetCamera), transform(Undo), transform(Redo),
        transform(ToolBrush), transform(ToolEraser), transform(ToolPicker),
        BrushShapes.Select(transform).ToArray(),
        transform(Size), transform(Spacing), transform(Opacity),
        transform(ColorHex), transform(ColorArea), transform(HueBar),
        SavedColors.Select(transform).ToArray(),
        transform(AddColor), transform(SaveChanges), transform(Cancel));

    /// <summary>Every rectangle in the layout, for checks that apply to all of them.</summary>
    public ScreenRect[] All() =>
    [
        Clear, ResetCamera, Undo, Redo, ToolBrush, ToolEraser, ToolPicker,
        .. BrushShapes, Size, Spacing, Opacity, ColorHex, ColorArea, HueBar,
        .. SavedColors, AddColor, SaveChanges, Cancel
    ];
}

/// <summary>One paintable surface and where its canvas sits on screen.</summary>
/// <param name="Id">A stable identifier, used in saved projects.</param>
/// <param name="Name">The name shown in the UI.</param>
/// <param name="Layout">The key of the <see cref="PaintLayout"/> this surface's paint screen uses.</param>
/// <param name="Canvas">The canvas rectangle at the catalog's reference resolution.</param>
public sealed record Surface(string Id, string Name, string Layout, ScreenRect Canvas);

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
    System.Collections.Generic.Dictionary<string, PaintLayout> Layouts,
    Surface[] Surfaces);

/// <summary>
/// A surface and its paint screen controls for one specific game resolution: every coordinate here is in
/// real screen pixels and can be used directly for mouse input.
/// </summary>
public sealed class ResolvedLayout
{
    public ResolvedLayout(Surface surface, int screenWidth, int screenHeight, ScreenRect canvas, PaintLayout controls)
    {
        Surface = surface;
        ScreenWidth = screenWidth;
        ScreenHeight = screenHeight;
        Canvas = canvas;
        Controls = controls;
    }

    public Surface Surface { get; }
    public int ScreenWidth { get; }
    public int ScreenHeight { get; }

    /// <summary>The surface's canvas in screen pixels.</summary>
    public ScreenRect Canvas { get; }

    /// <summary>The paint screen controls in screen pixels.</summary>
    public PaintLayout Controls { get; }
}
