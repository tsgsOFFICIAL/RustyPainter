using Avalonia;
using Avalonia.Controls;

namespace RustyPainter.Windows;

/// <summary>A small box over the game that shows how painting is going, and lets every click and key through.</summary>
public partial class StatusOverlayWindow : Window
{
    private PixelRect _area;

    public StatusOverlayWindow()
    {
        InitializeComponent();

        ScalingChanged += (_, _) => ApplyArea();
        Opened += (_, _) =>
        {
            ClickThrough.Apply(this);
            ApplyArea();
        };
    }

    /// <summary>Moves the window over a rectangle of the screen, in pixels.</summary>
    public void MoveTo(PixelRect area)
    {
        _area = area;
        ApplyArea();
    }

    private void ApplyArea()
    {
        if (_area.Width <= 0 || _area.Height <= 0)
            return;

        Position = _area.Position;

        double scale = RenderScaling <= 0 ? 1 : RenderScaling;
        Width = _area.Width / scale;
        Height = _area.Height / scale;
    }
}
