using RustyPainter.Imaging;
using RustyPainter.Interface;

namespace RustyPainter.Painting;

/// <summary>Everything the painter needs to paint what Studio prepared.</summary>
/// <param name="Plan">The picture as cells.</param>
/// <param name="Surface">What it is painted on.</param>
/// <param name="Mode">Whether the game is used in swatch mode or free color mode.</param>
/// <param name="BlockSize">The brush size in game units, which is also the size of one cell.</param>
/// <param name="PictureName">The name of the picture, for showing to the user.</param>
public sealed record PaintRequest(PaintPlan Plan, Surface Surface, ColorMode Mode, int BlockSize, string PictureName);

/// <summary>Makes painters that send input to the real machine. Tests make ones that do not.</summary>
public interface IPaintEngineFactory
{
    PaintEngine Create(PaintTiming timing);
}
