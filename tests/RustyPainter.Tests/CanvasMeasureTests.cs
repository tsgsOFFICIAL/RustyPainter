using RustyPainter.Imaging;

namespace RustyPainter.Tests;

public class CanvasMeasureTests
{
    private static PixelBuffer Canvas(int width, int height, byte grey = 120)
    {
        PixelBuffer canvas = new(width, height);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                canvas.Set(x, y, grey, grey, grey);

        return canvas;
    }

    private static void Square(PixelBuffer picture, int x, int y, int size, byte value = 0)
    {
        for (int j = y; j < y + size; j++)
            for (int i = x; i < x + size; i++)
                picture.Set(i, j, value, value, value);
    }

    [Fact]
    public void Finds_what_was_painted_between_two_pictures()
    {
        PixelBuffer before = Canvas(100, 60);
        PixelBuffer after = Canvas(100, 60);
        Square(after, 20, 10, 12);

        bool[] changed = CanvasMeasure.Changed(before, after);

        PixelBox box = CanvasMeasure.Bounds(changed, 100)!.Value;
        Assert.Equal((20, 10, 31, 21), (box.X1, box.Y1, box.X2, box.Y2));
        Assert.Equal(144, box.Pixels);
        Assert.Equal((12, 12), (box.Width, box.Height));
    }

    [Fact]
    public void A_little_shimmer_is_not_a_change()
    {
        PixelBuffer before = Canvas(50, 50, 120);
        PixelBuffer after = Canvas(50, 50, 130);

        Assert.Null(CanvasMeasure.Bounds(CanvasMeasure.Changed(before, after), 50));
    }

    [Fact]
    public void Paint_that_is_close_to_the_canvas_color_is_still_found_when_it_differs_enough()
    {
        PixelBuffer before = Canvas(50, 50, 120);
        PixelBuffer after = Canvas(50, 50, 120);
        Square(after, 10, 10, 5, 70);

        Assert.NotNull(CanvasMeasure.Bounds(CanvasMeasure.Changed(before, after), 50));
    }

    [Fact]
    public void Pictures_of_different_sizes_cannot_be_compared()
    {
        Assert.Throws<ArgumentException>(() => CanvasMeasure.Changed(Canvas(10, 10), Canvas(10, 11)));
    }

    [Fact]
    public void Nothing_painted_gives_no_bounds()
    {
        Assert.Null(CanvasMeasure.Bounds(new bool[100], 10));
    }

    [Fact]
    public void Separate_dots_are_found_left_to_right_and_noise_is_dropped()
    {
        PixelBuffer before = Canvas(200, 60);
        PixelBuffer after = Canvas(200, 60);
        Square(after, 120, 20, 20);
        Square(after, 10, 20, 8);
        Square(after, 60, 20, 12);
        after.Set(190, 5, 0, 0, 0);   // a single stray pixel

        IReadOnlyList<PixelBox> blobs = CanvasMeasure.Blobs(CanvasMeasure.Changed(before, after), 200);

        Assert.Equal([8, 12, 20], blobs.Select(blob => blob.Width));
        Assert.Equal([8, 12, 20], blobs.Select(blob => blob.Height));
    }

    [Fact]
    public void Touching_squares_are_one_blob()
    {
        PixelBuffer before = Canvas(100, 40);
        PixelBuffer after = Canvas(100, 40);
        Square(after, 10, 10, 10);
        Square(after, 20, 10, 10);

        IReadOnlyList<PixelBox> blobs = CanvasMeasure.Blobs(CanvasMeasure.Changed(before, after), 100);

        PixelBox only = Assert.Single(blobs);
        Assert.Equal(20, only.Width);
    }

    [Fact]
    public void Works_out_pixels_per_brush_unit_for_width_and_height_separately()
    {
        PixelBox dot = new(100, 100, 140, 105, 0);   // 41 wide, 6 tall: not square on purpose

        (double x, double y) = CanvasMeasure.PixelsPerUnit(dot, 16);

        Assert.Equal(41 / 16.0, x, 6);
        Assert.Equal(6 / 16.0, y, 6);
    }

    [Fact]
    public void The_area_a_big_fill_covers_is_the_canvas()
    {
        PixelBuffer before = Canvas(300, 200);
        PixelBuffer after = Canvas(300, 200);
        for (int y = 30; y < 170; y++)
            for (int x = 40; x < 260; x++)
                after.Set(x, y, 0, 0, 0);

        PixelBox area = CanvasMeasure.Bounds(CanvasMeasure.Changed(before, after), 300)!.Value;

        Assert.Equal((40, 30, 259, 169), (area.X1, area.Y1, area.X2, area.Y2));
        Assert.Equal(220 * 140, area.Pixels);
    }
}
