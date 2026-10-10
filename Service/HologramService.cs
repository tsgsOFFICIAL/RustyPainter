using Avalonia;
using Avalonia.Media.Imaging;
using RustyPainter.Imaging;
using RustyPainter.Interface;
using RustyPainter.Painting;
using RustyPainter.Windows;
using System;

namespace RustyPainter.Service;

/// <inheritdoc cref="IHologramService" />
public sealed class HologramService : IHologramService
{
    private HologramWindow? _window;
    private PixelBuffer? _shown;

    public bool IsShown => _window is not null;

    public void Show(PixelBuffer picture, ScreenRect area, double opacity)
    {
        bool isNew = _window is null;
        _window ??= new HologramWindow();

        // The picture is only converted again when it is a different one, so moving it is cheap.
        if (!ReferenceEquals(picture, _shown))
        {
            WriteableBitmap bitmap = BitmapPixels.ToBitmap(picture);
            Bitmap? old = _window.Image;
            _window.Image = bitmap;
            old?.Dispose();
            _shown = picture;
        }

        _window.PictureOpacity = Math.Clamp(opacity, 0, 1);
        _window.MoveTo(new PixelRect((int)Math.Round(area.X1), (int)Math.Round(area.Y1), (int)Math.Round(area.Width), (int)Math.Round(area.Height)));

        if (isNew || !_window.IsVisible)
            _window.Show();
    }

    public void Hide()
    {
        // Closed rather than hidden, so a window that is out of sight can never keep the app from ending.
        Bitmap? old = _window?.Image;
        _window?.Close();
        old?.Dispose();
        _window = null;
        _shown = null;
    }
}
