using Avalonia.Headless.XUnit;
using System.Globalization;
using RustyPainter.Imaging;
using RustyPainter.Interface;
using RustyPainter.Painting;
using RustyPainter.Service;

namespace RustyPainter.Tests;

/// <summary>
/// Tries the Carve and Add tools of the stone sculpture in the game, with its sculpting screen open and the block seen from the
/// front, and reports how thick and how deep the strokes are. It cuts into the stone and then clears it, so it needs
/// RUSTYPAINTER_LIVE=sculpture. Results go to RUSTYPAINTER_LIVE_OUT, a picture to RUSTYPAINTER_SHOTS.
/// </summary>
public class LiveSculptureTests
{
    private static string Mode => Environment.GetEnvironmentVariable("RUSTYPAINTER_LIVE") ?? "";

    private static void Write(string line)
    {
        if (Environment.GetEnvironmentVariable("RUSTYPAINTER_LIVE_OUT") is { Length: > 0 } path)
            File.AppendAllText(path, line + Environment.NewLine);
    }

    private static double Luminance(PixelBuffer buffer, int i)
        => 0.2126 * buffer.Data[i * 4] + 0.7152 * buffer.Data[i * 4 + 1] + 0.0722 * buffer.Data[i * 4 + 2];

    [AvaloniaFact]
    public async Task Try_the_carve_and_add_tools()
    {
        Assert.SkipUnless(Mode == "sculpture", "Set RUSTYPAINTER_LIVE=sculpture to run this. It cuts into the stone and sends real input to the game.");

        CancellationToken stop = TestContext.Current.CancellationToken;
        GameWindowService game = new();
        Win32PaintSensors sensors = new(game);
        Win32ScreenCapture capture = new();

        for (int i = 0; i < 90 && !sensors.IsGameInFront(); i++)
            await Task.Delay(500, stop);

        Assert.True(sensors.IsGameInFront(), "Rust did not come to the front in time.");
        Write("rust in front, starting in 3 seconds");
        await Task.Delay(3000, stop);

        GameWindow window = game.Find()!;
        double sx = window.Width / 2560.0, sy = window.Height / 1440.0;
        ScreenPoint At(double x, double y) => new(window.Client.X1 + x * sx, window.Client.Y1 + y * sy);
        ScreenRect region = new(window.Client.X1 + 780 * sx, window.Client.Y1 + 280 * sy, window.Client.X1 + 1550 * sx, window.Client.Y1 + 1170 * sy);

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

        int shots = 0;
        async Task<PixelBuffer> Look()
        {
            await Task.Delay(800, stop);
            Assert.True(sensors.IsGameInFront(), "Rust is no longer in front, so nothing was copied.");
            PixelBuffer taken = capture.Capture(region) ?? throw new InvalidOperationException("The block could not be copied.");
            if (Environment.GetEnvironmentVariable("RUSTYPAINTER_SHOTS") is { Length: > 0 } folder)
                TestImages.SavePng(taken, Path.Combine(folder, $"sculpture-{++shots}.png"));

            return taken;
        }

        // The tools sit in one row, the brushes in another, and the sliders are numbers that can be clicked and typed into.
        ScreenPoint carve = At(2112, 507), add = At(2176, 507), cube = At(2176, 686), size = At(2488, 754), hardness = At(2488, 810);
        Stroke line = new(0, 0, 2, Vertical: false);

        string Describe(string label, PixelBuffer before, PixelBuffer after, double lineY)
        {
            int width = before.Width, height = before.Height;
            int row = (int)((lineY * sy) + window.Client.Y1 - region.Y1);
            int fromX = (int)((1000 * sx) + window.Client.X1 - region.X1), toX = (int)((1300 * sx) + window.Client.X1 - region.X1);

            // How many rows around the line differ clearly, and how much, counted along the middle of the line.
            List<int> thickness = [];
            double signedChange = 0, strongest = 0;
            int samples = 0;
            for (int x = fromX; x <= toX; x += 6)
            {
                int count = 0;
                for (int y = Math.Max(0, row - 60); y < Math.Min(height, row + 60); y++)
                {
                    double change = Luminance(after, y * width + x) - Luminance(before, y * width + x);
                    if (Math.Abs(change) > 12)
                        count++;

                    if (Math.Abs(y - row) < 4)
                    {
                        signedChange += change;
                        strongest = Math.Max(strongest, Math.Abs(change));
                        samples++;
                    }
                }

                thickness.Add(count);
            }

            thickness.Sort();
            return $"{label}: {thickness[thickness.Count / 2]} px of visible change across the line, brightness change on the line itself {signedChange / Math.Max(1, samples):0.0} on average, up to {strongest:0}";
        }

        await Run(
        [
            new ClickControlStep("Clear", At(79, 79)),
            new ClickControlStep("Carve", carve),
            new ClickControlStep("Cube", cube),
            new SetBrushValueStep("Size", size, "8"),
            new SetBrushValueStep("Hardness", hardness, "0.5"),
            new ClickControlStep("Carve", carve)
        ]);

        PixelBuffer blank = await Look();

        await Run([new PaintStrokeStep(line, At(1000, 500), At(1300, 500))]);
        PixelBuffer carvedOnce = await Look();
        Write(Describe("carve, one pass", blank, carvedOnce, 500));

        await Run([new PaintStrokeStep(line, At(1000, 500), At(1300, 500))]);
        PixelBuffer carvedTwice = await Look();
        Write(Describe("carve, second pass over the same line", carvedOnce, carvedTwice, 500));

        await Run([new ClickControlStep("Add", add), new PaintStrokeStep(line, At(1000, 760), At(1300, 760))]);
        PixelBuffer added = await Look();
        Write(Describe("add, one pass", carvedTwice, added, 760));

        await Run([new ClickControlStep("Carve", carve), new SetBrushValueStep("Size", size, "16"), new PaintStrokeStep(line, At(1000, 1000), At(1300, 1000))]);
        PixelBuffer bigger = await Look();
        Write(Describe("carve, size 16, one pass", added, bigger, 1000));

        const int zoom = 1;
        PixelBuffer shown = bigger;
        if (Environment.GetEnvironmentVariable("RUSTYPAINTER_SHOTS") is { Length: > 0 } folder)
            TestImages.SavePng(shown, Path.Combine(folder, "sculpture.png"));

        await Run([new ClickControlStep("Clear", At(79, 79))]);
        Write("done, stone cleared");
    }

    /// <summary>Reads which tool and which brush are lit on the sculpting screen. Reads only.</summary>
    [Fact]
    public async Task Read_the_sculpture_panel()
    {
        Assert.SkipUnless(Mode == "sculptlook", "Set RUSTYPAINTER_LIVE=sculptlook to run this. It only reads pixels.");

        GameWindowService game = new();
        Win32PaintSensors sensors = new(game);
        for (int i = 0; i < 90 && !sensors.IsGameInFront(); i++)
            await Task.Delay(500, TestContext.Current.CancellationToken);

        Assert.True(sensors.IsGameInFront(), "Rust did not come to the front in time.");
        await Task.Delay(1000, TestContext.Current.CancellationToken);

        GameWindow window = game.Find()!;
        double sx = window.Width / 2560.0, sy = window.Height / 1440.0;
        ScreenPoint At(double x, double y) => new(window.Client.X1 + x * sx, window.Client.Y1 + y * sy);

        string Describe(Rgb? color) => color is { } c ? $"{c.Hex}{(c.G - Math.Max(c.R, c.B) >= 25 ? " GREEN" : "")}" : "unreadable";
        string[] tools = ["carve", "add", "push/pull", "slab", "clip", "smooth"];
        for (int i = 0; i < 6; i++)
            Write($"tool {tools[i]} at {2112 + i * 64},507: top {Describe(sensors.ReadColor(At(2112 + i * 64, 507 - 26)))} left {Describe(sensors.ReadColor(At(2112 + i * 64 - 26, 507)))}");

        string[] brushes = ["sphere", "cube", "cylinder", "cone", "capsule", "hexagon"];
        for (int i = 0; i < 6; i++)
            Write($"brush {brushes[i]} at {2112 + i * 64},686: top {Describe(sensors.ReadColor(At(2112 + i * 64, 686 - 26)))} left {Describe(sensors.ReadColor(At(2112 + i * 64 - 26, 686)))}");
    }

    /// <summary>Carves one line at each of a few small brush sizes, on a clean block each time, and reports how wide each one is.</summary>
    [AvaloniaFact]
    public async Task Sweep_the_carve_sizes()
    {
        Assert.SkipUnless(Mode == "sculptsizes", "Set RUSTYPAINTER_LIVE=sculptsizes to run this. It cuts into the stone and sends real input to the game.");

        CancellationToken stop = TestContext.Current.CancellationToken;
        GameWindowService game = new();
        Win32PaintSensors sensors = new(game);
        Win32ScreenCapture capture = new();

        for (int i = 0; i < 90 && !sensors.IsGameInFront(); i++)
            await Task.Delay(500, stop);

        Assert.True(sensors.IsGameInFront(), "Rust did not come to the front in time.");
        await Task.Delay(3000, stop);

        GameWindow window = game.Find()!;
        double sx = window.Width / 2560.0, sy = window.Height / 1440.0;
        ScreenPoint At(double x, double y) => new(window.Client.X1 + x * sx, window.Client.Y1 + y * sy);
        ScreenRect region = new(window.Client.X1 + 780 * sx, window.Client.Y1 + 280 * sy, window.Client.X1 + 1550 * sx, window.Client.Y1 + 1170 * sy);

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
            await Task.Delay(800, stop);
            Assert.True(sensors.IsGameInFront(), "Rust is no longer in front, so nothing was copied.");
            return capture.Capture(region) ?? throw new InvalidOperationException("The block could not be copied.");
        }

        ScreenPoint carve = At(2112, 507), cube = At(2176, 686), size = At(2488, 754);
        Stroke line = new(0, 0, 2, Vertical: false);
        int number = 0;

        foreach (string value in new[] { "0.5", "1", "2", "4" })
        {
            await Run([new ClickControlStep("Clear", At(79, 79)), new ClickControlStep("Carve", carve), new ClickControlStep("Cube", cube), new SetBrushValueStep("Size", size, value)]);
            PixelBuffer before = await Look();
            await Run([new PaintStrokeStep(line, At(1000, 720), At(1300, 720))]);
            PixelBuffer after = await Look();

            int row = (int)(720 * sy + window.Client.Y1 - region.Y1);
            List<int> thickness = [];
            double strongest = 0;
            for (int x = (int)(1060 * sx + window.Client.X1 - region.X1); x <= (int)(1240 * sx + window.Client.X1 - region.X1); x += 6)
            {
                int count = 0;
                for (int y = Math.Max(0, row - 90); y < Math.Min(before.Height, row + 90); y++)
                {
                    double change = Math.Abs(Luminance(after, y * before.Width + x) - Luminance(before, y * before.Width + x));
                    if (change > 10)
                        count++;

                    strongest = Math.Max(strongest, change);
                }

                thickness.Add(count);
            }

            thickness.Sort();
            Write($"size {value}: {thickness[thickness.Count / 2]} px of visible change across the line, strongest change {strongest:0}");

            if (Environment.GetEnvironmentVariable("RUSTYPAINTER_SHOTS") is { Length: > 0 } folder)
                TestImages.SavePng(after, Path.Combine(folder, $"sculpt-size-{++number}.png"));
        }

        await Run([new ClickControlStep("Clear", At(79, 79))]);
        Write("done, stone cleared");
    }
}
