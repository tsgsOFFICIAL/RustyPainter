using Avalonia.Headless.XUnit;
using System.Globalization;
using RustyPainter.Imaging;
using RustyPainter.Interface;
using RustyPainter.Painting;
using RustyPainter.Service;

namespace RustyPainter.Tests;

/// <summary>
/// Checks the carvable pumpkin in the game, with its carve screen open: how many screen pixels one brush unit is at the middle of
/// it, and how far the carving reaches. It clears the carving and cuts into the pumpkin, so it needs RUSTYPAINTER_LIVE=pumpkin.
/// Results go to RUSTYPAINTER_LIVE_OUT.
/// </summary>
public class LivePumpkinTests
{
    private static string Mode => Environment.GetEnvironmentVariable("RUSTYPAINTER_LIVE") ?? "";

    private static void Write(string line)
    {
        if (Environment.GetEnvironmentVariable("RUSTYPAINTER_LIVE_OUT") is { Length: > 0 } path)
            File.AppendAllText(path, line + Environment.NewLine);
    }

    private static string N(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Measure_the_pumpkin()
    {
        Assert.SkipUnless(Mode == "pumpkin", "Set RUSTYPAINTER_LIVE=pumpkin to run this. It clears the carving and sends real input to the game.");

        CancellationToken stop = TestContext.Current.CancellationToken;
        GameWindowService game = new();
        Win32PaintSensors sensors = new(game);
        Win32ScreenCapture capture = new();

        for (int i = 0; i < 90 && !sensors.IsGameInFront(); i++)
            await Task.Delay(500, stop);

        Assert.True(sensors.IsGameInFront(), "Rust did not come to the front in time.");
        Write("measuring the pumpkin; rust in front, starting in 3 seconds");
        await Task.Delay(3000, stop);

        GameWindow window = game.Find()!;
        SurfaceService surfaces = new();
        Surface pumpkin = surfaces.Catalog.Surfaces.Single(s => s.Id == "pumpkin");
        ResolvedLayout layout = surfaces.Resolve(pumpkin, window);
        PaintLayout controls = layout.Swatches;
        ScreenRect canvas = layout.Canvas;
        ScreenRect region = new(Math.Max(window.Client.X1, canvas.X1 - 150), Math.Max(window.Client.Y1, canvas.Y1 - 150),
            Math.Min(window.Client.X2, canvas.X2 + 150), Math.Min(window.Client.Y2, canvas.Y2 + 150));
        Rgb white = Rgb.Parse(controls.ColorGrid!.Colors[0]);

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
            await Task.Delay(700, stop);
            Assert.True(sensors.IsGameInFront(), "Rust is no longer in front, so nothing was copied.");
            return capture.Capture(region) ?? throw new InvalidOperationException("The pumpkin could not be copied.");
        }

        ScreenPoint At(double fx, double fy) => new(canvas.X1 + fx * canvas.Width, canvas.Y1 + fy * canvas.Height);
        Stroke dot = new(0, 0, 1, Vertical: false);

        await Run(
        [
            new ResetCameraStep(controls.ResetCamera.Center),
            new ClickControlStep("Brush", controls.ToolBrush.Center),
            new ClickControlStep("Clear", controls.Clear.Center),
            new ClickControlStep("Brush shape", controls.BrushShapes[PaintOptions.CarvingShape].Center),
            new SelectSwatchStep(0, white, controls.ColorGrid!.CellCenter(0))
        ]);

        PixelBuffer empty = await Look();

        (int size, double position)[] dots = [(10, 0.25), (20, 0.5), (40, 0.75)];
        List<PaintStep> dotSteps = [];
        foreach ((int size, double position) in dots)
        {
            ScreenPoint point = At(position, 0.5);
            dotSteps.Add(new SetBrushValueStep("Size", controls.Size!.Value.Center, size.ToString(CultureInfo.InvariantCulture)));
            dotSteps.Add(new PaintStrokeStep(dot, point, point));
            Write($"dot size {size} at {point.X:0.0},{point.Y:0.0}");
        }

        await Run(dotSteps);
        PixelBuffer withDots = await Look();
        bool[] changedByDots = CanvasMeasure.Changed(empty, withDots, 40);
        Write($"separate pieces found: {CanvasMeasure.Blobs(changedByDots, empty.Width, minimum: 12).Count}");

        for (int i = 0; i < dots.Length; i++)
        {
            ScreenPoint center = At(dots[i].position, 0.5);
            PixelBox? box = CanvasMeasure.BoundsAround(changedByDots, empty.Width, center.X - region.X1, center.Y - region.Y1, dots[i].size * 3.5);
            if (box is null)
            {
                Write($"size {dots[i].size}: nothing changed");
                continue;
            }

            (double x, double y) = CanvasMeasure.PixelsPerUnit(box.Value, dots[i].size);
            Write($"size {dots[i].size}: {box.Value.Width} x {box.Value.Height} px, {N(x)} x {N(y)} px per unit");
        }

        // A sweep of the biggest brush over everything the pumpkin shows, to see how far the carving reaches.
        List<PaintStep> sweep = [new SetBrushValueStep("Size", controls.Size!.Value.Center, "100")];
        for (double y = canvas.Y1 - 60; y <= canvas.Y2 + 60; y += 70)
            sweep.Add(new PaintStrokeStep(new Stroke(0, 0, 2, Vertical: false), new ScreenPoint(canvas.X1 - 60, y), new ScreenPoint(canvas.X2 + 60, y)));

        await Run(sweep);
        PixelBuffer swept = await Look();
        bool[] changed = CanvasMeasure.Changed(empty, swept, 40);
        PixelBox? area = CanvasMeasure.SolidBounds(changed, empty.Width, share: 0.15);
        if (area is { } found)
            Write($"carved area on screen: {region.X1 + found.X1:0},{region.Y1 + found.Y1:0} to {region.X1 + found.X2 + 1:0},{region.Y1 + found.Y2 + 1:0} ({found.Width} x {found.Height}), canvas was {canvas.X1:0},{canvas.Y1:0} to {canvas.X2:0},{canvas.Y2:0}");
        else
            Write("the sweep changed nothing");

        Write("done");
    }

    /// <summary>Copies only the pumpkin region of the screen, magnified, into the folder named by RUSTYPAINTER_SHOTS. Reads only.</summary>
    [AvaloniaFact]
    public async Task Save_a_look_at_the_pumpkin()
    {
        Assert.SkipUnless(Mode == "snap", "Set RUSTYPAINTER_LIVE=snap to run this. It only reads the screen.");

        GameWindowService game = new();
        Win32PaintSensors sensors = new(game);
        for (int i = 0; i < 90 && !sensors.IsGameInFront(); i++)
            await Task.Delay(500, TestContext.Current.CancellationToken);

        Assert.True(sensors.IsGameInFront(), "Rust did not come to the front in time.");
        await Task.Delay(1000, TestContext.Current.CancellationToken);

        GameWindow window = game.Find()!;
        double sx = window.Width / 2560.0, sy = window.Height / 1440.0;
        PixelBuffer shot = new Win32ScreenCapture().Capture(new ScreenRect(window.Client.X1 + 700 * sx, window.Client.Y1 + 480 * sy, window.Client.X1 + 1280 * sx, window.Client.Y1 + 1000 * sy))!;
        const int zoom = 2;
        PixelBuffer big = new(shot.Width * zoom, shot.Height * zoom);
        for (int y = 0; y < big.Height; y++)
            for (int x = 0; x < big.Width; x++)
                Array.Copy(shot.Data, ((y / zoom) * shot.Width + x / zoom) * 4, big.Data, (y * big.Width + x) * 4, 4);

        TestImages.SavePng(big, Path.Combine(Environment.GetEnvironmentVariable("RUSTYPAINTER_SHOTS")!, "pumpkin.png"));
    }

    /// <summary>Cuts single dots of the sizes that painting uses, far apart, and measures what each one leaves. Clears the carving.</summary>
    [Fact]
    public async Task Measure_the_pumpkin_stamps()
    {
        Assert.SkipUnless(Mode == "stamps", "Set RUSTYPAINTER_LIVE=stamps to run this. It clears the carving and sends real input to the game.");

        CancellationToken stop = TestContext.Current.CancellationToken;
        GameWindowService game = new();
        Win32PaintSensors sensors = new(game);
        Win32ScreenCapture capture = new();

        for (int i = 0; i < 90 && !sensors.IsGameInFront(); i++)
            await Task.Delay(500, stop);

        Assert.True(sensors.IsGameInFront(), "Rust did not come to the front in time.");
        await Task.Delay(3000, stop);

        GameWindow window = game.Find()!;
        SurfaceService surfaces = new();
        ResolvedLayout layout = surfaces.Resolve(surfaces.Catalog.Surfaces.Single(s => s.Id == "pumpkin"), window);
        PaintLayout controls = layout.Swatches;
        ScreenRect canvas = layout.Canvas;
        ScreenRect region = new(canvas.X1 - 100, canvas.Y1 - 100, canvas.X2 + 100, canvas.Y2 + 100);
        Rgb white = Rgb.Parse(controls.ColorGrid!.Colors[0]);

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
            await Task.Delay(700, stop);
            Assert.True(sensors.IsGameInFront(), "Rust is no longer in front, so nothing was copied.");
            return capture.Capture(region) ?? throw new InvalidOperationException("The pumpkin could not be copied.");
        }

        await Run(
        [
            new ResetCameraStep(controls.ResetCamera.Center),
            new ClickControlStep("Brush", controls.ToolBrush.Center),
            new ClickControlStep("Clear", controls.Clear.Center),
            new ClickControlStep("Brush shape", controls.BrushShapes[PaintOptions.CarvingShape].Center),
            new SelectSwatchStep(0, white, controls.ColorGrid!.CellCenter(0))
        ]);

        int[] sizes = [2, 4, 8, 16, 32];
        double centerX = canvas.Center.X, centerY = canvas.Center.Y;
        Stroke dot = new(0, 0, 1, Vertical: false);

        foreach ((string direction, bool across) in new[] { ("in a row", true), ("in a column", false) })
        {
            PixelBuffer before = await Look();
            List<PaintStep> steps = [];
            List<(int size, ScreenPoint at)> placed = [];
            for (int i = 0; i < sizes.Length; i++)
            {
                double offset = (i - 2) * (across ? 62 : 38);
                ScreenPoint at = across ? new(centerX + offset, centerY) : new(centerX, centerY + offset);
                steps.Add(new SetBrushValueStep("Size", controls.Size!.Value.Center, sizes[i].ToString(CultureInfo.InvariantCulture)));
                steps.Add(new PaintStrokeStep(dot, at, at));
                placed.Add((sizes[i], at));
            }

            await Run(steps);
            PixelBuffer after = await Look();
            bool[] changed = CanvasMeasure.Changed(before, after, 40);

            foreach ((int size, ScreenPoint at) in placed)
            {
                PixelBox? box = CanvasMeasure.BoundsAround(changed, before.Width, at.X - region.X1, at.Y - region.Y1, 28);
                Write(box is null
                    ? $"{direction}: size {size} at {at.X:0},{at.Y:0}: nothing changed"
                    : $"{direction}: size {size} at {at.X:0},{at.Y:0}: {box.Value.Width} x {box.Value.Height} px, {N(box.Value.Width / (double)size)} x {N(box.Value.Height / (double)size)} px per unit");
            }

            await Run([new ClickControlStep("Clear", controls.Clear.Center)]);
        }

        Write("done");
    }
}
