using RustyPainter.Localization;
using Avalonia.Media.Imaging;
using System.Threading.Tasks;
using Avalonia.Threading;
using Avalonia.Platform;
using Avalonia.Controls;
using System.Net.Http;
using Avalonia.Layout;
using Avalonia.Media;
using SkiaSharp;
using System.IO;
using Avalonia;
using System;

namespace RustyPainter;

/// <summary>
/// Loads an image (including animated GIFs) from a local path or an http(s) URL and displays it in
/// an Avalonia <see cref="Image"/>. Animated GIFs are decoded frame-by-frame with SkiaSharp - the
/// same native codec Avalonia already ships with via Avalonia.Skia - and cycled on a timer using
/// each frame's own duration, so no third-party animated-image package is needed.
/// </summary>
public static class AnimatedImage
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>
    /// Creates a placeholder showing <paramref name="altText"/> and begins loading the image in the
    /// background, swapping in the decoded (and, for multi-frame GIFs, animating) image once ready.
    /// Falls back to showing the alt text if the source can't be fetched or decoded.
    /// </summary>
    /// <param name="source">A local file path or an http(s) URL.</param>
    /// <param name="altText">Alt text shown while loading, or if loading fails.</param>
    public static Control Create(string source, string altText)
    {
        TextBlock placeholder = new()
        {
            Text = altText.Length > 0 ? altText : Loc.Get("ImageLoading"),
            FontStyle = FontStyle.Italic
        };
        placeholder.Classes.Add("changelogBody");

        // Stretch, not Left: the host has to be given the full width before the image inside it can
        // decide how much of that width to actually use.
        ContentControl host = new() { Content = placeholder, HorizontalAlignment = HorizontalAlignment.Stretch };

        _ = LoadAsync(host, source, altText);

        return host;
    }

    /// <summary>
    /// Fetches and decodes the image, then swaps it into <paramref name="host"/>. Any failure
    /// (network, unrecognized format, etc.) leaves the alt text in place instead of throwing.
    /// </summary>
    private static async Task LoadAsync(ContentControl host, string source, string altText)
    {
        try
        {
            byte[] bytes = await FetchBytesAsync(source);
            host.Content = BuildImageControl(bytes);
        }
        catch
        {
            if (host.Content is TextBlock placeholder)
                placeholder.Text = altText.Length > 0 ? altText : Loc.Get("ImageFailed");
        }
    }

    /// <summary>
    /// Reads the image bytes from an http(s) URL or a local file path.
    /// </summary>
    private static Task<byte[]> FetchBytesAsync(string source)
    {
        if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return Client.GetByteArrayAsync(source);
        }

        return File.ReadAllBytesAsync(source);
    }

    /// <summary>
    /// Decodes the image bytes and returns a control showing it. A GIF with more than one frame
    /// animates on a timer using each frame's own duration; everything else (single-frame GIF, PNG,
    /// JPEG, WebP, etc.) is shown as a static image.
    /// </summary>
    internal static Control BuildImageControl(byte[] bytes)
    {
        SKCodec codec = SKCodec.Create(new SKMemoryStream(bytes))
            ?? throw new InvalidDataException("Unrecognized image format.");

        SKImageInfo targetInfo = new(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        WriteableBitmap bitmap = new(
            new PixelSize(targetInfo.Width, targetInfo.Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        // Fills whatever width the changelog gives it, shrinking on a narrow window, but never
        // scales past the image's own resolution - upscaling a screenshot just makes it soft. A
        // fixed cap was used here before, which left images marooned in a maximised window.
        Image image = new()
        {
            Source = bitmap,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MaxWidth = targetInfo.Width,
            MaxHeight = targetInfo.Height
        };

        int frameCount = codec.FrameCount;
        if (frameCount <= 1)
        {
            DecodeFrame(codec, targetInfo, bitmap, frameIndex: 0, priorFrame: -1);
            image.InvalidateVisual();
            codec.Dispose();
            return image;
        }

        SKCodecFrameInfo[] frames = codec.FrameInfo;
        int index = 0;
        DispatcherTimer timer = new();

        void Tick()
        {
            DecodeFrame(codec, targetInfo, bitmap, index, frames[index].RequiredFrame);
            image.InvalidateVisual();

            int duration = frames[index].Duration;
            timer.Interval = TimeSpan.FromMilliseconds(duration > 0 ? duration : 100);
            index = (index + 1) % frameCount;
        }

        timer.Tick += (_, _) => Tick();
        Tick();
        timer.Start();

        // Stop the timer and free the codec once the image leaves the visual tree (window closed,
        // scrolled away and recycled, etc.) so it doesn't keep animating and burning CPU forever.
        image.DetachedFromVisualTree += (_, _) =>
        {
            timer.Stop();
            codec.Dispose();
        };

        return image;
    }

    /// <summary>
    /// Decodes a single frame directly into the bitmap's existing pixel buffer. Reusing the same
    /// buffer across frames (rather than a fresh one each time) is what lets SkiaSharp correctly
    /// composite GIF frames that only encode a partial-region delta over the previous frame.
    /// </summary>
    private static void DecodeFrame(SKCodec codec, SKImageInfo info, WriteableBitmap bitmap, int frameIndex, int priorFrame)
    {
        using ILockedFramebuffer framebuffer = bitmap.Lock();
        SKCodecOptions options = new(frameIndex, priorFrame);
        codec.GetPixels(info, framebuffer.Address, framebuffer.RowBytes, options);
    }
}