namespace RustyPainter.Painting;

/// <summary>
/// How long to wait between the small things the painter does. The game needs a moment to notice each one:
/// too fast and clicks are missed or letters dropped, too slow and a picture takes forever. All in milliseconds.
/// </summary>
/// <param name="Settle">After moving the cursor, before pressing.</param>
/// <param name="Hold">Between pressing and releasing the mouse button, and between a press and the first move of a drag.</param>
/// <param name="AfterClick">After a click on a button or swatch.</param>
/// <param name="FieldOpen">After clicking a number field, before typing, while the game turns it into a text box.</param>
/// <param name="KeyGap">Between two typed keys.</param>
/// <param name="AfterField">After typing into a field.</param>
/// <param name="AfterStroke">After a stroke, before the next one.</param>
/// <param name="ScrollGap">Between two turns of the mouse wheel.</param>
/// <param name="CameraReset">After the camera reset button, while the view moves back.</param>
public sealed record PaintTiming(
    int Settle = 12,
    int Hold = 16,
    int AfterClick = 40,
    int FieldOpen = 200,
    int KeyGap = 60,
    int AfterField = 120,
    int AfterStroke = 6,
    int ScrollGap = 80,
    int CameraReset = 1200)
{
    /// <summary>A pace that is slow enough to be reliable on most machines.</summary>
    public static PaintTiming Default { get; } = new();

    /// <summary>
    /// A pace picked by a slider: 1 is the slowest and 100 the fastest. The painting itself speeds up
    /// the most, the waits for the game's own animations are kept.
    /// </summary>
    public static PaintTiming FromSpeed(int percent)
    {
        double slowness = 1 + (100 - System.Math.Clamp(percent, 1, 100)) / 100.0 * 4;
        int Scale(int value) => System.Math.Max(1, (int)System.Math.Round(value * slowness / 2));

        return new PaintTiming(
            Settle: Scale(12), Hold: Scale(16), AfterClick: Scale(40), FieldOpen: System.Math.Max(120, Scale(200)),
            KeyGap: System.Math.Max(30, Scale(60)), AfterField: Scale(120), AfterStroke: Scale(6),
            ScrollGap: System.Math.Max(40, Scale(80)), CameraReset: 1200);
    }
}
