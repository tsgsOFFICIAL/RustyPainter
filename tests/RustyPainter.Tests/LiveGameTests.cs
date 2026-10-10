using RustyPainter.Imaging;
using RustyPainter.Interface;
using RustyPainter.Painting;
using RustyPainter.Service;

namespace RustyPainter.Tests;

/// <summary>
/// Checks against the real game while it is running. They do nothing unless RUSTYPAINTER_LIVE is set, because
/// some of them send real mouse and keyboard input. RUSTYPAINTER_LIVE=look only reads (the window, the cursor
/// and one pixel); RUSTYPAINTER_LIVE=paint also sends input, and then only while Rust is the window in front.
/// Results are written to the file named by RUSTYPAINTER_LIVE_OUT.
/// </summary>
public class LiveGameTests
{
    private static string Mode => Environment.GetEnvironmentVariable("RUSTYPAINTER_LIVE") ?? "";

    private static void Write(string line)
    {
        if (Environment.GetEnvironmentVariable("RUSTYPAINTER_LIVE_OUT") is { Length: > 0 } path)
            File.AppendAllText(path, line + Environment.NewLine);
    }

    /// <summary>
    /// Paints a small test pattern on the small wooden sign, without clearing it. The painter stops by itself the moment
    /// the mouse is moved or another window comes to the front, and this test gives up when that happens.
    /// </summary>
    [Fact]
    public async Task Paint_a_small_pattern_on_the_small_wooden_sign()
    {
        Assert.SkipUnless(Mode == "paint", "Set RUSTYPAINTER_LIVE=paint to run this. It sends real input to the game.");

        GameWindowService game = new();
        Win32PaintSensors sensors = new(game);

        // Time to switch to the game. Nothing is sent until it is in front.
        for (int i = 0; i < 90 && !sensors.IsGameInFront(); i++)
            await Task.Delay(500, TestContext.Current.CancellationToken);

        Assert.True(sensors.IsGameInFront(), "Rust did not come to the front in time.");
        Write("rust in front, starting in 3 seconds");
        await Task.Delay(3000, TestContext.Current.CancellationToken);

        GameWindow window = game.Find()!;
        SurfaceService surfaces = new();
        Surface sign = surfaces.Catalog.Surfaces.Single(surface => surface.Id == "sign-small");
        ResolvedLayout layout = surfaces.Resolve(sign, window);
        IReadOnlyList<Rgb> swatches = layout.Swatches.ColorGrid!.Colors.Select(Rgb.Parse).ToList();

        (int w, int h) = StudioGeometry.Grid(sign, 8);
        int[] cells = new int[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                cells[y * w + x] = x == 0 || y == 0 || x == w - 1 || y == h - 1 ? 20 : (x + y) % 8 == 0 ? 40 : Quantizer.Unpainted;

        PaintPlan plan = new(w, h, swatches, cells);
        PaintScript script = PaintScriptBuilder.Build(plan, layout, new PaintOptions(ColorMode.Swatches, 8, ClearFirst: false));
        Write($"plan {w}x{h}, {plan.StrokeCount} strokes, {script.Steps.Count} steps");

        PaintEngine engine = new(new Win32PaintInput(), sensors);
        engine.StateChanged += state =>
        {
            Write($"state: {state} {engine.PauseReason}");

            // Anything but running on its own ends this test, so nobody is ever fought for the mouse.
            if (state == PaintState.Paused)
                engine.Abort();
        };

        System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        await engine.RunAsync(script, cancellation: TestContext.Current.CancellationToken);
        Write($"finished with {engine.State} after {clock.Elapsed.TotalSeconds:0.0} s, {engine.Progress.StrokesDone} strokes, step {engine.CurrentStep}. {engine.FailureMessage}");
    }

    /// <summary>
    /// Clears the small wooden sign and paints single dots of known brush sizes and three lines of known spacing, so a
    /// screenshot shows how big a brush unit is on screen. Same safety as the other painting test.
    /// </summary>
    [Fact]
    public async Task Paint_calibration_dots_on_the_small_wooden_sign()
    {
        Assert.SkipUnless(Mode == "calibrate", "Set RUSTYPAINTER_LIVE=calibrate to run this. It clears the sign and sends real input to the game.");

        GameWindowService game = new();
        Win32PaintSensors sensors = new(game);

        for (int i = 0; i < 90 && !sensors.IsGameInFront(); i++)
            await Task.Delay(500, TestContext.Current.CancellationToken);

        Assert.True(sensors.IsGameInFront(), "Rust did not come to the front in time.");
        Write("rust in front, starting in 3 seconds");
        await Task.Delay(3000, TestContext.Current.CancellationToken);

        GameWindow window = game.Find()!;
        SurfaceService surfaces = new();
        Surface sign = surfaces.Catalog.Surfaces.Single(surface => surface.Id == "sign-small");
        ResolvedLayout layout = surfaces.Resolve(sign, window);
        PaintLayout controls = layout.Swatches;
        ScreenRect canvas = layout.Canvas;
        Rgb color = Rgb.Parse(controls.ColorGrid!.Colors[0]);
        Write($"canvas {canvas.X1:0.0},{canvas.Y1:0.0} to {canvas.X2:0.0},{canvas.Y2:0.0}, color {color.Hex}");

        ScreenPoint At(double fx, double fy) => new(canvas.X1 + fx * canvas.Width, canvas.Y1 + fy * canvas.Height);
        Stroke dot = new(0, 0, 1, Vertical: false);

        List<PaintStep> steps =
        [
            new ResetCameraStep(controls.ResetCamera.Center),
            new EnsureColorModeStep(ColorMode.Swatches, controls.ColorModeToggle.Center),
            new ClickControlStep("Brush", controls.ToolBrush.Center),
            new ClickControlStep("Clear", controls.Clear.Center),
            new ClickControlStep("Brush shape", controls.BrushShapes[PaintOptions.SquareShape].Center),
            new SetBrushValueStep("Opacity", controls.Opacity!.Value.Center, "1"),
            new SetBrushValueStep("Spacing", controls.Spacing!.Value.Center, "0.1"),
            new SelectSwatchStep(0, color, controls.ColorGrid!.CellCenter(0))
        ];

        // One dot for each brush size, left to right.
        (int size, double x)[] dots = [(2, 0.08), (4, 0.18), (8, 0.30), (16, 0.46), (32, 0.72)];
        foreach ((int size, double x) in dots)
        {
            ScreenPoint point = At(x, 0.30);
            steps.Add(new SetBrushValueStep("Size", controls.Size!.Value.Center, size.ToString()));
            steps.Add(new PaintStrokeStep(dot, point, point));
            Write($"dot size {size} at {point.X:0.0},{point.Y:0.0}");
        }

        // Three lines with the same size and different spacing.
        steps.Add(new SetBrushValueStep("Size", controls.Size!.Value.Center, "8"));
        foreach ((string spacing, double y) in new[] { ("1", 0.62), ("0.5", 0.74), ("0.1", 0.86) })
        {
            ScreenPoint from = At(0.08, y), to = At(0.60, y);
            steps.Add(new SetBrushValueStep("Spacing", controls.Spacing!.Value.Center, spacing));
            steps.Add(new PaintStrokeStep(new Stroke(0, 0, 2, Vertical: false), from, to));
            Write($"line spacing {spacing} from {from.X:0.0},{from.Y:0.0} to {to.X:0.0},{to.Y:0.0}");
        }

        PaintEngine engine = new(new Win32PaintInput(), sensors);
        engine.StateChanged += state =>
        {
            Write($"state: {state} {engine.PauseReason}");
            if (state == PaintState.Paused)
                engine.Abort();
        };

        await engine.RunAsync(new PaintScript(steps), cancellation: TestContext.Current.CancellationToken);
        Write($"finished with {engine.State}, step {engine.CurrentStep} of {steps.Count}. {engine.FailureMessage}");
    }

    [Fact]
    public void Look_at_the_game_without_touching_it()
    {
        Assert.SkipUnless(Mode is "look" or "paint", "Set RUSTYPAINTER_LIVE=look to run this.");

        GameWindowService game = new();
        GameWindow? window = game.Find();
        Write($"window: {(window is null ? "not found" : $"{window.Width}x{window.Height} at {window.Client.X1},{window.Client.Y1}, minimized {window.IsMinimized}, in front {window.IsForeground}")}");

        Win32PaintSensors sensors = new(game);
        Write($"in front: {sensors.IsGameInFront()}");
        Write($"cursor: {sensors.CursorPosition.X},{sensors.CursorPosition.Y}");

        if (window is null)
            return;

        SurfaceService surfaces = new();
        Surface sign = surfaces.Catalog.Surfaces.Single(surface => surface.Id == "sign-small");
        ResolvedLayout layout = surfaces.Resolve(sign, window);
        ScreenPoint toggle = layout.Swatches.ColorModeToggle.Center;
        ScreenPoint freeToggle = layout.Free.ColorModeToggle.Center;

        Write($"swatch toggle at {toggle.X:0},{toggle.Y:0}: {sensors.ReadColor(toggle)?.Hex ?? "unreadable"}");
        Write($"free toggle at {freeToggle.X:0},{freeToggle.Y:0}: {sensors.ReadColor(freeToggle)?.Hex ?? "unreadable"}");
        Write($"canvas {layout.Canvas.X1:0},{layout.Canvas.Y1:0} to {layout.Canvas.X2:0},{layout.Canvas.Y2:0}");
    }
}
