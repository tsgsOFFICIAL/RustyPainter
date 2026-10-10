using Avalonia;
using RustyPainter.Interface;
using RustyPainter.Painting;
using RustyPainter.Windows;
using System;

namespace RustyPainter.Service;

/// <inheritdoc cref="IStatusOverlayService" />
public sealed class StatusOverlayService : IStatusOverlayService
{
    private StatusOverlayWindow? _window;

    public bool IsShown => _window is not null;

    public void Show(object content, ScreenRect area)
    {
        Hide();

        _window = new StatusOverlayWindow { DataContext = content };
        _window.MoveTo(new PixelRect((int)Math.Round(area.X1), (int)Math.Round(area.Y1), (int)Math.Round(area.Width), (int)Math.Round(area.Height)));
        _window.Show();
    }

    public void Hide()
    {
        // Closed rather than hidden, so a window that is out of sight can never keep the app from ending.
        _window?.Close();
        _window = null;
    }
}
