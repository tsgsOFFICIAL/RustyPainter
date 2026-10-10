using System.Globalization;
using RustyPainter.Imaging;
using RustyPainter.Interface;
using RustyPainter.Painting;
using RustyPainter.Service;

namespace RustyPainter.Tests;

/// <summary>
/// Measures a surface in the running game: how many screen pixels one brush unit is, and where the canvas really starts and
/// ends. It paints a few dots of known sizes and one big fill, and copies only the canvas rectangle of the screen (a few
/// hundred pixels across) before and after, while Rust is the window in front. Nothing is kept of those pictures.
/// It does nothing unless RUSTYPAINTER_LIVE=measure; RUSTYPAINTER_SURFACE names the surface to measure and
/// RUSTYPAINTER_LIVE_OUT the file the results go to. It clears the canvas of the surface.
/// </summary>
public class LiveMeasureTests
{
    private static string Mode => Environment.GetEnvironmentVariable("RUSTYPAINTER_LIVE") ?? "";

    private static void Write(string line)
    {
        if (Environment.GetEnvironmentVariable("RUSTYPAINTER_LIVE_OUT") is { Length: > 0 } path)
            File.AppendAllText(path, line + Environment.NewLine);
    }

    private static string N(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static double Luminance(Rgb color) => 0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B;

    [Fact]
    public async Task Measure_the_surface_that_is_open_in_the_game()
    {
        Assert.SkipUnless(Mode == "measure", "Set RUSTYPAINTER_LIVE=measure to run this. It clears the canvas and sends real input to the game.");

        string id = Environment.GetEnvironmentVariable("RUSTYPAINTER_SURFACE") ?? "sign-small";
        CancellationToken stop = TestContext.Current.CancellationToken;

        GameWindowService game = new();
        Win32PaintSensors sensors = new(game);
        Win32ScreenCapture capture = new();

        for (int i = 0; i < 90 && !sensors.IsGameInFront(); i++)
            await Task.Delay(500, stop);

        Assert.True(sensors.IsGameInFront(), "Rust did not come to the front in time.");
        Write($"measuring {id}; rust in front, starting in 3 seconds");
        await Task.Delay(3000, stop);

        GameWindow window = game.Find()!;
        SurfaceService surfaces = new();
        Surface surface = surfaces.Catalog.Surfaces.Single(s => s.Id == id);
        ResolvedLayout layout = surfaces.Resolve(surface, window);
        PaintLayout controls = layout.Swatches;
        Assert.NotNull(controls.Size);

        ScreenRect canvas = layout.Canvas;
        IReadOnlyList<Rgb> swatches = controls.ColorGrid!.Colors.Select(Rgb.Parse).ToList();

        // Look a little beyond the canvas as it is recorded, in case the recorded edges are a few pixels off.
        const int margin = 60;
        ScreenRect region = new(
            Math.Max(window.Client.X1, canvas.X1 - margin), Math.Max(window.Client.Y1, canvas.Y1 - margin),
            Math.Min(window.Client.X2, canvas.X2 + margin), Math.Min(window.Client.Y2, canvas.Y2 + margin));

        PaintEngine Engine(PaintTiming? timing = null)
        {
            PaintEngine engine = new(new Win32PaintInput(), sensors, timing);
            engine.StateChanged += state =>
            {
                if (state == PaintState.Paused)
                    engine.Abort();
            };

            return engine;
        }

        async Task Run(List<PaintStep> steps, PaintTiming? timing = null)
        {
            PaintEngine engine = Engine(timing);
            await engine.RunAsync(new PaintScript(steps), cancellation: stop);
            Assert.True(engine.State == PaintState.Finished, $"Painting stopped: {engine.State} {engine.PauseReason} {engine.FailureMessage}");
        }

        async Task<PixelBuffer> Look()
        {
            await Task.Delay(600, stop);
            Assert.True(sensors.IsGameInFront(), "Rust is no longer in front, so nothing was copied.");
            return capture.Capture(region) ?? throw new InvalidOperationException("The canvas could not be copied.");
        }

        ScreenPoint At(double fx, double fy) => new(canvas.X1 + fx * canvas.Width, canvas.Y1 + fy * canvas.Height);
        Stroke dot = new(0, 0, 1, Vertical: false);

        // 1. Set up and clear, then look at the empty canvas.
        List<PaintStep> setup = [new ResetCameraStep(controls.ResetCamera.Center)];
        if (surface.ScrollOut > 0)
            setup.Add(new ScrollOutStep(surface.ScrollOut));

        setup.AddRange(
        [
            new EnsureColorModeStep(ColorMode.Swatches, controls.ColorModeToggle.Center),
            new ClickControlStep("Brush", controls.ToolBrush.Center),
            new ClickControlStep("Clear", controls.Clear.Center),
            new ClickControlStep("Brush shape", controls.BrushShapes[PaintOptions.SquareShape].Center),
            new SetBrushValueStep("Opacity", controls.Opacity!.Value.Center, "1"),
            new SetBrushValueStep("Spacing", controls.Spacing!.Value.Center, "0.1")
        ]);

        await Run(setup);
        PixelBuffer empty = await Look();

        // Paint in whichever swatch stands out most from the canvas.
        double average = 0;
        for (int i = 0; i < empty.Width * empty.Height; i++)
            average += 0.2126 * empty.Data[i * 4] + 0.7152 * empty.Data[i * 4 + 1] + 0.0722 * empty.Data[i * 4 + 2];

        average /= empty.Width * empty.Height;
        int swatch = average > 110
            ? Enumerable.Range(0, swatches.Count).OrderBy(i => Luminance(swatches[i])).First()
            : Enumerable.Range(0, swatches.Count).OrderByDescending(i => Luminance(swatches[i])).First();
        Write($"canvas average brightness {N(average)}, painting with swatch {swatch} {swatches[swatch].Hex}");

        // 2. Dots of known sizes along the longer side of the canvas.
        (int size, double position)[] dots = [(16, 0.10), (50, 0.34), (100, 0.78)];
        bool wide = canvas.Width >= canvas.Height;
        List<PaintStep> dotSteps = [new SelectSwatchStep(swatch, swatches[swatch], controls.ColorGrid!.CellCenter(swatch))];
        foreach ((int size, double position) in dots)
        {
            ScreenPoint point = wide ? At(position, 0.5) : At(0.5, position);
            dotSteps.Add(new SetBrushValueStep("Size", controls.Size!.Value.Center, size.ToString(CultureInfo.InvariantCulture)));
            dotSteps.Add(new PaintStrokeStep(dot, point, point));
        }

        await Run(dotSteps);
        PixelBuffer withDots = await Look();

        bool[] changedByDots = CanvasMeasure.Changed(empty, withDots);
        Write($"separate pieces found: {CanvasMeasure.Blobs(changedByDots, empty.Width, minimum: 12).Count}");

        // Each dot is looked for around the place it was painted, so a dot that the plank seams cut into pieces is still one dot.
        List<(double X, double Y)> perUnit = [];
        for (int i = 0; i < dots.Length; i++)
        {
            ScreenPoint center = wide ? At(dots[i].position, 0.5) : At(0.5, dots[i].position);
            double nearest = double.MaxValue;
            for (int j = 0; j < dots.Length; j++)
            {
                if (j == i)
                    continue;

                ScreenPoint other = wide ? At(dots[j].position, 0.5) : At(0.5, dots[j].position);
                nearest = Math.Min(nearest, Math.Abs(wide ? other.X - center.X : other.Y - center.Y));
            }

            double reach = Math.Min(dots[i].size * 3.5, nearest * 0.55);

            PixelBox? box = CanvasMeasure.BoundsAround(changedByDots, empty.Width, center.X - region.X1, center.Y - region.Y1, reach);
            Assert.True(box is not null, $"Dot {dots[i].size} left nothing to measure.");

            (double x, double y) = CanvasMeasure.PixelsPerUnit(box.Value, dots[i].size);
            perUnit.Add((x, y));
            Write($"size {dots[i].size}: {box.Value.Width} x {box.Value.Height} px, {N(x)} x {N(y)} px per unit");
        }

        // How much each size adds over the one before is free of the soft edge every dot has.
        for (int i = 1; i < dots.Length; i++)
        {
            double growX = (perUnit[i].X * dots[i].size - perUnit[i - 1].X * dots[i - 1].size) / (dots[i].size - dots[i - 1].size);
            double growY = (perUnit[i].Y * dots[i].size - perUnit[i - 1].Y * dots[i - 1].size) / (dots[i].size - dots[i - 1].size);
            Write($"growth {dots[i - 1].size} to {dots[i].size}: {N(growX)} x {N(growY)} px per unit");
        }

        // The biggest dot is the most exact one, because a pixel of error counts for less.
        (double ppuX, double ppuY) = perUnit[^1];
        Write($"pixels per unit: {N(ppuX)} across, {N(ppuY)} down");

        // 3. One big fill with the biggest brush, which shows where the canvas really ends.
        double footprint = 100 * ppuY;
        double step = 0.8 * footprint;
        List<PaintStep> fill = [new SetBrushValueStep("Size", controls.Size!.Value.Center, "100")];
        for (double y = canvas.Y1 + 2; ; y += step)
        {
            double row = Math.Min(y, canvas.Y2 - 2);
            fill.Add(new PaintStrokeStep(new Stroke(0, 0, 2, Vertical: false), new ScreenPoint(canvas.X1 + 2, row), new ScreenPoint(canvas.X2 - 2, row)));

            if (row >= canvas.Y2 - 2)
                break;
        }

        await Run(fill);
        PixelBuffer filled = await Look();

        PixelBox? area = CanvasMeasure.SolidBounds(CanvasMeasure.Changed(empty, filled), empty.Width);
        Assert.NotNull(area);

        double left = region.X1 + area.Value.X1, top = region.Y1 + area.Value.Y1;
        double right = region.X1 + area.Value.X2 + 1, bottom = region.Y1 + area.Value.Y2 + 1;
        double coverage = (double)area.Value.Pixels / (area.Value.Width * area.Value.Height);
        Write($"recorded canvas {canvas.X1:0},{canvas.Y1:0} to {canvas.X2:0},{canvas.Y2:0} ({canvas.Width:0} x {canvas.Height:0})");
        Write($"measured canvas {left:0},{top:0} to {right:0},{bottom:0} ({right - left:0} x {bottom - top:0}), {N(coverage * 100)} percent filled");
        Write($"texture size {N((right - left) / ppuX)} x {N((bottom - top) / ppuY)} units");

        // Where are the pixels the fill did not change? Dark ones are seams between planks, light ones are wood that white hardly shows on.
        void Explain(string label, PixelBuffer after)
        {
            bool[] mask = CanvasMeasure.Changed(empty, after);
            PixelBox box = area.Value;
            int missed = 0, dark = 0, light = 0, bandRows = 10;
            int[] rowMissed = new int[bandRows], colMissed = new int[bandRows];

            for (int y = box.Y1; y <= box.Y2; y++)
            {
                for (int x = box.X1; x <= box.X2; x++)
                {
                    int i = y * empty.Width + x;
                    if (mask[i])
                        continue;

                    missed++;
                    double lum = 0.2126 * empty.Data[i * 4] + 0.7152 * empty.Data[i * 4 + 1] + 0.0722 * empty.Data[i * 4 + 2];
                    if (lum < 50) dark++;
                    else if (lum > 170) light++;

                    rowMissed[Math.Min(bandRows - 1, (y - box.Y1) * bandRows / box.Height)]++;
                    colMissed[Math.Min(bandRows - 1, (x - box.X1) * bandRows / box.Width)]++;
                }
            }

            int total = box.Width * box.Height;
            Write($"{label}: {N(100.0 * (total - missed) / total)} percent changed; of the {missed} unchanged pixels {N(100.0 * dark / Math.Max(1, missed))} percent were dark (seams), {N(100.0 * light / Math.Max(1, missed))} percent light wood");
            Write($"  unchanged share per tenth, top to bottom: {string.Join(" ", rowMissed.Select(m => N(100.0 * m / (total / bandRows))))}");
            Write($"  unchanged share per tenth, left to right: {string.Join(" ", colMissed.Select(m => N(100.0 * m / (total / bandRows))))}");
        }

        // How many pixels changed in each 20 pixel wide column, so a few stray pixels far off cannot pass for canvas.
        {
            bool[] mask = CanvasMeasure.Changed(empty, filled);
            List<string> columns = [];
            for (int x0 = 0; x0 < empty.Width; x0 += 20)
            {
                int count = 0;
                for (int y = 0; y < empty.Height; y++)
                    for (int x = x0; x < Math.Min(empty.Width, x0 + 20); x++)
                        if (mask[y * empty.Width + x])
                            count++;

                columns.Add($"{region.X1 + x0:0}:{count}");
            }

            Write($"changed pixels per 20 pixel column (screen x): {string.Join(" ", columns)}");
        }

        Explain("default pace", filled);

        // The same fill again, much slower. If the first pace was too fast, more of the canvas changes now.
        await Run(fill, PaintTiming.FromSpeed(10));
        PixelBuffer slow = await Look();
        Explain("after a second, slow fill", slow);
        Write("done");
    }
}
