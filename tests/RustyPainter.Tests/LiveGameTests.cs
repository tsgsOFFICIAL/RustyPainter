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
            new EnsureColorModeStep(ColorMode.Swatches, layout.Swatches.ColorModeToggle.Center, layout.Free.ColorModeToggle.Center),
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

    /// <summary>
    /// Runs the real setup steps for any-color mode on the portrait frame and logs, every 100 ms, which step is running and
    /// which brush shape is lit, so it shows when a click is lost. Start with the game in swatch mode to include the switch.
    /// </summary>
    [Fact]
    public async Task Trace_the_setup_in_any_color_mode()
    {
        Assert.SkipUnless(Mode == "trace", "Set RUSTYPAINTER_LIVE=trace to run this. It sends real input to the game.");

        GameWindowService game = new();
        Win32PaintSensors sensors = new(game);

        for (int i = 0; i < 90 && !sensors.IsGameInFront(); i++)
            await Task.Delay(500, TestContext.Current.CancellationToken);

        Assert.True(sensors.IsGameInFront(), "Rust did not come to the front in time.");
        Write("rust in front, starting in 3 seconds");
        await Task.Delay(3000, TestContext.Current.CancellationToken);

        GameWindow window = game.Find()!;
        SurfaceService surfaces = new();
        ResolvedLayout layout = surfaces.Resolve(surfaces.Catalog.Surfaces.Single(surface => surface.Id == "frame-portrait"), window);
        PaintPlan plan = new(1, 1, [Rgb.Parse("#FF0000")], [0]);
        PaintScript script = PaintScriptBuilder.Build(plan, layout, new PaintOptions(ColorMode.Free, 8, ClearFirst: false));
        List<PaintStep> setup = script.Steps.TakeWhile(step => step is not PaintStrokeStep).ToList();
        Write($"{setup.Count} setup steps: {string.Join(", ", setup.Select(step => step.GetType().Name))}");

        PaintEngine engine = new(new Win32PaintInput(), sensors);
        engine.StateChanged += state =>
        {
            Write($"state: {state} {engine.PauseReason}");
            if (state == PaintState.Paused)
                engine.Abort();
        };

        string Lit(PaintLayout controls)
        {
            List<int> lit = [];
            for (int i = 0; i < controls.BrushShapes.Length; i++)
            {
                ScreenPoint c = controls.BrushShapes[i].Center;
                if (sensors.ReadColor(new ScreenPoint(c.X, c.Y - 18)) is { } px && px.G - Math.Max(px.R, px.B) >= 25)
                    lit.Add(i);
            }

            return lit.Count == 0 ? "none" : string.Join("+", lit);
        }

        System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        using CancellationTokenSource stop = new();
        Task poller = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                Write($"{clock.ElapsedMilliseconds,6} ms step {engine.CurrentStep} lit(free row) {Lit(layout.Free)} lit(swatch row) {Lit(layout.Swatches)} toggle(free) {sensors.ReadColor(layout.Free.ColorModeToggle.Center)?.Hex} toggle(swatch) {sensors.ReadColor(layout.Swatches.ColorModeToggle.Center)?.Hex}");
                await Task.Delay(100);
            }
        });

        await engine.RunAsync(new PaintScript(setup), cancellation: TestContext.Current.CancellationToken);
        await Task.Delay(1500, TestContext.Current.CancellationToken);
        Write($"after 1.5 s: lit(free row) {Lit(layout.Free)}");
        stop.Cancel();
        await poller;
        Write($"finished with {engine.State}. {engine.FailureMessage}");
    }

    /// <summary>Reads pixels across both places the color mode toggle can be, to find where its fill shows. Reads only.</summary>
    [Fact]
    public void Read_the_color_mode_toggle()
    {
        Assert.SkipUnless(Mode is "look" or "paint", "Set RUSTYPAINTER_LIVE=look to run this.");

        GameWindowService game = new();
        GameWindow window = game.Find()!;
        Win32PaintSensors sensors = new(game);
        ResolvedLayout layout = new SurfaceService().Resolve(new SurfaceService().Catalog.Surfaces.Single(surface => surface.Id == "frame-portrait"), window);

        foreach ((string name, ScreenRect box) in new[] { ("swatch position", layout.Swatches.ColorModeToggle), ("free position", layout.Free.ColorModeToggle) })
        {
            Write($"{name}: {box.X1:0},{box.Y1:0} to {box.X2:0},{box.Y2:0}");
            foreach (double fy in new[] { 0.12, 0.3, 0.5, 0.7, 0.88 })
            {
                string row = string.Join(" ", new[] { 0.12, 0.3, 0.5, 0.7, 0.88 }.Select(fx => sensors.ReadColor(new ScreenPoint(box.X1 + fx * box.Width, box.Y1 + fy * box.Height))?.Hex ?? "unreadable"));
                Write($"  y {fy:0.00}: {row}");
            }
        }
    }

    /// <summary>Reads the row of brush shapes and prints a few pixels of each, to tell the selected one from the others. Reads only.</summary>
    [Fact]
    public void Read_the_brush_shape_row()
    {
        Assert.SkipUnless(Mode is "look" or "paint", "Set RUSTYPAINTER_LIVE=look to run this.");

        GameWindowService game = new();
        GameWindow window = game.Find()!;
        Win32PaintSensors sensors = new(game);
        ResolvedLayout layout = new SurfaceService().Resolve(new SurfaceService().Catalog.Surfaces.Single(surface => surface.Id == "frame-portrait"), window);

        foreach ((string name, PaintLayout controls) in new[] { ("swatch layout", layout.Swatches), ("free layout", layout.Free) })
        {
            Write(name);
            for (int i = 0; i < controls.BrushShapes.Length; i++)
            {
                ScreenPoint c = controls.BrushShapes[i].Center;
                string Px(double dx, double dy) => sensors.ReadColor(new ScreenPoint(c.X + dx, c.Y + dy))?.Hex ?? "unreadable";
                Write($"  shape {i} at {c.X:0},{c.Y:0}: left {Px(-18, 0)} left2 {Px(-14, 0)} top {Px(0, -18)} edge {Px(-11, -11)} center {Px(0, 0)}");
            }
        }
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
