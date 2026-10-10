using RustyPainter.Imaging;
using RustyPainter.Interface;
using RustyPainter.Painting;
using RustyPainter.Service;

namespace RustyPainter.Tests;

/// <summary>
/// Checks how the paint screen of an animated sign behaves, with the sign open in the game: whether clicking a frame number
/// opens it, whether each frame holds its own picture, and whether clearing wipes one frame or all of them. It paints two big
/// dots and clears frames, so it needs RUSTYPAINTER_LIVE=frames. Results go to RUSTYPAINTER_LIVE_OUT.
/// </summary>
public class LiveAnimatedTests
{
    private static string Mode => Environment.GetEnvironmentVariable("RUSTYPAINTER_LIVE") ?? "";

    private static void Write(string line)
    {
        if (Environment.GetEnvironmentVariable("RUSTYPAINTER_LIVE_OUT") is { Length: > 0 } path)
            File.AppendAllText(path, line + Environment.NewLine);
    }

    [Fact]
    public async Task How_the_frames_of_an_animated_sign_behave()
    {
        Assert.SkipUnless(Mode == "frames", "Set RUSTYPAINTER_LIVE=frames to run this. It clears frames and sends real input to the game.");

        string id = Environment.GetEnvironmentVariable("RUSTYPAINTER_SURFACE") ?? "animated-neon-large";
        CancellationToken stop = TestContext.Current.CancellationToken;
        GameWindowService game = new();
        Win32PaintSensors sensors = new(game);
        Win32ScreenCapture capture = new();

        for (int i = 0; i < 90 && !sensors.IsGameInFront(); i++)
            await Task.Delay(500, stop);

        Assert.True(sensors.IsGameInFront(), "Rust did not come to the front in time.");
        Write($"frames of {id}; rust in front, starting in 3 seconds");
        await Task.Delay(3000, stop);

        GameWindow window = game.Find()!;
        SurfaceService surfaces = new();
        Surface surface = surfaces.Catalog.Surfaces.Single(s => s.Id == id);
        ResolvedLayout layout = surfaces.Resolve(surface, window);
        PaintLayout controls = layout.Swatches;
        FrameControls frames = controls.Frames!;
        ScreenRect canvas = layout.Canvas;
        ScreenRect region = new(canvas.X1 - 20, canvas.Y1 - 20, canvas.X2 + 20, canvas.Y2 + 20);
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

        string Lit()
        {
            List<int> lit = [];
            for (int i = 0; i < frames.Buttons.Length; i++)
            {
                ScreenRect button = frames.Buttons[i];
                if (sensors.ReadColor(new ScreenPoint(button.Center.X, button.Y1 + 0.18 * button.Height)) is { } px && px.G - Math.Max(px.R, px.B) >= 25)
                    lit.Add(i + 1);
            }

            return lit.Count == 0 ? "none" : string.Join("+", lit);
        }

        async Task<PixelBuffer> Look()
        {
            await Task.Delay(700, stop);
            Assert.True(sensors.IsGameInFront(), "Rust is no longer in front, so nothing was copied.");
            return capture.Capture(region) ?? throw new InvalidOperationException("The canvas could not be copied.");
        }

        ScreenPoint At(double fx, double fy) => new(canvas.X1 + fx * canvas.Width, canvas.Y1 + fy * canvas.Height);

        PaintStep Frame(int number) => new SelectFrameStep(number - 1, frames.Buttons[number - 1].Center,
            new ScreenPoint(frames.Buttons[number - 1].Center.X, frames.Buttons[number - 1].Y1 + 0.18 * frames.Buttons[number - 1].Height));

        List<PaintStep> Setup() =>
        [
            new ResetCameraStep(controls.ResetCamera.Center),
            new EnsureColorModeStep(ColorMode.Swatches, layout.Swatches.ColorModeToggle.Center, layout.Free.ColorModeToggle.Center),
            new ClickControlStep("Brush", controls.ToolBrush.Center)
        ];

        List<PaintStep> Dot(double fx) =>
        [
            new ClickControlStep("Brush", controls.ToolBrush.Center),
            new ClickControlStep("Brush shape", controls.BrushShapes[3].Center),
            new SelectSwatchStep(0, white, controls.ColorGrid!.CellCenter(0)),
            new PaintStrokeStep(new Stroke(0, 0, 1, Vertical: false), At(fx, 0.5), At(fx, 0.5))
        ];

        string Count(PixelBuffer baseline, PixelBuffer now)
        {
            bool[] changed = CanvasMeasure.Changed(baseline, now, 60);
            int left = 0, right = 0;
            for (int y = 0; y < baseline.Height; y++)
            {
                for (int x = 0; x < baseline.Width; x++)
                {
                    if (!changed[y * baseline.Width + x])
                        continue;

                    if (x < baseline.Width / 2) left++;
                    else right++;
                }
            }

            return $"changed pixels: left half {left}, right half {right}";
        }

        // Start from blank frames 1 to 3, clearing each while it is the open one.
        List<PaintStep> blank = Setup();
        foreach (int number in new[] { 1, 2, 3 })
        {
            blank.Add(Frame(number));
            blank.Add(new ClickControlStep("Clear", controls.Clear.Center));
        }

        blank.Add(Frame(1));
        await Run(blank);
        Write($"after clearing frames 1 to 3 and opening 1: lit {Lit()}");
        PixelBuffer baseline = await Look();

        // A dot on the left of frame 1, then a dot on the right of frame 2.
        await Run([Frame(1), .. Dot(0.30)]);
        PixelBuffer frame1 = await Look();
        Write($"frame 1 with its dot (lit {Lit()}): {Count(baseline, frame1)}");

        await Run([Frame(2)]);
        Write($"opened frame 2: lit {Lit()}");
        PixelBuffer frame2Before = await Look();
        Write($"frame 2 before painting: {Count(baseline, frame2Before)}   (a dot from frame 1 showing here would mean frames share a picture)");

        await Run(Dot(0.70));
        PixelBuffer frame2 = await Look();
        Write($"frame 2 with its dot: {Count(baseline, frame2)}");

        // Does clearing frame 2 touch frame 1?
        await Run([new ClickControlStep("Clear", controls.Clear.Center)]);
        PixelBuffer frame2Cleared = await Look();
        Write($"frame 2 after Clear: {Count(baseline, frame2Cleared)}");

        await Run([Frame(1)]);
        PixelBuffer frame1Again = await Look();
        Write($"frame 1 opened again (lit {Lit()}): {Count(baseline, frame1Again)}   (its dot still there means Clear only clears the open frame)");

        await Run([Frame(3)]);
        Write($"frame 3 opened (lit {Lit()})");
        await Run([Frame(1)]);
        Write($"frame 1 opened once more (lit {Lit()})");

        // Leave the frames as clean as they were found.
        List<PaintStep> finish = [];
        foreach (int number in new[] { 1, 2, 3 })
        {
            finish.Add(Frame(number));
            finish.Add(new ClickControlStep("Clear", controls.Clear.Center));
        }

        finish.Add(Frame(1));
        await Run(finish);
        Write("done, frames 1 to 3 cleared");
    }
}
