using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System.Globalization;
using System;

namespace RustyPainter.Imaging;

/// <summary>What a picture of text looks like.</summary>
/// <param name="Color">The color of the letters.</param>
/// <param name="Background">The color behind the letters, or <see langword="null"/> to leave it unpainted.</param>
public sealed record TextPictureSettings(Rgb Color, Rgb? Background, bool Bold);

/// <summary>Draws text into a picture, so it can be painted like any other picture.</summary>
public static class TextPicture
{
    private const double BaseFontSize = 200;
    private const double MaxSide = 4096;

    /// <summary>The picture is as big as the text needs, with a margin around it.</summary>
    /// <param name="text">The text. Line breaks start new lines, each one centered.</param>
    /// <param name="font">The font to use, or <see langword="null"/> for the default one.</param>
    public static RenderTargetBitmap Render(string text, TextPictureSettings settings, FontFamily? font = null)
    {
        Typeface typeface = new(font ?? FontFamily.Default, FontStyle.Normal, settings.Bold ? FontWeight.Bold : FontWeight.Normal);
        IBrush ink = new SolidColorBrush(Avalonia.Media.Color.FromRgb(settings.Color.R, settings.Color.G, settings.Color.B));

        FormattedText Format(double size, double? width = null) => new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, ink)
        {
            TextAlignment = TextAlignment.Center,
            MaxTextWidth = width ?? double.PositiveInfinity
        };

        // Measure at a large size first, and shrink the whole thing if the text is very long.
        FormattedText probe = Format(BaseFontSize);
        double size = BaseFontSize;
        double margin = size * 0.15;
        double scale = Math.Min(1, (MaxSide - 2 * margin) / Math.Max(probe.Width, probe.Height));

        if (scale < 1)
        {
            size = BaseFontSize * scale;
            margin = size * 0.15;
        }

        // Centering needs a width to center in, so lay the text out again at its own measured width.
        double textWidth = Math.Ceiling(Format(size).Width);
        FormattedText formatted = Format(size, textWidth);

        int width = (int)Math.Ceiling(textWidth + 2 * margin);
        int height = (int)Math.Ceiling(formatted.Height + 2 * margin);

        RenderTargetBitmap bitmap = new(new PixelSize(Math.Max(1, width), Math.Max(1, height)));
        using DrawingContext context = bitmap.CreateDrawingContext();

        if (settings.Background is { } background)
        {
            context.FillRectangle(
                new SolidColorBrush(Avalonia.Media.Color.FromRgb(background.R, background.G, background.B)),
                new Rect(0, 0, width, height));
        }

        context.DrawText(formatted, new Point(margin, margin));
        return bitmap;
    }
}
