using System.Numerics;
using RustyPainter.ViewModels;

namespace RustyPainter.Tests;

public class PlacementGestureTests
{
    private static PlacementGesture Started(double zoom = 1, double offsetX = 0, double offsetY = 0)
    {
        PlacementGesture gesture = new();
        gesture.Begin(800, 400, zoom, offsetX, offsetY);
        return gesture;
    }

    private static Vector2 Apply(PlacementGesture gesture, float x, float y) => Vector2.Transform(new Vector2(x, y), gesture.Matrix);

    [Fact]
    public void Does_nothing_before_it_starts()
    {
        PlacementGesture gesture = new();

        gesture.Pan(10, 10);
        gesture.ZoomBy(2);

        Assert.False(gesture.IsActive);
        Assert.False(gesture.HasUncommitted);
        Assert.Equal(Matrix3x2.Identity, gesture.Matrix);
    }

    [Fact]
    public void Panning_moves_the_picture_by_the_same_amount()
    {
        PlacementGesture gesture = Started();

        gesture.Pan(40, -20);

        Assert.Equal(new Vector2(140, 80), Apply(gesture, 100, 100));
        Assert.Equal(5, gesture.OffsetXPercent, 6);
        Assert.Equal(-5, gesture.OffsetYPercent, 6);
        Assert.True(gesture.HasUncommitted);
    }

    [Fact]
    public void Zooming_keeps_the_middle_of_the_picture_still()
    {
        PlacementGesture gesture = Started();

        gesture.ZoomBy(2);

        Assert.Equal(new Vector2(400, 200), Apply(gesture, 400, 200));
        Assert.Equal(new Vector2(500, 200), Apply(gesture, 450, 200));
        Assert.Equal(2, gesture.Zoom);
    }

    [Fact]
    public void Zooming_keeps_a_shifted_picture_middle_still()
    {
        // Shifted right by a quarter of the canvas width, so its middle is at x = 600.
        PlacementGesture gesture = Started(offsetX: 25);

        gesture.ZoomBy(2);

        Assert.Equal(600, Apply(gesture, 600, 200).X, 3);
    }

    [Fact]
    public void Panning_and_zooming_combine()
    {
        PlacementGesture gesture = Started();

        gesture.ZoomBy(2);
        gesture.Pan(100, 0);

        Assert.Equal(new Vector2(500, 200), Apply(gesture, 400, 200));
    }

    [Fact]
    public void The_limits_of_the_settings_are_respected()
    {
        PlacementGesture gesture = Started(zoom: 2, offsetX: 90);

        gesture.Pan(100000, -100000);
        gesture.ZoomBy(100);

        Assert.Equal(100, gesture.OffsetXPercent);
        Assert.Equal(-100, gesture.OffsetYPercent);
        Assert.Equal(PlacementGesture.MaxZoom, gesture.Zoom);

        gesture.ZoomBy(0.0001);
        Assert.Equal(PlacementGesture.MinZoom, gesture.Zoom);
    }

    [Fact]
    public void Beginning_again_keeps_the_first_picture_as_the_start()
    {
        PlacementGesture gesture = Started();
        gesture.Pan(80, 0);

        // The mouse goes down again before the new render arrived, so the picture on screen is still the old one.
        gesture.Begin(800, 400, 1, 10, 0);
        gesture.Pan(80, 0);

        Assert.Equal(new Vector2(260, 100), Apply(gesture, 100, 100));
    }

    [Fact]
    public void A_new_render_leaves_only_the_remaining_movement()
    {
        PlacementGesture gesture = Started();
        gesture.Pan(80, 0);
        gesture.MarkCommitted();

        // The render for the first 10 percent arrives while the user keeps dragging.
        gesture.Rebase(1, 10, 0);
        Assert.Equal(new Vector2(100, 100), Apply(gesture, 100, 100));

        gesture.Pan(40, 0);
        Assert.Equal(new Vector2(140, 100), Apply(gesture, 100, 100));
        Assert.Equal(15, gesture.OffsetXPercent, 6);
    }

    [Fact]
    public void Resetting_puts_the_picture_back()
    {
        PlacementGesture gesture = Started();
        gesture.Pan(50, 50);
        gesture.ZoomBy(2);

        gesture.Reset();

        Assert.False(gesture.IsActive);
        Assert.Equal(Matrix3x2.Identity, gesture.Matrix);
    }

    [Fact]
    public void Committing_says_nothing_is_left_to_hand_over()
    {
        PlacementGesture gesture = Started();
        gesture.Pan(5, 5);
        Assert.True(gesture.HasUncommitted);

        gesture.MarkCommitted();

        Assert.False(gesture.HasUncommitted);
        Assert.True(gesture.IsActive);
    }
}
