using RustyPainter.Imaging;
using RustyPainter.Interface;
using RustyPainter.Service;

namespace RustyPainter.Tests;

public class RgbTests
{
    [Fact]
    public void Reads_and_writes_hex()
    {
        Rgb color = Rgb.Parse("#FF8533");

        Assert.Equal(new Rgb(255, 133, 51), color);
        Assert.Equal("#FF8533", color.Hex);
        Assert.Equal(color, Rgb.Parse("ff8533"));
    }

    [Theory]
    [InlineData("#FFF")]
    [InlineData("#GGGGGG")]
    [InlineData("")]
    public void Rejects_bad_hex(string text)
    {
        Assert.ThrowsAny<FormatException>(() => Rgb.Parse(text));
    }
}

public class OrientTests
{
    private static PixelBuffer RedThenBlue()
    {
        PixelBuffer p = new(2, 1);
        p.Set(0, 0, 255, 0, 0);
        p.Set(1, 0, 0, 0, 255);
        return p;
    }

    private static bool IsRed(PixelBuffer p, int x, int y) => p.Data[p.OffsetOf(x, y)] == 255 && p.Data[p.OffsetOf(x, y) + 2] == 0;

    [Fact]
    public void Doing_nothing_returns_the_same_picture()
    {
        PixelBuffer source = RedThenBlue();
        Assert.Same(source, PlacementEngine.Orient(source, 0, false, false));
        Assert.Same(source, PlacementEngine.Orient(source, 4, false, false));
    }

    [Fact]
    public void A_quarter_turn_clockwise_puts_the_left_pixel_on_top()
    {
        PixelBuffer turned = PlacementEngine.Orient(RedThenBlue(), 1, false, false);

        Assert.Equal((1, 2), (turned.Width, turned.Height));
        Assert.True(IsRed(turned, 0, 0));
        Assert.False(IsRed(turned, 0, 1));
    }

    [Fact]
    public void Three_quarter_turns_put_the_left_pixel_at_the_bottom()
    {
        PixelBuffer turned = PlacementEngine.Orient(RedThenBlue(), 3, false, false);

        Assert.True(IsRed(turned, 0, 1));
    }

    [Fact]
    public void A_flip_swaps_left_and_right()
    {
        PixelBuffer flipped = PlacementEngine.Orient(RedThenBlue(), 0, true, false);

        Assert.True(IsRed(flipped, 1, 0));
        Assert.False(IsRed(flipped, 0, 0));
    }

    [Fact]
    public void A_vertical_flip_of_one_row_changes_nothing()
    {
        PixelBuffer flipped = PlacementEngine.Orient(RedThenBlue(), 0, false, true);

        Assert.True(IsRed(flipped, 0, 0));
    }

    [Fact]
    public void Two_turns_equal_flipping_both_ways()
    {
        PixelBuffer source = new(3, 2);
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 3; x++)
                source.Set(x, y, (byte)(x * 40), (byte)(y * 90), 7);

        PixelBuffer turned = PlacementEngine.Orient(source, 2, false, false);
        PixelBuffer flipped = PlacementEngine.Orient(source, 0, true, true);

        Assert.Equal(flipped.Data, turned.Data);
    }
}

public class ResampleTests
{
    private static PixelBuffer FourByTwo()
    {
        // Left half red, right half blue.
        PixelBuffer p = new(4, 2);
        for (int y = 0; y < 2; y++)
        {
            p.Set(0, y, 255, 0, 0);
            p.Set(1, y, 255, 0, 0);
            p.Set(2, y, 0, 0, 255);
            p.Set(3, y, 0, 0, 255);
        }

        return p;
    }

    private static (byte R, byte G, byte B, byte A) Cell(PixelBuffer p, int x, int y)
    {
        int o = p.OffsetOf(x, y);
        return (p.Data[o], p.Data[o + 1], p.Data[o + 2], p.Data[o + 3]);
    }

    [Fact]
    public void Fill_covers_the_grid_and_crops_the_sides()
    {
        PixelBuffer grid = PlacementEngine.Resample(FourByTwo(), new PlacementSettings(FitMode.Fill), 2, 2);

        Assert.Equal((255, 0, 0, 255), Cell(grid, 0, 0));
        Assert.Equal((0, 0, 255, 255), Cell(grid, 1, 0));
        Assert.Equal((255, 0, 0, 255), Cell(grid, 0, 1));
    }

    [Fact]
    public void Fit_keeps_the_whole_picture_and_leaves_the_rest_empty()
    {
        PixelBuffer grid = PlacementEngine.Resample(FourByTwo(), new PlacementSettings(FitMode.Fit), 4, 4);

        for (int x = 0; x < 4; x++)
        {
            Assert.Equal(0, Cell(grid, x, 0).A);
            Assert.Equal(255, Cell(grid, x, 1).A);
            Assert.Equal(255, Cell(grid, x, 2).A);
            Assert.Equal(0, Cell(grid, x, 3).A);
        }
    }

    [Fact]
    public void Stretch_fills_every_cell()
    {
        PixelBuffer grid = PlacementEngine.Resample(FourByTwo(), new PlacementSettings(FitMode.Stretch), 3, 5);

        for (int y = 0; y < 5; y++)
            for (int x = 0; x < 3; x++)
                Assert.Equal(255, Cell(grid, x, y).A);
    }

    [Fact]
    public void Moving_the_picture_shifts_what_is_shown()
    {
        // Half a canvas to the right brings the left (red) part of the picture into the second cell too.
        PixelBuffer grid = PlacementEngine.Resample(FourByTwo(), new PlacementSettings(FitMode.Fill, 1, 0.5, 0), 2, 2);

        Assert.Equal((255, 0, 0, 255), Cell(grid, 0, 0));
        Assert.Equal((255, 0, 0, 255), Cell(grid, 1, 0));
    }

    [Fact]
    public void Moving_it_far_enough_leaves_cells_empty()
    {
        PixelBuffer grid = PlacementEngine.Resample(FourByTwo(), new PlacementSettings(FitMode.Fit, 1, 1, 0), 4, 2);

        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 4; x++)
                Assert.Equal(0, Cell(grid, x, y).A);
    }

    [Fact]
    public void Zooming_in_shows_only_part_of_the_picture()
    {
        PixelBuffer quadrants = new(2, 2);
        quadrants.Set(0, 0, 255, 0, 0);
        quadrants.Set(1, 0, 0, 255, 0);
        quadrants.Set(0, 1, 0, 0, 255);
        quadrants.Set(1, 1, 255, 255, 255);

        PixelBuffer grid = PlacementEngine.Resample(quadrants, new PlacementSettings(FitMode.Fill, 2), 2, 2);

        // At double zoom the top left cell sits inside the red quadrant. Enlarging blends smoothly, so a little of the
        // neighbouring quadrants shows through, but red must clearly win.
        (byte r, byte g, byte b, _) = Cell(grid, 0, 0);
        Assert.True(r > g + 60 && r > b + 60, $"Expected mostly red, got {r},{g},{b}");

        // Without the zoom the same cell is an even mix of all four quadrants.
        PixelBuffer unzoomed = PlacementEngine.Resample(quadrants, new PlacementSettings(FitMode.Fill, 1), 2, 2);
        Assert.Equal((255, 0, 0, 255), Cell(unzoomed, 0, 0));
    }

    [Fact]
    public void Shrinking_averages_the_pixels_under_each_cell()
    {
        PixelBuffer checker = new(2, 2);
        checker.Set(0, 0, 0, 0, 0);
        checker.Set(1, 0, 255, 255, 255);
        checker.Set(0, 1, 255, 255, 255);
        checker.Set(1, 1, 0, 0, 0);

        PixelBuffer grid = PlacementEngine.Resample(checker, new PlacementSettings(FitMode.Stretch), 1, 1);

        (byte r, byte g, byte b, byte a) = Cell(grid, 0, 0);
        Assert.InRange(r, 126, 129);
        Assert.Equal(r, g);
        Assert.Equal(r, b);
        Assert.Equal(255, a);
    }

    [Fact]
    public void Enlarging_blends_smoothly()
    {
        PixelBuffer p = new(2, 1);
        p.Set(0, 0, 0, 0, 0);
        p.Set(1, 0, 255, 255, 255);

        PixelBuffer grid = PlacementEngine.Resample(p, new PlacementSettings(FitMode.Stretch), 4, 1);

        byte[] values = Enumerable.Range(0, 4).Select(x => Cell(grid, x, 0).R).ToArray();
        Assert.Equal(0, values[0]);
        Assert.Equal(255, values[3]);
        Assert.True(values[0] < values[1] && values[1] < values[2] && values[2] < values[3], string.Join(",", values));
    }

    [Fact]
    public void Transparent_pixels_stay_transparent()
    {
        PixelBuffer p = new(2, 1);
        p.Set(0, 0, 255, 0, 0);
        p.Set(1, 0, 0, 255, 0, 0);

        PixelBuffer grid = PlacementEngine.Resample(p, new PlacementSettings(FitMode.Stretch), 2, 1);

        Assert.Equal(255, Cell(grid, 0, 0).A);
        Assert.Equal(0, Cell(grid, 1, 0).A);
    }

    [Fact]
    public void A_transparent_neighbour_does_not_darken_the_edge()
    {
        PixelBuffer p = new(2, 1);
        p.Set(0, 0, 255, 0, 0);
        p.Set(1, 0, 0, 0, 0, 0);

        PixelBuffer grid = PlacementEngine.Resample(p, new PlacementSettings(FitMode.Stretch), 3, 1);

        // The middle cell is half over the red pixel; its color must stay full red, only its alpha drops.
        (byte r, _, _, byte a) = Cell(grid, 1, 0);
        Assert.Equal(255, r);
        Assert.InRange(a, 1, 254);
    }
}

public class AdjustmentTests
{
    private static PixelBuffer One(byte r, byte g, byte b, byte a = 255)
    {
        PixelBuffer p = new(1, 1);
        p.Set(0, 0, r, g, b, a);
        return p;
    }

    private static (int R, int G, int B) Rgb(PixelBuffer p) => (p.Data[0], p.Data[1], p.Data[2]);

    [Fact]
    public void Neutral_settings_return_the_same_picture()
    {
        PixelBuffer p = One(1, 2, 3);
        Assert.Same(p, Adjustments.Apply(p, new AdjustmentSettings()));
    }

    [Fact]
    public void Invert_flips_every_channel()
    {
        (int r, int g, int b) = Rgb(Adjustments.Apply(One(10, 20, 30), new AdjustmentSettings(Filter: ImageFilter.Invert)));

        Assert.InRange(r, 244, 246);
        Assert.InRange(g, 234, 236);
        Assert.InRange(b, 224, 226);
    }

    [Fact]
    public void Black_and_white_gives_grey()
    {
        (int r, int g, int b) = Rgb(Adjustments.Apply(One(255, 0, 0), new AdjustmentSettings(Filter: ImageFilter.BlackAndWhite)));

        Assert.Equal(r, g);
        Assert.Equal(g, b);
        Assert.InRange(r, 75, 77);
    }

    [Fact]
    public void Sepia_tints_towards_brown()
    {
        (int r, int g, int b) = Rgb(Adjustments.Apply(One(128, 128, 128), new AdjustmentSettings(Filter: ImageFilter.Sepia)));

        Assert.True(r > g && g > b, $"{r},{g},{b}");
    }

    [Fact]
    public void Brighten_lightens()
    {
        (int r, _, _) = Rgb(Adjustments.Apply(One(100, 100, 100), new AdjustmentSettings(Filter: ImageFilter.Brighten)));
        Assert.True(r > 100);
    }

    [Fact]
    public void Posterize_leaves_only_four_levels()
    {
        int[] allowed = [0, 85, 170, 255];

        foreach (byte value in new byte[] { 0, 30, 60, 100, 140, 200, 250 })
        {
            (int r, int g, int b) = Rgb(Adjustments.Apply(One(value, value, value), new AdjustmentSettings(Filter: ImageFilter.Posterize)));
            Assert.Contains(r, allowed);
            Assert.Equal(r, g);
            Assert.Equal(r, b);
        }
    }

    [Fact]
    public void Brightness_moves_every_channel()
    {
        (int up, _, _) = Rgb(Adjustments.Apply(One(100, 100, 100), new AdjustmentSettings(Brightness: 0.2)));
        (int down, _, _) = Rgb(Adjustments.Apply(One(100, 100, 100), new AdjustmentSettings(Brightness: -0.2)));

        Assert.InRange(up, 150, 152);
        Assert.InRange(down, 48, 50);
    }

    [Fact]
    public void Contrast_pushes_dark_darker_and_light_lighter()
    {
        AdjustmentSettings punchy = new(Contrast: 0.5);

        Assert.True(Rgb(Adjustments.Apply(One(100, 100, 100), punchy)).R < 100);
        Assert.True(Rgb(Adjustments.Apply(One(200, 200, 200), punchy)).R > 200);
    }

    [Fact]
    public void Losing_all_saturation_gives_grey()
    {
        (int r, int g, int b) = Rgb(Adjustments.Apply(One(200, 50, 50), new AdjustmentSettings(Saturation: -1)));

        Assert.Equal(r, g);
        Assert.Equal(g, b);
    }

    [Fact]
    public void Transparent_pixels_stay_transparent()
    {
        PixelBuffer result = Adjustments.Apply(One(200, 50, 50, 0), new AdjustmentSettings(Brightness: 0.5, Filter: ImageFilter.Invert));

        Assert.Equal(0, result.Data[3]);
    }

    [Fact]
    public void Out_of_range_values_are_kept_inside_the_byte_range()
    {
        (int r, int g, int b) = Rgb(Adjustments.Apply(One(250, 5, 128), new AdjustmentSettings(Brightness: 1, Contrast: 1, Saturation: 1)));

        Assert.InRange(r, 0, 255);
        Assert.InRange(g, 0, 255);
        Assert.InRange(b, 0, 255);
    }
}

public class QuantizerTests
{
    private static readonly Rgb[] BlackAndWhite = [new Rgb(0, 0, 0), new Rgb(255, 255, 255)];

    private static PixelBuffer Grey(int width, int height, byte value) => PixelBuffer.Solid(width, height, value, value, value);

    [Fact]
    public void Without_dithering_every_cell_takes_its_nearest_color()
    {
        int[] dark = Quantizer.Map(Grey(2, 2, 100), BlackAndWhite, Dithering.None);
        int[] light = Quantizer.Map(Grey(2, 2, 200), BlackAndWhite, Dithering.None);

        Assert.All(dark, index => Assert.Equal(0, index));
        Assert.All(light, index => Assert.Equal(1, index));
    }

    [Theory]
    [InlineData(Dithering.None)]
    [InlineData(Dithering.Smooth)]
    [InlineData(Dithering.Ordered)]
    public void Transparent_cells_are_left_unpainted(Dithering dithering)
    {
        PixelBuffer p = Grey(3, 1, 255);
        p.Set(1, 0, 255, 255, 255, 0);

        int[] result = Quantizer.Map(p, BlackAndWhite, dithering);

        Assert.Equal(Quantizer.Unpainted, result[1]);
        Assert.NotEqual(Quantizer.Unpainted, result[0]);
        Assert.NotEqual(Quantizer.Unpainted, result[2]);
    }

    [Theory]
    [InlineData(Dithering.None)]
    [InlineData(Dithering.Smooth)]
    [InlineData(Dithering.Ordered)]
    public void A_palette_color_always_maps_to_itself(Dithering dithering)
    {
        Rgb[] palette = [new Rgb(255, 0, 0), new Rgb(0, 255, 0), new Rgb(0, 0, 255), new Rgb(255, 255, 0)];
        PixelBuffer p = new(4, 1);
        for (int x = 0; x < 4; x++)
            p.Set(x, 0, palette[x].R, palette[x].G, palette[x].B);

        // Ordered dithering shifts colors a little, but never far enough to change a pure palette color here.
        Assert.Equal([0, 1, 2, 3], Quantizer.Map(p, palette, dithering));
    }

    [Fact]
    public void Smooth_dithering_mixes_black_and_white_to_look_like_mid_grey()
    {
        int[] result = Quantizer.Map(Grey(16, 16, 128), BlackAndWhite, Dithering.Smooth);

        double whiteShare = result.Count(index => index == 1) / (double)result.Length;
        Assert.InRange(whiteShare, 0.4, 0.6);
    }

    [Fact]
    public void Pattern_dithering_also_uses_both_colors()
    {
        int[] result = Quantizer.Map(Grey(16, 16, 128), BlackAndWhite, Dithering.Ordered);

        double whiteShare = result.Count(index => index == 1) / (double)result.Length;
        Assert.InRange(whiteShare, 0.3, 0.8);
    }

    [Fact]
    public void Pattern_dithering_repeats_every_eight_cells()
    {
        int[] result = Quantizer.Map(Grey(16, 16, 128), BlackAndWhite, Dithering.Ordered);

        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                Assert.Equal(result[y * 16 + x], result[y * 16 + x + 8]);
    }

    [Fact]
    public void Without_dithering_a_gradient_forms_bands()
    {
        PixelBuffer ramp = new(256, 1);
        for (int x = 0; x < 256; x++)
            ramp.Set(x, 0, (byte)x, (byte)x, (byte)x);

        int[] result = Quantizer.Map(ramp, BlackAndWhite, Dithering.None);

        // One switch from black to white and no flicker.
        int switches = 0;
        for (int x = 1; x < 256; x++)
            if (result[x] != result[x - 1]) switches++;

        Assert.Equal(1, switches);
    }

    [Fact]
    public void An_empty_palette_is_refused()
    {
        Assert.Throws<ArgumentException>(() => Quantizer.Map(Grey(1, 1, 0), [], Dithering.None));
    }

    [Fact]
    public void Picks_the_perceptually_nearest_color()
    {
        // A strong green is nearer to a dark green than to red or blue by eye.
        Rgb[] palette = [new Rgb(255, 0, 0), new Rgb(0, 100, 0), new Rgb(0, 0, 255)];
        PixelBuffer p = PixelBuffer.Solid(1, 1, 20, 160, 30);

        Assert.Equal(1, Quantizer.Map(p, palette, Dithering.None)[0]);
    }
}

public class MedianCutTests
{
    private static PixelBuffer Stripes(params Rgb[] colors)
    {
        PixelBuffer p = new(colors.Length * 10, 1);
        for (int i = 0; i < colors.Length; i++)
            for (int x = 0; x < 10; x++)
                p.Set(i * 10 + x, 0, colors[i].R, colors[i].G, colors[i].B);

        return p;
    }

    [Fact]
    public void Flat_colors_are_kept_exactly()
    {
        Rgb[] colors = [new Rgb(200, 30, 30), new Rgb(30, 200, 30), new Rgb(30, 30, 200)];

        List<Rgb> palette = MedianCut.BuildPalette(Stripes(colors), 3);

        Assert.Equal(colors.OrderBy(c => c.Hex), palette.OrderBy(c => c.Hex));
    }

    [Fact]
    public void Never_returns_more_colors_than_asked_for()
    {
        Rgb[] colors = [new Rgb(200, 30, 30), new Rgb(30, 200, 30), new Rgb(30, 30, 200)];

        Assert.Equal(2, MedianCut.BuildPalette(Stripes(colors), 2).Count);
    }

    [Fact]
    public void Returns_fewer_when_the_picture_has_fewer_colors()
    {
        Rgb[] colors = [new Rgb(200, 30, 30), new Rgb(30, 200, 30)];

        Assert.Equal(2, MedianCut.BuildPalette(Stripes(colors), 16).Count);
    }

    [Fact]
    public void A_picture_with_nothing_painted_gives_black()
    {
        PixelBuffer p = PixelBuffer.Solid(2, 2, 10, 20, 30, 0);

        Assert.Equal([new Rgb(0, 0, 0)], MedianCut.BuildPalette(p, 8));
    }

    [Fact]
    public void A_gradient_is_split_into_the_requested_number_of_greys()
    {
        PixelBuffer ramp = new(256, 1);
        for (int x = 0; x < 256; x++)
            ramp.Set(x, 0, (byte)x, (byte)x, (byte)x);

        List<Rgb> palette = MedianCut.BuildPalette(ramp, 4);

        Assert.Equal(4, palette.Count);
        Assert.Equal(4, palette.Select(c => c.R).Distinct().Count());
    }

    [Fact]
    public void One_color_asked_for_gives_the_average()
    {
        List<Rgb> palette = MedianCut.BuildPalette(Stripes(new Rgb(0, 0, 0), new Rgb(200, 200, 200)), 1);

        Assert.Single(palette);
        Assert.InRange(palette[0].R, 99, 101);
    }
}

public class PaintPlanTests
{
    private static readonly Rgb[] TwoColors = [new Rgb(255, 0, 0), new Rgb(0, 0, 255)];

    [Fact]
    public void Counts_cells_colors_and_strokes()
    {
        // 0 0 1 .
        // 1 1 . 0
        PaintPlan plan = new(4, 2, TwoColors, [0, 0, 1, -1, 1, 1, -1, 0]);

        Assert.Equal(6, plan.PaintedCells);
        Assert.Equal(2, plan.ColorsUsed);
        Assert.Equal(4, plan.StrokeCount);
    }

    [Fact]
    public void A_run_does_not_continue_onto_the_next_row()
    {
        PaintPlan plan = new(2, 2, TwoColors, [1, 1, 1, 1]);

        Assert.Equal(2, plan.StrokeCount);
    }

    [Fact]
    public void A_gap_splits_a_run()
    {
        PaintPlan plan = new(5, 1, TwoColors, [0, -1, 0, 0, -1]);

        Assert.Equal(2, plan.StrokeCount);
    }

    [Fact]
    public void Lists_only_the_colors_that_are_used_most_used_first()
    {
        Rgb[] palette = [new Rgb(1, 1, 1), new Rgb(2, 2, 2), new Rgb(3, 3, 3)];
        PaintPlan plan = new(4, 1, palette, [2, 2, 2, 0]);

        Assert.Equal([new Rgb(3, 3, 3), new Rgb(1, 1, 1)], plan.UsedColors);
    }

    [Fact]
    public void An_empty_plan_needs_nothing()
    {
        PaintPlan plan = new(2, 1, TwoColors, [-1, -1]);

        Assert.Equal(0, plan.PaintedCells);
        Assert.Equal(0, plan.StrokeCount);
        Assert.Equal(0, plan.ColorsUsed);
    }

    [Fact]
    public void Renders_painted_cells_opaque_and_the_rest_transparent()
    {
        PaintPlan plan = new(2, 1, TwoColors, [1, -1]);

        PixelBuffer image = plan.Render();

        Assert.Equal([0, 0, 255, 255, 0, 0, 0, 0], image.Data);
    }
}

public class StudioGeometryTests
{
    private static Surface Find(string id) => new SurfaceService().Catalog.Surfaces.Single(s => s.Id == id);

    [Fact]
    public void A_surface_with_a_known_texture_uses_it()
    {
        Surface medium = Find("sign-medium");

        Assert.True(StudioGeometry.IsTextureKnown(medium));
        Assert.Equal((512, 256), StudioGeometry.Texture(medium));
        Assert.Equal((512, 256), StudioGeometry.Grid(medium, 1));
        Assert.Equal((64, 32), StudioGeometry.Grid(medium, 8));
    }

    [Fact]
    public void Cells_round_up_so_the_whole_canvas_is_covered()
    {
        Assert.Equal((6, 3), StudioGeometry.Grid(Find("sign-medium"), 100));
        Assert.Equal((171, 86), StudioGeometry.Grid(Find("sign-medium"), 3));
    }

    [Fact]
    public void Other_surfaces_are_estimated_from_their_canvas_on_screen()
    {
        // A surface made up for the test, so this keeps working as more real surfaces are measured.
        Surface unmeasured = new("test", "Test", "standard", new ScreenRect(0, 0, 944, 471));

        Assert.False(StudioGeometry.IsTextureKnown(unmeasured));
        Assert.Equal((559, 279), StudioGeometry.Texture(unmeasured));
    }

    [Fact]
    public void The_brush_size_is_kept_inside_what_the_game_allows()
    {
        Surface medium = Find("sign-medium");

        Assert.Equal(StudioGeometry.Grid(medium, 1), StudioGeometry.Grid(medium, 0));
        Assert.Equal(StudioGeometry.Grid(medium, 100), StudioGeometry.Grid(medium, 5000));
    }
}

public class StudioPipelineTests
{
    private static readonly IReadOnlyList<Rgb> Swatches = new SurfaceService().Catalog.Layouts["standard"].Swatches.ColorGrid!.Colors.Select(Rgb.Parse).ToList();

    private static StudioSettings Settings(ColorMode mode = ColorMode.Swatches, FitMode fit = FitMode.Fill, ImageFilter filter = ImageFilter.None, Dithering dithering = Dithering.None, int maxColors = 16)
        => new(new PlacementSettings(fit), new AdjustmentSettings(Filter: filter), mode, maxColors, dithering);

    [Fact]
    public void A_swatch_colored_picture_maps_to_that_swatch()
    {
        Rgb red = new(255, 51, 51);
        PixelBuffer source = PixelBuffer.Solid(1, 1, red.R, red.G, red.B);

        PaintPlan plan = StudioPipeline.Run(source, Settings(), 3, 2, Swatches);

        Assert.Equal(6, plan.PaintedCells);
        Assert.Equal(1, plan.ColorsUsed);
        Assert.Equal(red, plan.UsedColors[0]);
        Assert.Equal(2, plan.StrokeCount);
        Assert.Equal(64, plan.Palette.Count);
    }

    private static StudioSettings WithBackground(Rgb background, ColorMode mode = ColorMode.Swatches, FitMode fit = FitMode.Fit)
        => new(new PlacementSettings(fit), new AdjustmentSettings(), mode, 16, Dithering.None, background);

    /// <summary>A 2 x 1 picture that is wider than a 2 x 2 grid, so fitting it leaves empty rows above and below.</summary>
    private static PixelBuffer WideRed() => PixelBuffer.Solid(4, 1, 255, 51, 51);

    [Fact]
    public void Without_a_background_the_empty_part_of_the_canvas_stays_unpainted()
    {
        PaintPlan plan = StudioPipeline.Run(WideRed(), Settings(fit: FitMode.Fit), 4, 4, Swatches);

        Assert.True(plan.PaintedCells < 16);
    }

    [Fact]
    public void A_background_paints_every_cell()
    {
        PaintPlan plan = StudioPipeline.Run(WideRed(), WithBackground(new Rgb(0, 0, 0)), 4, 4, Swatches);

        Assert.Equal(16, plan.PaintedCells);
    }

    [Fact]
    public void The_background_color_is_exact_in_free_mode()
    {
        Rgb background = new(12, 34, 56);

        PaintPlan plan = StudioPipeline.Run(WideRed(), WithBackground(background, ColorMode.Free), 4, 4, Swatches);

        Assert.Contains(background, plan.UsedColors);
        Assert.Equal(16, plan.PaintedCells);
    }

    [Fact]
    public void In_swatch_mode_the_background_takes_the_nearest_swatch()
    {
        Rgb swatch = Swatches[10];

        PaintPlan plan = StudioPipeline.Run(WideRed(), WithBackground(swatch), 4, 4, Swatches);

        Assert.Contains(swatch, plan.UsedColors);
    }

    [Fact]
    public void The_original_in_the_split_view_is_solid_on_the_background_color()
    {
        Rgb background = new(200, 10, 10);

        PaintPlan plan = StudioPipeline.Run(WideRed(), WithBackground(background), 4, 4, Swatches);

        PixelBuffer reference = plan.Reference!;
        for (int i = 3; i < reference.Data.Length; i += 4)
            Assert.Equal(255, reference.Data[i]);

        // The empty rows above the picture show the background, not a gap that the result could show through.
        Assert.Equal((200, 10, 10), (reference.Data[0], reference.Data[1], reference.Data[2]));
    }

    [Fact]
    public void A_filter_does_not_change_the_background_color()
    {
        Rgb background = new(255, 51, 51);
        StudioSettings settings = new(new PlacementSettings(FitMode.Fit), new AdjustmentSettings(Filter: ImageFilter.Invert), ColorMode.Free, 16, Dithering.None, background);

        PaintPlan plan = StudioPipeline.Run(WideRed(), settings, 4, 4, Swatches);

        // The picture is inverted but the empty rows keep the chosen color.
        Assert.Equal(background, plan.Palette[plan.Cells[0]]);
    }

    [Fact]
    public void Underlaying_blends_partly_transparent_pixels_and_fills_transparent_ones()
    {
        PixelBuffer picture = new(3, 1);
        picture.Set(0, 0, 200, 100, 50);
        picture.Set(1, 0, 200, 100, 50, 0);
        picture.Set(2, 0, 200, 100, 50, 128);

        PixelBuffer result = StudioPipeline.Underlay(picture, new Rgb(0, 0, 0));

        Assert.Equal((200, 100, 50, 255), (result.Data[0], result.Data[1], result.Data[2], result.Data[3]));
        Assert.Equal((0, 0, 0, 255), (result.Data[4], result.Data[5], result.Data[6], result.Data[7]));
        Assert.InRange(result.Data[8], 99, 101);
        Assert.Equal(255, result.Data[11]);
    }

    [Fact]
    public void Free_mode_keeps_the_exact_color_of_a_flat_picture()
    {
        PixelBuffer source = PixelBuffer.Solid(2, 2, 200, 30, 30);

        PaintPlan plan = StudioPipeline.Run(source, Settings(ColorMode.Free), 4, 4, Swatches);

        Assert.Equal([new Rgb(200, 30, 30)], plan.UsedColors);
    }

    [Fact]
    public void Free_mode_respects_the_color_limit()
    {
        PixelBuffer ramp = new(64, 1);
        for (int x = 0; x < 64; x++)
            ramp.Set(x, 0, (byte)(x * 4), (byte)(255 - x * 4), (byte)(x * 2));

        PaintPlan plan = StudioPipeline.Run(ramp, Settings(ColorMode.Free, maxColors: 5), 64, 1, Swatches);

        Assert.True(plan.ColorsUsed <= 5);
        Assert.True(plan.ColorsUsed >= 2);
    }

    [Fact]
    public void Fit_leaves_the_empty_part_of_the_canvas_unpainted()
    {
        PixelBuffer source = PixelBuffer.Solid(4, 2, 0, 0, 0);

        PaintPlan plan = StudioPipeline.Run(source, Settings(fit: FitMode.Fit), 4, 4, Swatches);

        Assert.Equal(8, plan.PaintedCells);
        Assert.Equal(Quantizer.Unpainted, plan.Cells[0]);
        Assert.Equal(Quantizer.Unpainted, plan.Cells[15]);
    }

    [Fact]
    public void Filters_run_before_the_colors_are_matched()
    {
        // White inverted is black, and black is the first swatch.
        PixelBuffer source = PixelBuffer.Solid(1, 1, 255, 255, 255);

        PaintPlan plan = StudioPipeline.Run(source, Settings(filter: ImageFilter.Invert), 2, 2, Swatches);

        Assert.Equal([new Rgb(0, 0, 0)], plan.UsedColors);
    }

    [Fact]
    public void Dithering_makes_a_smooth_picture_use_more_swatches_than_none()
    {
        PixelBuffer ramp = new(64, 8);
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 64; x++)
                ramp.Set(x, y, (byte)(x * 4), (byte)(100 + x), 60);

        PaintPlan flat = StudioPipeline.Run(ramp, Settings(dithering: Dithering.None), 64, 8, Swatches);
        PaintPlan dithered = StudioPipeline.Run(ramp, Settings(dithering: Dithering.Smooth), 64, 8, Swatches);

        Assert.True(dithered.StrokeCount > flat.StrokeCount, $"{dithered.StrokeCount} vs {flat.StrokeCount}");
    }
}
