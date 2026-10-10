using RustyPainter.Interface;
using System.Reflection;
using System.Text.Json;
using System.Linq;
using System.IO;
using System;

namespace RustyPainter.Service;

/// <inheritdoc cref="ISurfaceService" />
public sealed class SurfaceService : ISurfaceService
{
    private const string ResourceName = "surfaces.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>Loads the catalog embedded in the app and checks it is usable.</summary>
    /// <exception cref="InvalidDataException">The catalog is missing or invalid. This is a build problem, not a user problem.</exception>
    public SurfaceService() : this(LoadEmbedded())
    {
    }

    /// <summary>Uses an already loaded catalog, for tests.</summary>
    public SurfaceService(SurfaceCatalog catalog)
    {
        Validate(catalog);
        Catalog = catalog;
    }

    public SurfaceCatalog Catalog { get; }

    public ResolvedLayout Resolve(Surface surface, int screenWidth, int screenHeight)
        => Resolve(surface, screenWidth, screenHeight, 0, 0);

    public ResolvedLayout Resolve(Surface surface, GameWindow window)
    {
        if (window.IsMinimized || window.Width <= 0 || window.Height <= 0)
            throw new ArgumentException("The game window is minimized or has no size, so there is nothing on screen to resolve.");

        return Resolve(surface, window.Width, window.Height, window.Client.X1, window.Client.Y1);
    }

    private ResolvedLayout Resolve(Surface surface, int screenWidth, int screenHeight, double originX, double originY)
    {
        if (screenWidth <= 0 || screenHeight <= 0)
            throw new ArgumentException($"The game resolution must be positive, got {screenWidth}x{screenHeight}.");

        // The coordinates were recorded on a 16:9 screen. Scaling each axis on its own is exact for
        // other 16:9 resolutions; other aspect ratios need checking against the game before trusting them.
        double sx = (double)screenWidth / Catalog.ReferenceWidth;
        double sy = (double)screenHeight / Catalog.ReferenceHeight;

        ScreenRect Place(ScreenRect rect) => rect.Scale(sx, sy).Offset(originX, originY);

        GameControls c = Catalog.Controls;
        ColorGrid g = c.ColorGrid;

        ColorGrid grid = new(Place(g.Area), g.Columns, g.Rows, g.CellWidth * sx, g.CellHeight * sy, g.GapX * sx, g.GapY * sy);
        GameControls controls = new(
            Place(c.ResetCamera),
            Place(c.BrushSize),
            Place(c.Spacing),
            Place(c.Opacity),
            Place(c.BrushMode),
            Place(c.PaintTool),
            grid);

        return new ResolvedLayout(surface, screenWidth, screenHeight, Place(surface.Canvas), controls, grid);
    }

    private static SurfaceCatalog LoadEmbedded()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException($"The embedded resource '{ResourceName}' is missing.");

        try
        {
            return JsonSerializer.Deserialize<SurfaceCatalog>(stream, SerializerOptions)
                ?? throw new InvalidDataException($"'{ResourceName}' is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"'{ResourceName}' is not valid: {ex.Message}", ex);
        }
    }

    /// <summary>Fails loudly on a bad catalog, so a typo in the data file is caught at startup and not mid painting.</summary>
    private static void Validate(SurfaceCatalog catalog)
    {
        if (catalog.ReferenceWidth <= 0 || catalog.ReferenceHeight <= 0)
            throw new InvalidDataException("The reference resolution must be positive.");

        if (catalog.BrushSizes is not { Length: > 0 } || catalog.BrushSizes.Any(size => size <= 0))
            throw new InvalidDataException("The brush sizes must be a non-empty list of positive numbers.");

        ColorGrid grid = catalog.Controls.ColorGrid;
        if (catalog.Palette.Length != grid.Columns * grid.Rows)
            throw new InvalidDataException($"The palette has {catalog.Palette.Length} colours but the colour grid has {grid.Columns * grid.Rows} swatches.");

        string[] badColors = catalog.Palette.Where(color => !IsHexColor(color)).ToArray();
        if (badColors.Length > 0)
            throw new InvalidDataException($"These palette entries are not #RRGGBB: {string.Join(", ", badColors)}.");

        string[] duplicates = catalog.Palette.GroupBy(color => color, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        if (duplicates.Length > 0)
            throw new InvalidDataException($"The palette repeats these colours, so one swatch can never be picked: {string.Join(", ", duplicates)}.");

        foreach (Surface surface in catalog.Surfaces)
        {
            if (string.IsNullOrWhiteSpace(surface.Id) || string.IsNullOrWhiteSpace(surface.Name))
                throw new InvalidDataException("Every surface needs an id and a name.");

            if (surface.Canvas.Width <= 0 || surface.Canvas.Height <= 0)
                throw new InvalidDataException($"The canvas of '{surface.Id}' has no area (the second corner must be below and right of the first).");

            if (surface.Canvas.X1 < 0 || surface.Canvas.Y1 < 0 ||
                surface.Canvas.X2 > catalog.ReferenceWidth || surface.Canvas.Y2 > catalog.ReferenceHeight)
                throw new InvalidDataException($"The canvas of '{surface.Id}' is outside the {catalog.ReferenceWidth}x{catalog.ReferenceHeight} reference screen.");
        }

        string[] repeatedIds = catalog.Surfaces.GroupBy(surface => surface.Id).Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        if (repeatedIds.Length > 0)
            throw new InvalidDataException($"These surface ids are used more than once: {string.Join(", ", repeatedIds)}.");
    }

    private static bool IsHexColor(string value)
        => value.Length == 7 && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit);
}
