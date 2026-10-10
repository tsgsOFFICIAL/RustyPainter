using RustyPainter.Imaging;
using RustyPainter.Localization;
using System;

namespace RustyPainter.Painting;

/// <summary>Works out roughly how long painting takes, from the number of strokes and the pace.</summary>
public static class PaintTimeEstimator
{
    /// <summary>
    /// Windows wakes a waiting program only about every 15.6 ms, so every wait lasts a whole number of those.
    /// Counting that in is the difference between an estimate that is close and one that is far too hopeful.
    /// </summary>
    public const double TimerStep = 15.625;

    /// <summary>Roughly how long painting takes. It does not include the time to open the paint screen.</summary>
    /// <param name="strokes">The strokes to paint.</param>
    /// <param name="timing">The pace.</param>
    /// <param name="mode">Swatch colors are picked with a click, free colors are typed.</param>
    /// <param name="scrollOut">How many times the wheel is turned to show the whole canvas.</param>
    /// <param name="clearFirst">Whether the canvas is cleared first.</param>
    public static TimeSpan Estimate(StrokePlan strokes, PaintTiming timing, ColorMode mode, int scrollOut = 0, bool clearFirst = true)
    {
        double ms = 0;

        // Setting up: camera, scrolling, tool, clear, shape, and three typed brush values.
        ms += Click(timing, timing.CameraReset);
        ms += scrollOut * Wait(timing.ScrollGap);
        ms += Click(timing, timing.AfterClick) * (clearFirst ? 3 : 2);
        ms += 3 * TypedField(timing, characters: 3);

        int drags = strokes.StrokeCount - strokes.ClickCount;
        ms += drags * (2 * (Wait(timing.Settle) + Wait(timing.Hold)) + Wait(timing.AfterStroke));
        ms += strokes.ClickCount * (Wait(timing.Settle) + Wait(timing.Hold) + Wait(timing.AfterStroke));

        // Each color is picked once.
        ms += strokes.Passes.Count * (mode == ColorMode.Swatches ? Click(timing, timing.AfterClick) : TypedField(timing, characters: 6));

        return TimeSpan.FromMilliseconds(ms);
    }

    /// <summary>Turns a duration into text such as "About 4 minutes".</summary>
    public static string Describe(TimeSpan time)
    {
        if (time < TimeSpan.FromSeconds(10))
            return Loc.Get("TimeFewSeconds");

        if (time < TimeSpan.FromMinutes(1))
            return Loc.Count("TimeAboutSeconds", (long)(Math.Round(time.TotalSeconds / 5) * 5));

        if (time < TimeSpan.FromMinutes(90))
        return Loc.Count("TimeAboutMinutes", (long)Math.Round(time.TotalMinutes));

        return Loc.Format("TimeAboutHours", time.TotalHours);
    }

    private static double Wait(int milliseconds)
        => milliseconds <= 0 ? 0 : Math.Ceiling(milliseconds / TimerStep) * TimerStep;

    private static double Click(PaintTiming timing, int after)
        => Wait(timing.Settle) + Wait(timing.Hold) + Wait(after);

    private static double TypedField(PaintTiming timing, int characters)
        => Click(timing, timing.FieldOpen) + characters * Wait(timing.KeyGap) + Wait(timing.AfterField);
}
