using System.Net;
using Avalonia.Headless.XUnit;
using RustyPainter.Imaging;
using RustyPainter.Interface;
using RustyPainter.Service;
using RustyPainter.ViewModels;

namespace RustyPainter.Tests;

/// <summary>Pictures from a web link and from typed text.</summary>
public class StudioSourceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"rustypainter-source-{Guid.NewGuid():N}");

    public StudioSourceTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private byte[] PngBytes()
    {
        string path = Path.Combine(_folder, "picture.png");
        TestImages.SavePng(TestImages.RedAndGreen(4, 2), path);
        return File.ReadAllBytes(path);
    }

    private sealed class FakeDownloader(byte[]? data = null, string? failure = null) : IPictureDownloader
    {
        public Uri? Asked { get; private set; }

        public Task<byte[]> DownloadAsync(Uri link, CancellationToken cancellation = default)
        {
            Asked = link;
            if (failure is not null)
                throw new PictureDownloadException(failure);

            return Task.FromResult(data ?? []);
        }
    }

    [Fact]
    public void Recognizes_the_supported_pictures_by_their_first_bytes()
    {
        Assert.Equal(PictureFormat.Png, PictureFormatDetector.Detect([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]));
        Assert.Equal(PictureFormat.Jpeg, PictureFormatDetector.Detect([0xFF, 0xD8, 0xFF, 0xE0]));
        Assert.Equal(PictureFormat.Bmp, PictureFormatDetector.Detect("BM...."u8));
        Assert.Equal(PictureFormat.WebP, PictureFormatDetector.Detect("RIFF\0\0\0\0WEBPVP8 "u8));
        Assert.Equal(PictureFormat.Gif, PictureFormatDetector.Detect("GIF89a"u8));
        Assert.Equal(PictureFormat.Unknown, PictureFormatDetector.Detect("<html></html>"u8));
        Assert.Equal(PictureFormat.Unknown, PictureFormatDetector.Detect([]));
    }

    [AvaloniaFact]
    public async Task Loads_a_picture_from_a_link()
    {
        FakeDownloader downloader = new(PngBytes());
        StudioViewModel studio = new(new SurfaceService(), downloader);

        bool loaded = await studio.TryLoadLinkAsync("https://example.com/pictures/sunset.png");

        Assert.True(loaded, studio.ErrorText);
        Assert.Equal("sunset.png", studio.ImageName);
        Assert.Equal("4 x 2", studio.ImageInfo);
        Assert.False(studio.IsLoadingLink);
        Assert.Equal("https://example.com/pictures/sunset.png", downloader.Asked!.ToString());
    }

    [AvaloniaFact]
    public async Task A_link_without_a_scheme_gets_https()
    {
        FakeDownloader downloader = new(PngBytes());
        StudioViewModel studio = new(new SurfaceService(), downloader);

        Assert.True(await studio.TryLoadLinkAsync("example.com/a.png"));
        Assert.Equal("https", downloader.Asked!.Scheme);
    }

    [AvaloniaTheory]
    [InlineData("")]
    [InlineData("hello there")]
    [InlineData("ftp://example.com/a.png")]
    [InlineData("nonsense")]
    public async Task Rejects_text_that_is_not_a_web_link(string text)
    {
        FakeDownloader downloader = new(PngBytes());
        StudioViewModel studio = new(new SurfaceService(), downloader);

        Assert.False(await studio.TryLoadLinkAsync(text));
        Assert.True(studio.HasError);
        Assert.Null(downloader.Asked);
    }

    [AvaloniaFact]
    public async Task Refuses_a_gif_from_a_link()
    {
        StudioViewModel studio = new(new SurfaceService(), new FakeDownloader("GIF89a...."u8.ToArray()));

        Assert.False(await studio.TryLoadLinkAsync("https://example.com/funny.png"));
        Assert.Contains("GIF", studio.ErrorText);
        Assert.False(studio.HasImage);
    }

    [AvaloniaFact]
    public async Task Refuses_a_link_that_is_not_a_picture()
    {
        StudioViewModel studio = new(new SurfaceService(), new FakeDownloader("<html>not a picture</html>"u8.ToArray()));

        Assert.False(await studio.TryLoadLinkAsync("https://example.com/page"));
        Assert.True(studio.HasError);
        Assert.False(studio.HasImage);
    }

    [AvaloniaFact]
    public async Task A_failed_download_keeps_the_picture_and_explains()
    {
        StudioViewModel studio = new(new SurfaceService(), new FakeDownloader(failure: "The website answered with error 404."));
        string path = Path.Combine(_folder, "kept.png");
        TestImages.SavePng(TestImages.RedAndGreen(), path);
        Assert.True(studio.TryLoad(path));

        Assert.False(await studio.TryLoadLinkAsync("https://example.com/missing.png"));

        Assert.Equal("The website answered with error 404.", studio.ErrorText);
        Assert.Equal("kept.png", studio.ImageName);
        Assert.False(studio.IsLoadingLink);
    }

    [AvaloniaFact]
    public async Task The_load_button_needs_a_link()
    {
        StudioViewModel studio = new(new SurfaceService(), new FakeDownloader(PngBytes()));
        Assert.False(studio.LoadLinkCommand.CanExecute(null));

        studio.LinkText = "https://example.com/a.png";
        Assert.True(studio.LoadLinkCommand.CanExecute(null));

        Assert.True(await studio.TryLoadLinkAsync(studio.LinkText));
    }

    [Fact]
    public async Task Downloads_the_bytes_from_a_real_server()
    {
        byte[] payload = [1, 2, 3, 4, 5];
        using HttpListener listener = new();
        string prefix = $"http://127.0.0.1:{FreePort()}/";
        listener.Prefixes.Add(prefix);
        listener.Start();

        Task serve = Task.Run(async () =>
        {
            HttpListenerContext context = await listener.GetContextAsync();
            if (context.Request.Url!.AbsolutePath == "/missing")
                context.Response.StatusCode = 404;
            else
                await context.Response.OutputStream.WriteAsync(payload);
            context.Response.Close();
        });

        byte[] bytes = await new PictureDownloader().DownloadAsync(new Uri(prefix + "picture.png"));
        await serve;

        Assert.Equal(payload, bytes);
    }

    [Fact]
    public async Task A_server_error_becomes_a_readable_message()
    {
        using HttpListener listener = new();
        string prefix = $"http://127.0.0.1:{FreePort()}/";
        listener.Prefixes.Add(prefix);
        listener.Start();

        Task serve = Task.Run(async () =>
        {
            HttpListenerContext context = await listener.GetContextAsync();
            context.Response.StatusCode = 404;
            context.Response.Close();
        });

        PictureDownloadException error = await Assert.ThrowsAsync<PictureDownloadException>(
            () => new PictureDownloader().DownloadAsync(new Uri(prefix + "missing")));
        await serve;

        Assert.Contains("404", error.Message);
    }

    [Fact]
    public async Task Only_web_links_can_be_downloaded()
    {
        await Assert.ThrowsAsync<PictureDownloadException>(() => new PictureDownloader().DownloadAsync(new Uri("file:///C:/secret.png")));
    }

    private static int FreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    // ---- text ----

    private static PixelBuffer Draw(string text, Rgb color, Rgb? background = null, bool bold = true)
    {
        using var bitmap = TextPicture.Render(text, new TextPictureSettings(color, background, bold));
        return BitmapPixels.FromBitmap(bitmap);
    }

    [AvaloniaFact]
    public void Text_becomes_a_picture_in_the_chosen_color()
    {
        PixelBuffer picture = Draw("HI", new Rgb(255, 0, 0));

        Assert.True(picture.Width > 20 && picture.Height > 20);

        bool anyRed = false, anyOpaque = false;
        for (int p = 0; p < picture.Width * picture.Height; p++)
        {
            byte alpha = picture.Data[p * 4 + 3];
            if (alpha == 255)
            {
                anyOpaque = true;
                anyRed |= picture.Data[p * 4] > 200 && picture.Data[p * 4 + 1] < 40 && picture.Data[p * 4 + 2] < 40;
            }
        }

        Assert.True(anyOpaque);
        Assert.True(anyRed);
    }

    [AvaloniaFact]
    public void A_clear_background_is_transparent_and_a_solid_one_is_not()
    {
        PixelBuffer clear = Draw("I", new Rgb(255, 255, 255));
        Assert.Equal(0, clear.Data[3]);

        PixelBuffer solid = Draw("I", new Rgb(255, 255, 255), new Rgb(0, 0, 255));
        Assert.Equal((0, 0, 255, 255), (solid.Data[0], solid.Data[1], solid.Data[2], solid.Data[3]));
    }

    [AvaloniaFact]
    public void More_lines_make_a_taller_picture()
    {
        PixelBuffer one = Draw("RUST", new Rgb(255, 255, 255));
        PixelBuffer two = Draw("RUST\nPAINTER", new Rgb(255, 255, 255));

        Assert.True(two.Height > one.Height * 1.6, $"{one.Height} vs {two.Height}");
    }

    [AvaloniaFact]
    public void Very_long_text_stays_within_a_sane_size()
    {
        PixelBuffer picture = Draw(new string('W', 400), new Rgb(255, 255, 255));

        Assert.InRange(picture.Width, 1, 4200);
        Assert.InRange(picture.Height, 1, 4200);
    }

    [AvaloniaFact]
    public void Typing_text_makes_the_picture_and_keeps_the_placement()
    {
        StudioViewModel studio = new(new SurfaceService());
        studio.SourceChoices.Select(SourceKind.Text);

        studio.Text = "Hello";

        Assert.True(studio.HasImage);
        Assert.Equal("Text", studio.ImageName);
        Assert.Equal(FitMode.Fit, studio.PlacementChoices.Selected);

        studio.Zoom = 2;

        studio.Text = "Hello there";
        Assert.Equal(2, studio.Zoom);
        Assert.NotNull(studio.ProcessNow());
    }

    [AvaloniaFact]
    public void Nothing_is_drawn_for_empty_text_or_an_unfinished_color()
    {
        StudioViewModel studio = new(new SurfaceService());
        studio.SourceChoices.Select(SourceKind.Text);

        studio.Text = "   ";
        Assert.False(studio.HasImage);

        studio.Text = "Hi";
        Assert.True(studio.HasImage);

        // Typing a new color passes through half finished values; the picture stays as it was.
        studio.TextColorHex = "#FF";
        Assert.True(studio.HasImage);
        Assert.False(studio.HasError);
    }

    [AvaloniaFact]
    public void Text_only_updates_the_picture_while_the_text_tab_is_open()
    {
        StudioViewModel studio = new(new SurfaceService());
        studio.Text = "Hello";
        Assert.False(studio.HasImage);

        studio.SourceChoices.Select(SourceKind.Text);

        Assert.True(studio.HasImage);
    }

    [AvaloniaFact]
    public void The_solid_background_color_is_used()
    {
        StudioViewModel studio = new(new SurfaceService());
        studio.SourceChoices.Select(SourceKind.Text);
        studio.Text = "I";
        studio.TextBackgroundChoices.Select(true);
        studio.TextBackgroundHex = "#0000FF";
        studio.PlacementChoices.Select(FitMode.Fill);
        studio.ColorModeChoices.Select(ColorMode.Free);
        studio.MaxColors = 4;

        PaintPlan plan = studio.ProcessNow()!;

        Assert.Equal(plan.Width * plan.Height, plan.PaintedCells);
    }
}
