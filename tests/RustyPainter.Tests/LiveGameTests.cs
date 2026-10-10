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
