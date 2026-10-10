namespace RustyPainter.Interface;

/// <summary>
/// Provides the surface catalog and works out where things are on screen for a given game resolution.
/// </summary>
public interface ISurfaceService
{
    /// <summary>The loaded catalog.</summary>
    SurfaceCatalog Catalog { get; }

    /// <summary>
    /// Scales a surface and the paint screen controls from the catalog's reference resolution to a game
    /// running at <paramref name="screenWidth"/> by <paramref name="screenHeight"/>.
    /// </summary>
    /// <exception cref="System.ArgumentException">The resolution is not positive.</exception>
    ResolvedLayout Resolve(Surface surface, int screenWidth, int screenHeight);
}
