using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using RustyPainter.Imaging;
using RustyPainter.Service;
using RustyPainter.ViewModels;
using RustyPainter.Views;

namespace RustyPainter.Tests;

/// <summary>Draws the real Studio page headlessly and looks at the pixels.</summary>
public class StudioRenderTests : IDisposable
{

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"rustypainter-render-{Guid.NewGuid():N}");

    public StudioRenderTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static WriteableBitmap Draw(StudioViewModel studio, string name)
    {
        Window window = new() { Width = 1296, Height = 760, Content = new StudioView { DataContext = studio } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        WriteableBitmap frame = window.CaptureRenderedFrame()!;

        // Set RUSTYPAINTER_SHOTS to a folder to keep the frames for a look with your own eyes.
        if (Environment.GetEnvironmentVariable("RUSTYPAINTER_SHOTS") is { Length: > 0 } folder)
            frame.Save(Path.Combine(folder, name + ".png"));

        return frame;
    }

    /// <summary>Clearly red: well above both of the other channels. The test picture is a dark red, not pure red.</summary>
    private static bool IsRed(byte r, byte g, byte b) => r > 100 && r > g + 60 && r > b + 60;

    private static (byte R, byte G, byte B) PixelAt(WriteableBitmap bitmap, int x, int y)
    {
        IntPtr buffer = Marshal.AllocHGlobal(4);
        try
        {
            bitmap.CopyPixels(new PixelRect(x, y, 1, 1), buffer, 4, 4);
            byte[] bytes = new byte[4];
            Marshal.Copy(buffer, bytes, 0, 4);

            // Which byte holds red depends on the format the platform draws in.
            return bitmap.Format == PixelFormat.Bgra8888
                ? (bytes[2], bytes[1], bytes[0])
                : (bytes[0], bytes[1], bytes[2]);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [AvaloniaFact]
    public void An_empty_page_shows_the_empty_canvas_without_red()
    {
        StudioViewModel studio = new(new SurfaceService());

        WriteableBitmap frame = Draw(studio, "studio-empty");

        // The middle of the preview stage, away from the placeholder text.
        (byte r, byte g, byte b) = PixelAt(frame, 700, 330);
        Assert.False(IsRed(r, g, b), $"Unexpected red at the canvas: {r},{g},{b}");
    }

    [AvaloniaFact]
    public void A_loaded_picture_covers_the_whole_canvas()
    {
        string path = Path.Combine(_folder, "red.png");
        TestImages.SavePng(PixelBuffer.Solid(1, 1, 200, 30, 30), path);

        StudioViewModel studio = new(new SurfaceService());
        Assert.True(studio.TryLoad(path));

        // Any color, so the picture keeps its own red instead of the nearest game swatch.
        studio.ColorModeChoices.Select(ColorMode.Free);
        studio.ProcessNow();

        WriteableBitmap frame = Draw(studio, "studio-loaded");

        // The canvas sits in the middle column. Sample its centre and a point near each side of the sign.
        foreach ((int x, int y) in new[] { (700, 380), (560, 380), (840, 380) })
        {
            (byte r, byte g, byte b) = PixelAt(frame, x, y);
            Assert.True(IsRed(r, g, b), $"Expected red at {x},{y} but got {r},{g},{b}");
        }
    }

    [AvaloniaFact]
    public void A_spinner_shows_while_the_picture_is_being_prepared()
    {
        string path = Path.Combine(_folder, "red.png");
        TestImages.SavePng(PixelBuffer.Solid(1, 1, 200, 30, 30), path);

        // Hold the update back so the page is still busy when it is drawn.
        StudioViewModel studio = new(new SurfaceService()) { ReprocessDelay = TimeSpan.FromSeconds(30) };
        Assert.True(studio.TryLoad(path));
        Assert.True(studio.IsBusy);

        WriteableBitmap busy = Draw(studio, "studio-busy");
        Assert.True(HasAccent(busy), "Expected the orange spinner in the top right of the preview.");

        studio.ProcessNow();
        WriteableBitmap done = Draw(studio, "studio-not-busy");
        Assert.False(HasAccent(done), "The spinner should be gone once the picture is ready.");
    }

    [AvaloniaFact]
    public void The_text_tab_shows_the_text_on_the_canvas()
    {
        StudioViewModel studio = new(new SurfaceService());
        studio.SourceChoices.Select(SourceKind.Text);
        studio.Text = "RUST\nPAINTER";
        studio.ColorModeChoices.Select(ColorMode.Free);
        studio.ProcessNow();

        WriteableBitmap frame = Draw(studio, "studio-text");

        // The letters are white on a clear background, so somewhere on the canvas there must be near white.
        bool white = false;
        for (int y = 250; y < 420 && !white; y += 2)
            for (int x = 500; x < 840 && !white; x += 2)
            {
                (byte r, byte g, byte b) = PixelAt(frame, x, y);
                white = r > 230 && g > 230 && b > 230;
            }

        Assert.True(white, "Expected white letters on the canvas.");
    }

    [AvaloniaFact]
    public void The_link_tab_draws_without_trouble()
    {
        StudioViewModel studio = new(new SurfaceService());
        studio.SourceChoices.Select(SourceKind.Link);
        studio.LinkText = "https://example.com/picture.png";

        Draw(studio, "studio-link");
    }

    /// <summary>Looks for the orange accent in the top right corner of the preview stage, where the spinner sits.</summary>
    private static bool HasAccent(WriteableBitmap frame)
    {
        for (int y = 70; y < 120; y++)
            for (int x = 840; x < 1010; x++)
            {
                (byte r, byte g, byte b) = PixelAt(frame, x, y);
                if (r > 200 && g is > 70 and < 160 && b < 70)
                    return true;
            }

        return false;
    }

    [AvaloniaFact]
    public void The_canvas_follows_the_selected_surface()
    {
        string path = Path.Combine(_folder, "red.png");
        TestImages.SavePng(PixelBuffer.Solid(1, 1, 200, 30, 30), path);

        StudioViewModel studio = new(new SurfaceService());
        studio.TryLoad(path);
        studio.ColorModeChoices.Select(ColorMode.Free);

        // The huge sign is wide and flat, so a point high above its centre must not be on the canvas.
        studio.SelectedSurface = studio.AllSurfaces.Single(option => option.Surface.Id == "sign-huge");
        studio.ProcessNow();
        WriteableBitmap wide = Draw(studio, "studio-huge-sign");
        (byte r, byte g, byte b) top = PixelAt(wide, 700, 180);
        Assert.False(IsRed(top.r, top.g, top.b), $"The flat canvas should not reach up to y=180, got {top}");

        // The standing canvas is tall, so the same point must be on the canvas.
        studio.SelectedSurface = studio.AllSurfaces.Single(option => option.Surface.Id == "canvas-standing");
        studio.ProcessNow();
        WriteableBitmap tall = Draw(studio, "studio-standing-canvas");
        (byte r, byte g, byte b) topTall = PixelAt(tall, 700, 180);
        Assert.True(IsRed(topTall.r, topTall.g, topTall.b), $"The tall canvas should reach up to y=180, got {topTall}");
    }
}
