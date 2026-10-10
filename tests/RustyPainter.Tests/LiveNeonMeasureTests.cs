using System.Globalization;
using RustyPainter.Imaging;
using RustyPainter.Interface;
using RustyPainter.Painting;
using RustyPainter.Service;

namespace RustyPainter.Tests;

/// <summary>
/// Measures a neon sign in the running game. Neon signs have no brush size to type, only a few fixed brush presets, so each
/// preset paints one dot and its footprint on screen is measured, and the biggest preset then fills the canvas to show where
/// it ends. Only the canvas rectangle of the screen is copied, only while Rust is in front, and nothing is kept.
/// It does nothing unless RUSTYPAINTER_LIVE=neon; RUSTYPAINTER_SURFACE names the sign and RUSTYPAINTER_LIVE_OUT the results file.
/// It clears the canvas of the sign.
/// </summary>
public class LiveNeonMeasureTests
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
    public async Task Measure_the_neon_sign_that_is_open_in_the_game()
    {
        Assert.SkipUnless(Mode == "neon", "Set RUSTYPAINTER_LIVE=neon to run this. It clears the canvas and sends real input to the game.");

        string id = Environment.GetEnvironmentVariable("RUSTYPAINTER_SURFACE") ?? "neon-small";
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
        ScreenRect canvas = layout.Canvas;
        IReadOnlyList<Rgb> swatches = controls.ColorGrid!.Colors.Select(Rgb.Parse).ToList();

        const int margin = 60;
        ScreenRect region = new(
            Math.Max(window.Client.X1, canvas.X1 - margin), Math.Max(window.Client.Y1, canvas.Y1 - margin),
            Math.Min(window.Client.X2, canvas.X2 + margin), Math.Min(window.Client.Y2, canvas.Y2 + margin));

        async Task Run(List<PaintStep> steps)
        {
            PaintEngine engine = new(new Win32PaintInput(), sensors);
            engine.StateChanged += state =>
            {
                if (state == PaintState.Paused)
                    engine.Abort();
            };

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

        List<PaintStep> setup = [new ResetCameraStep(controls.ResetCamera.Center)];
        if (surface.ScrollOut > 0)
            setup.Add(new ScrollOutStep(surface.ScrollOut));

        setup.AddRange(
        [
            new EnsureColorModeStep(ColorMode.Swatches, layout.Swatches.ColorModeToggle.Center, layout.Free.ColorModeToggle.Center),
            new ClickControlStep("Brush", controls.ToolBrush.Center),
            new ClickControlStep("Clear", controls.Clear.Center)
        ]);

        await Run(setup);
        PixelBuffer empty = await Look();

        double average = 0;
        for (int i = 0; i < empty.Width * empty.Height; i++)
            average += 0.2126 * empty.Data[i * 4] + 0.7152 * empty.Data[i * 4 + 1] + 0.0722 * empty.Data[i * 4 + 2];

        average /= empty.Width * empty.Height;
        int swatch = average > 110
            ? Enumerable.Range(0, swatches.Count).OrderBy(i => Luminance(swatches[i])).First()
            : Enumerable.Range(0, swatches.Count).OrderByDescending(i => Luminance(swatches[i])).First();
        Write($"canvas average brightness {N(average)}, painting with swatch {swatch} {swatches[swatch].Hex}; {controls.BrushShapes.Length} presets");

        // One dot for each preset, spread along the longer side of the canvas.
        bool wide = canvas.Width >= canvas.Height;
        double[] positions = controls.BrushShapes.Length == 4 ? [0.12, 0.32, 0.56, 0.84] : Enumerable.Range(0, controls.BrushShapes.Length).Select(i => 0.12 + 0.72 * i / Math.Max(1, controls.BrushShapes.Length - 1)).ToArray();

        List<PaintStep> dotSteps = [new SelectSwatchStep(swatch, swatches[swatch], controls.ColorGrid!.CellCenter(swatch))];
        for (int k = 0; k < controls.BrushShapes.Length; k++)
        {
            ScreenPoint point = wide ? At(positions[k], 0.5) : At(0.5, positions[k]);
            dotSteps.Add(new ClickControlStep("Brush shape", controls.BrushShapes[k].Center));
            dotSteps.Add(new PaintStrokeStep(dot, point, point));
        }

        await Run(dotSteps);
        PixelBuffer withDots = await Look();
        bool[] changedByDots = CanvasMeasure.Changed(empty, withDots);

        List<(double X, double Y)> footprints = [];
        for (int k = 0; k < controls.BrushShapes.Length; k++)
        {
            ScreenPoint center = wide ? At(positions[k], 0.5) : At(0.5, positions[k]);
            double nearest = double.MaxValue;
            for (int j = 0; j < positions.Length; j++)
            {
                if (j == k)
                    continue;

                ScreenPoint other = wide ? At(positions[j], 0.5) : At(0.5, positions[j]);
                nearest = Math.Min(nearest, Math.Abs(wide ? other.X - center.X : other.Y - center.Y));
            }

            PixelBox? box = CanvasMeasure.BoundsAround(changedByDots, empty.Width, center.X - region.X1, center.Y - region.Y1, nearest * 0.55);
            Assert.True(box is not null, $"Preset {k} left nothing to measure.");
            footprints.Add((box.Value.Width, box.Value.Height));
            Write($"preset {k}: {box.Value.Width} x {box.Value.Height} px");
        }

        // The biggest preset fills the canvas.
        int biggest = Enumerable.Range(0, footprints.Count).OrderByDescending(k => footprints[k].Y).First();
        double step = 0.8 * footprints[biggest].Y;
        List<PaintStep> fill = [new ClickControlStep("Brush shape", controls.BrushShapes[biggest].Center)];
        for (double y = canvas.Y1 + 2; ; y += step)
        {
            double row = Math.Min(y, canvas.Y2 - 2);
            fill.Add(new PaintStrokeStep(new Stroke(0, 0, 2, Vertical: false), new ScreenPoint(canvas.X1 + 2, row), new ScreenPoint(canvas.X2 - 2, row)));

            if (row >= canvas.Y2 - 2)
                break;
        }

        await Run(fill);
        PixelBuffer filled = await Look();

        bool[] filledMask = CanvasMeasure.Changed(empty, filled);
        PixelBox? area = CanvasMeasure.SolidBounds(filledMask, empty.Width);
        Assert.NotNull(area);

        double left = region.X1 + area.Value.X1, top = region.Y1 + area.Value.Y1;
        double right = region.X1 + area.Value.X2 + 1, bottom = region.Y1 + area.Value.Y2 + 1;
        double coverage = (double)area.Value.Pixels / (area.Value.Width * area.Value.Height);
        Write($"filled with preset {biggest}");
        Write($"recorded canvas {canvas.X1:0},{canvas.Y1:0} to {canvas.X2:0},{canvas.Y2:0} ({canvas.Width:0} x {canvas.Height:0})");
        Write($"measured canvas {left:0},{top:0} to {right:0},{bottom:0} ({right - left:0} x {bottom - top:0}), {N(coverage * 100)} percent filled");

        List<string> columns = [];
        for (int x0 = 0; x0 < empty.Width; x0 += 20)
        {
            int count = 0;
            for (int y = 0; y < empty.Height; y++)
                for (int x = x0; x < Math.Min(empty.Width, x0 + 20); x++)
                    if (filledMask[y * empty.Width + x])
                        count++;

            columns.Add($"{region.X1 + x0:0}:{count}");
        }

        Write($"changed pixels per 20 pixel column (screen x): {string.Join(" ", columns)}");
        Write("done");
    }
}
