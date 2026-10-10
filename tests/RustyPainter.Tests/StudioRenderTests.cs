using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
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
    public void The_letter_color_picker_opens_with_a_spectrum()
    {
        StudioViewModel studio = new(new SurfaceService());
        studio.SourceChoices.Select(SourceKind.Text);
        studio.Text = "Hi";

        Window window = new() { Width = 1296, Height = 760, Content = new StudioView { DataContext = studio } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        ColorPicker picker = window.GetVisualDescendants().OfType<ColorPicker>().First();
        DropDownButton button = picker.GetVisualDescendants().OfType<DropDownButton>().First();
        Flyout flyout = Assert.IsType<Flyout>(button.Flyout);
        flyout.ShowAt(button);
        Dispatcher.UIThread.RunJobs();

        Assert.True(flyout.IsOpen);

        WriteableBitmap frame = window.CaptureRenderedFrame()!;
        if (Environment.GetEnvironmentVariable("RUSTYPAINTER_SHOTS") is { Length: > 0 } folder)
            frame.Save(Path.Combine(folder, "studio-color-picker.png"));
    }

    [AvaloniaFact]
    public async Task Dragging_the_picture_moves_the_shown_render_and_applies_the_settings_once()
    {
        string path = Path.Combine(_folder, "red.png");
        TestImages.SavePng(TestImages.RedAndGreen(), path);

        StudioViewModel studio = new(new SurfaceService());
        Assert.True(studio.TryLoad(path));
        studio.ProcessNow();

        Window window = new() { Width = 1296, Height = 760, Content = new StudioView { DataContext = studio } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Border stage = window.GetVisualDescendants().OfType<Border>().First(border => border.Name == "Stage");
        Image shown = window.GetVisualDescendants().OfType<Image>().First(image => image.Name == "PreviewImage");
        Point topLeft = stage.TranslatePoint(new Point(0, 0), window)!.Value;
        Point bottomRight = stage.TranslatePoint(new Point(stage.Bounds.Width, stage.Bounds.Height), window)!.Value;
        double shownWidth = bottomRight.X - topLeft.X;
        Point middle = new(topLeft.X + shownWidth / 2, (topLeft.Y + bottomRight.Y) / 2);

        window.MouseDown(middle, MouseButton.Left);
        window.MouseMove(new Point(middle.X + 60, middle.Y));
        window.MouseMove(new Point(middle.X + 60, middle.Y + 30));

        // While the mouse is down the settings stay as they were and the shown render is simply moved.
        Assert.Equal(0, studio.OffsetX);
        Assert.False(shown.RenderTransform is null);
        Assert.True(((MatrixTransform)shown.RenderTransform!).Matrix.M31 > 0);

        window.MouseUp(new Point(middle.X + 60, middle.Y + 30), MouseButton.Left);

        Assert.InRange(studio.OffsetX, 60 / shownWidth * 100 - 0.6, 60 / shownWidth * 100 + 0.6);
        Assert.True(studio.OffsetY > 0);

        // Once the new render arrives the moved copy is dropped.
        for (int i = 0; i < 100 && studio.IsBusy; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(30);
        }

        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Matrix.Identity, ((MatrixTransform)shown.RenderTransform!).Matrix);

        // Moving the mouse afterwards without a button held must not drag.
        double before = studio.OffsetX;
        window.MouseMove(new Point(middle.X + 200, middle.Y));
        Assert.Equal(before, studio.OffsetX);
    }

    [AvaloniaFact]
    public async Task The_mouse_wheel_zooms_once_it_stops()
    {
        string path = Path.Combine(_folder, "red.png");
        TestImages.SavePng(TestImages.RedAndGreen(), path);

        StudioViewModel studio = new(new SurfaceService());
        Assert.True(studio.TryLoad(path));
        studio.ProcessNow();

        Window window = new() { Width = 1296, Height = 760, Content = new StudioView { DataContext = studio } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Border stage = window.GetVisualDescendants().OfType<Border>().First(border => border.Name == "Stage");
        Image shown = window.GetVisualDescendants().OfType<Image>().First(image => image.Name == "PreviewImage");
        Point middle = stage.TranslatePoint(new Point(stage.Bounds.Width / 2, stage.Bounds.Height / 2), window)!.Value;

        window.MouseWheel(middle, new Vector(0, 1));
        window.MouseWheel(middle, new Vector(0, 1));

        Assert.Equal(1, studio.Zoom);
        Assert.True(((MatrixTransform)shown.RenderTransform!).Matrix.M11 > 1.2);

        for (int i = 0; i < 100 && studio.Zoom == 1; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(30);
        }

        Assert.Equal(1.21, studio.Zoom, 2);
    }

    [AvaloniaFact]
    public void Dragging_the_divider_moves_the_split_without_moving_the_picture()
    {
        string path = Path.Combine(_folder, "red.png");
        TestImages.SavePng(TestImages.Rainbow(), path);

        StudioViewModel studio = new(new SurfaceService());
        Assert.True(studio.TryLoad(path));
        studio.ProcessNow();
        studio.PreviewChoices.Select(PreviewMode.Split);

        Window window = new() { Width = 1296, Height = 760, Content = new StudioView { DataContext = studio } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Border stage = window.GetVisualDescendants().OfType<Border>().First(border => border.Name == "Stage");
        Border strip = window.GetVisualDescendants().OfType<Border>().First(border => border.Name == "SplitStrip");
        Point topLeft = stage.TranslatePoint(new Point(0, 0), window)!.Value;
        Point bottomRight = stage.TranslatePoint(new Point(stage.Bounds.Width, stage.Bounds.Height), window)!.Value;
        double shownWidth = bottomRight.X - topLeft.X;

        Point handle = strip.TranslatePoint(new Point(strip.Bounds.Width / 2, strip.Bounds.Height / 2), window)!.Value;
        Assert.Equal(topLeft.X + shownWidth / 2, handle.X, 1);

        double target = topLeft.X + shownWidth * 0.25;
        window.MouseDown(handle, MouseButton.Left);
        window.MouseMove(new Point(target, handle.Y));
        window.MouseUp(new Point(target, handle.Y), MouseButton.Left);

        Assert.Equal(0.25, studio.SplitPosition, 2);
        Assert.Equal(0, studio.OffsetX);
        Assert.Equal(0, studio.OffsetY);
    }

    [AvaloniaFact]
    public void The_split_view_shows_a_divider_in_the_middle()
    {
        string path = Path.Combine(_folder, "red.png");
        TestImages.SavePng(TestImages.Rainbow(), path);

        StudioViewModel studio = new(new SurfaceService());
        Assert.True(studio.TryLoad(path));
        studio.ProcessNow();
        studio.PreviewChoices.Select(PreviewMode.Split);

        WriteableBitmap frame = Draw(studio, "studio-split");

        (byte r, byte g, byte b) = PixelAt(frame, 666, 395);
        Assert.True(r > 200 && g > 200 && b > 200, $"Expected the white divider in the middle, got {r},{g},{b}");
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
