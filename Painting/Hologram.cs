using RustyPainter.Imaging;
using RustyPainter.Interface;

namespace RustyPainter.Painting;

/// <summary>
/// Shows the picture over the game, exactly where it is going to be painted, so the position can be checked and
/// moved before any painting starts. The picture never takes a click and never takes the focus from the game.
/// </summary>
public interface IHologramService
{
    /// <summary>Whether the picture is on screen.</summary>
    bool IsShown { get; }

    /// <summary>Shows the picture over a rectangle of the screen, or moves and updates it when it is already there.</summary>
    /// <param name="picture">The picture, one pixel per cell. Empty cells are see-through.</param>
    /// <param name="area">Where it goes, in screen pixels.</param>
    /// <param name="opacity">How solid it is, from 0 to 1.</param>
    void Show(PixelBuffer picture, ScreenRect area, double opacity);

    /// <summary>Takes the picture off the screen.</summary>
    void Hide();
}
