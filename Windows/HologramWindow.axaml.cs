using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace RustyPainter.Windows;

/// <summary>A see-through window with a picture in it that lets every click and key through to the game below it.</summary>
public partial class HologramWindow : Window
{
    private PixelRect _area;

    public HologramWindow()
    {
        InitializeComponent();

        // The window is measured in device independent units, so its size has to follow the scale of the screen it is on.
        ScalingChanged += (_, _) => ApplyArea();
        Opened += (_, _) =>
        {
            ClickThrough.Apply(this);
            ApplyArea();
        };
    }

    /// <summary>The picture that is shown.</summary>
    public Bitmap? Image
    {
        get => Picture.Source as Bitmap;
        set => Picture.Source = value;
    }

    /// <summary>How solid the picture is, from 0 to 1.</summary>
    public double PictureOpacity
    {
        get => Picture.Opacity;
        set => Picture.Opacity = value;
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
