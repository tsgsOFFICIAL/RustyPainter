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
}

/// <summary>
/// The grid of colour swatches in Rust's paint screen. Swatches are numbered left to right, then top to
/// bottom, which is also the order of <see cref="SurfaceCatalog.Palette"/>.
/// </summary>
/// <param name="Area">The whole grid.</param>
/// <param name="Columns">Swatches per row.</param>
/// <param name="Rows">Number of rows.</param>
/// <param name="CellWidth">Width of one swatch.</param>
/// <param name="CellHeight">Height of one swatch.</param>
/// <param name="GapX">Horizontal gap between swatches.</param>
/// <param name="GapY">Vertical gap between swatches.</param>
public sealed record ColorGrid(ScreenRect Area, int Columns, int Rows, double CellWidth, double CellHeight, double GapX, double GapY);

/// <summary>Where the clickable controls of Rust's paint screen are.</summary>
public sealed record GameControls(
    ScreenRect ResetCamera,
    ScreenRect BrushSize,
    ScreenRect Spacing,
    ScreenRect Opacity,
    ScreenRect BrushMode,
    ScreenRect PaintTool,
    ColorGrid ColorGrid);

/// <summary>One paintable surface and where its canvas sits on screen.</summary>
/// <param name="Id">A stable identifier, used in saved projects.</param>
/// <param name="Name">The name shown in the UI.</param>
/// <param name="Canvas">The canvas rectangle at the catalog's reference resolution.</param>
public sealed record Surface(string Id, string Name, ScreenRect Canvas);

/// <summary>
/// Everything known about Rust's paint screen, recorded once at a reference resolution and loaded from
/// <c>Data/surfaces.json</c>. Use <see cref="ISurfaceService.Resolve"/> to get positions for the real game resolution.
/// </summary>
/// <param name="ReferenceWidth">Width of the screen the coordinates were recorded on.</param>
/// <param name="ReferenceHeight">Height of the screen the coordinates were recorded on.</param>
/// <param name="BrushSizes">The brush sizes the painter can use, in pixels, largest first.</param>
/// <param name="Controls">Positions of the paint screen controls.</param>
/// <param name="Palette">The in-game colours as #RRGGBB, in swatch order.</param>
/// <param name="Surfaces">The paintable surfaces.</param>
public sealed record SurfaceCatalog(
    int ReferenceWidth,
    int ReferenceHeight,
    int[] BrushSizes,
    GameControls Controls,
    string[] Palette,
    Surface[] Surfaces);

/// <summary>
/// A surface and the paint screen controls for one specific game resolution: every coordinate here is in
/// real screen pixels and can be used directly for mouse input.
/// </summary>
public sealed class ResolvedLayout
{
    private readonly ColorGrid _grid;

    public ResolvedLayout(Surface surface, int screenWidth, int screenHeight, ScreenRect canvas, GameControls controls, ColorGrid grid)
    {
        Surface = surface;
        ScreenWidth = screenWidth;
        ScreenHeight = screenHeight;
        Canvas = canvas;
        Controls = controls;
        _grid = grid;
    }

    public Surface Surface { get; }
    public int ScreenWidth { get; }
    public int ScreenHeight { get; }

    /// <summary>The surface's canvas in screen pixels.</summary>
    public ScreenRect Canvas { get; }

    /// <summary>The paint screen controls in screen pixels.</summary>
    public GameControls Controls { get; }

    /// <summary>Gets the point to click for the palette swatch at <paramref name="paletteIndex"/>.</summary>
    public ScreenPoint ColorCenter(int paletteIndex)
    {
        int row = paletteIndex / _grid.Columns;
        int column = paletteIndex % _grid.Columns;

        return new ScreenPoint(
            _grid.Area.X1 + column * (_grid.CellWidth + _grid.GapX) + _grid.CellWidth / 2,
            _grid.Area.Y1 + row * (_grid.CellHeight + _grid.GapY) + _grid.CellHeight / 2);
    }
}
