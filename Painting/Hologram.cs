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

/// <summary>
/// A small box drawn over the game while painting, showing how far it has come, so the user does not have to leave the game
/// to see it. Like the hologram it never takes a click or the focus.
/// </summary>
public interface IStatusOverlayService
{
    /// <summary>Whether the box is on screen.</summary>
    bool IsShown { get; }

    /// <summary>Shows the box over a rectangle of the screen. It draws itself from the object it is given, and keeps up with it.</summary>
    /// <param name="content">The painting page, which says what the box shows.</param>
    /// <param name="area">Where it goes, in screen pixels.</param>
    void Show(object content, ScreenRect area);

    /// <summary>Takes the box off the screen.</summary>
    void Hide();
}
