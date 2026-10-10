using System.Collections.Generic;
using System.Linq;
using RustyPainter.Imaging;
using RustyPainter.Interface;

namespace RustyPainter.Painting;

/// <summary>Everything the painter needs to paint what Studio prepared.</summary>
/// <param name="Plan">The picture as cells.</param>
/// <param name="Surface">What it is painted on.</param>
/// <param name="Mode">Whether the game is used in swatch mode or free color mode.</param>
/// <param name="BlockSize">The brush size in game units, which is also the size of one cell.</param>
/// <param name="PictureName">The name of the picture, for showing to the user.</param>
/// <param name="FramePlans">The picture of every frame, first to last, for an animated sign. The first one is <paramref name="Plan"/>.</param>
/// <param name="FrameSeconds">How long each frame is shown while the frames play over the game, as the preview in Studio does.</param>
public sealed record PaintRequest(PaintPlan Plan, Surface Surface, ColorMode Mode, int BlockSize, string PictureName, IReadOnlyList<PaintPlan>? FramePlans = null, double FrameSeconds = 1.0)
{
    /// <summary>The picture of each frame. One picture unless the surface animates.</summary>
    public IReadOnlyList<PaintPlan> Plans => FramePlans is { Count: > 0 } ? FramePlans : [Plan];

    /// <summary>How many strokes there are in all frames.</summary>
    public int StrokeCount => Plans.Sum(plan => plan.StrokeCount);

    /// <summary>How many different colors all frames use.</summary>
    public int ColorsUsed => Plans.SelectMany(plan => plan.Strokes.Passes.Select(pass => pass.Color.Hex)).Distinct().Count();
}

/// <summary>Makes painters that send input to the real machine. Tests make ones that do not.</summary>
public interface IPaintEngineFactory
{
    PaintEngine Create(PaintTiming timing);
}
