using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using RustyPainter.Service;
using RustyPainter.ViewModels;
using RustyPainter.Views;

namespace RustyPainter.Tests;

/// <summary>Draws the real Studio page headlessly and looks at the pixels.</summary>
public class StudioRenderTests : IDisposable
{
    // A 1x1 red PNG, stretched by the page to cover the whole canvas.
    private const string OnePixelPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

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
        File.WriteAllBytes(path, Convert.FromBase64String(OnePixelPng));

        StudioViewModel studio = new(new SurfaceService());
        Assert.True(studio.TryLoad(path));

        WriteableBitmap frame = Draw(studio, "studio-loaded");

        // The canvas sits in the middle column. Sample its centre and a point near each side of the sign.
        foreach ((int x, int y) in new[] { (700, 380), (560, 380), (840, 380) })
        {
            (byte r, byte g, byte b) = PixelAt(frame, x, y);
            Assert.True(IsRed(r, g, b), $"Expected red at {x},{y} but got {r},{g},{b}");
        }
    }

    [AvaloniaFact]
    public void The_canvas_follows_the_selected_surface()
    {
        string path = Path.Combine(_folder, "red.png");
        File.WriteAllBytes(path, Convert.FromBase64String(OnePixelPng));

        StudioViewModel studio = new(new SurfaceService());
        studio.TryLoad(path);

        // The huge sign is wide and flat, so a point high above its centre must not be on the canvas.
        studio.SelectedSurface = studio.AllSurfaces.Single(option => option.Surface.Id == "sign-huge");
        WriteableBitmap wide = Draw(studio, "studio-huge-sign");
        (byte r, byte g, byte b) top = PixelAt(wide, 700, 180);
        Assert.False(IsRed(top.r, top.g, top.b), $"The flat canvas should not reach up to y=180, got {top}");

        // The standing canvas is tall, so the same point must be on the canvas.
        studio.SelectedSurface = studio.AllSurfaces.Single(option => option.Surface.Id == "canvas-standing");
        WriteableBitmap tall = Draw(studio, "studio-standing-canvas");
        (byte r, byte g, byte b) topTall = PixelAt(tall, 700, 180);
        Assert.True(IsRed(topTall.r, topTall.g, topTall.b), $"The tall canvas should reach up to y=180, got {topTall}");
    }
}
