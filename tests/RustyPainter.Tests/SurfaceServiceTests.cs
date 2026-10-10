using RustyPainter.Interface;
using RustyPainter.Service;

namespace RustyPainter.Tests;

public class SurfaceServiceTests
{
    private static readonly SurfaceService Service = new();
    private static SurfaceCatalog Catalog => Service.Catalog;
    private static LayoutFamily Standard => Catalog.Layouts["standard"];

    private static Surface Find(string id) => Catalog.Surfaces.Single(s => s.Id == id);

    private static void AssertRect(ScreenRect actual, double x1, double y1, double x2, double y2)
    {
        Assert.Equal(x1, actual.X1, 3);
        Assert.Equal(y1, actual.Y1, 3);
        Assert.Equal(x2, actual.X2, 3);
        Assert.Equal(y2, actual.Y2, 3);
    }

    private static SurfaceCatalog WithLayouts(LayoutFamily family) => Catalog with { Layouts = new() { ["standard"] = family } };

    // ---- data ----

    [Fact]
    public void Loads_the_embedded_catalog()
    {
        Assert.Equal(2560, Catalog.ReferenceWidth);
        Assert.Equal(1440, Catalog.ReferenceHeight);
        Assert.Equal(16, Catalog.Surfaces.Length);
        Assert.Contains("standard", Catalog.Layouts.Keys);
        Assert.Contains("neon", Catalog.Layouts.Keys);
        Assert.Contains("animated", Catalog.Layouts.Keys);
    }

    [Fact]
    public void Standard_surfaces_have_64_distinct_swatch_colors()
    {
        string[] colors = Standard.Swatches.ColorGrid!.Colors;

        Assert.Equal(64, colors.Length);
        Assert.Equal(64, colors.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Swatch_9_is_the_value_confirmed_in_game()
    {
        // The old app had #5A6629 here, a duplicate of swatch 13. The game itself shows #665A29.
        string[] colors = Standard.Swatches.ColorGrid!.Colors;

        Assert.Equal("#665A29", colors[9]);
        Assert.Equal("#5A6629", colors[13]);
    }

    [Fact]
    public void Neon_signs_have_eight_swatches_in_two_rows_of_four()
    {
        ColorGrid grid = Catalog.Layouts["neon"].Swatches.ColorGrid!;

        Assert.Equal(4, grid.Columns);
        Assert.Equal(2, grid.Rows);
        Assert.Equal(8, grid.Colors.Length);
        Assert.Equal(2081, grid.CellCenter(0).X, 0);
        Assert.Equal(741, grid.CellCenter(0).Y, 0);
        Assert.Equal(2469, grid.CellCenter(7).X, 0);
        Assert.Equal(869, grid.CellCenter(7).Y, 0);
    }

    [Fact]
    public void Neon_swatch_and_free_panels_are_almost_at_the_same_height()
    {
        // Unlike the standard family, the neon swatch panel is about as tall as the free picker panel.
        LayoutFamily neon = Catalog.Layouts["neon"];

        Assert.InRange(neon.Swatches.ToolBrush.Y1 - neon.Free.ToolBrush.Y1, -8, 8);
        Assert.InRange(neon.Swatches.ColorModeToggle.Y1 - neon.Free.ColorModeToggle.Y1, -8, 8);
    }

    [Fact]
    public void Canvases_that_open_cropped_need_scrolling_out_first()
    {
        Assert.Equal(6, Find("canvas-small").ScrollOut);
        Assert.Equal(11, Find("canvas-xxl").ScrollOut);
        Assert.All(Catalog.Surfaces.Where(s => s.Id is not ("canvas-small" or "canvas-xxl")), s => Assert.Equal(0, s.ScrollOut));
    }

    [Theory]
    [InlineData("canvas-small", 550, 132, 1434, 1308)]
    [InlineData("canvas-xxl", 137, 292, 1850, 1149)]
    [InlineData("neon-large", 544, 236, 1435, 1126)]
    [InlineData("neon-medium", 545, 362, 1439, 881)]
    [InlineData("neon-small", 730, 360, 1252, 883)]
    public void Measured_canvases_are_where_the_screenshots_put_them(string id, double x1, double y1, double x2, double y2)
    {
        AssertRect(Find(id).Canvas, x1, y1, x2, y2);
    }

    [Fact]
    public void Swatch_layout_keeps_the_old_apps_recorded_positions()
    {
        PaintLayout sw = Standard.Swatches;
        AssertRect(sw.ToolBrush, 2180, 103, 2235, 158);
        AssertRect(sw.BrushShapes[2], 2180, 282, 2235, 337);
        AssertRect(sw.Size!.Value, 2448, 358, 2527, 397);
        AssertRect(sw.Spacing!.Value, 2448, 414, 2527, 453);
        AssertRect(sw.Opacity!.Value, 2448, 470, 2527, 509);
        AssertRect(sw.ResetCamera, 672, 32, 767, 127);
        AssertRect(sw.ColorGrid!.Area, 2016, 625, 2527, 1203);
    }

    [Fact]
    public void Free_mode_panel_sits_133px_lower_than_swatch_mode_on_standard_surfaces()
    {
        LayoutFamily f = Standard;
        (ScreenRect swatch, ScreenRect free)[] pairs =
        [
            (f.Swatches.ToolBrush, f.Free.ToolBrush),
            (f.Swatches.ToolEraser, f.Free.ToolEraser),
            (f.Swatches.ToolPicker, f.Free.ToolPicker),
            (f.Swatches.BrushShapes[0], f.Free.BrushShapes[0]),
            (f.Swatches.BrushShapes[^1], f.Free.BrushShapes[^1]),
            (f.Swatches.ColorModeToggle, f.Free.ColorModeToggle)
        ];

        foreach ((ScreenRect swatch, ScreenRect free) in pairs)
        {
            Assert.InRange(free.Y1 - swatch.Y1, 131, 135);
            Assert.Equal(swatch.X1, free.X1, 3);
        }
    }

    [Fact]
    public void Toolbar_and_bottom_buttons_do_not_move_between_modes()
    {
        foreach (LayoutFamily f in Catalog.Layouts.Values)
        {
            Assert.Equal(f.Swatches.Clear, f.Free.Clear);
            Assert.Equal(f.Swatches.Undo, f.Free.Undo);
            Assert.Equal(f.Swatches.SaveChanges, f.Free.SaveChanges);
            Assert.Equal(f.Swatches.Cancel, f.Free.Cancel);
        }
    }

    [Fact]
    public void Color_mode_toggle_matches_where_the_game_screenshot_shows_it()
    {
        ScreenPoint center = Standard.Swatches.ColorModeToggle.Center;
        Assert.InRange(center.X, 2506, 2514);
        Assert.InRange(center.Y, 571, 579);
    }

    [Fact]
    public void Neon_signs_only_have_brush_presets()
    {
        PaintLayout free = Catalog.Layouts["neon"].Free;
        Assert.Equal(4, free.BrushShapes.Length);
        Assert.Null(free.Size);
        Assert.Null(free.Spacing);
        Assert.Null(free.Opacity);
    }

    [Fact]
    public void Color_grid_cells_are_where_they_should_be()
    {
        ColorGrid grid = Standard.Swatches.ColorGrid!;
        Assert.Equal(64, grid.Count);
        Assert.Equal(2079, grid.CellCenter(0).X, 3);
        Assert.Equal(642.5, grid.CellCenter(0).Y, 3);
        Assert.Equal(2463, grid.CellCenter(63).X, 3);
        Assert.Equal(1182.5, grid.CellCenter(63).Y, 3);
        Assert.True(grid.CellCenter(63).Y < grid.Area.Y2);
    }

    // ---- scaling ----

    [Fact]
    public void Reference_resolution_leaves_the_canvas_unchanged()
    {
        AssertRect(Service.Resolve(Find("sign-small"), 2560, 1440).Canvas, 669, 537, 1317, 880);
    }

    [Fact]
    public void Scales_canvas_and_controls_to_1080p()
    {
        ResolvedLayout hd = Service.Resolve(Find("sign-small"), 1920, 1080);
        AssertRect(hd.Canvas, 501.75, 402.75, 987.75, 660);
        Assert.Equal(2463 * 0.75, hd.Swatches.ColorGrid!.CellCenter(63).X, 3);
        Assert.Equal(1182.5 * 0.75, hd.Swatches.ColorGrid.CellCenter(63).Y, 3);
        Assert.Equal(2419 * 0.75, hd.Free.FreeColor!.ColorHex.X1, 3);
    }

    [Fact]
    public void Moves_everything_to_where_the_window_sits()
    {
        GameWindow window = new(1, 1, new ScreenRect(100, 50, 1060, 590), false, true);   // 960x540, 0.375 of the reference
        ResolvedLayout layout = Service.Resolve(Find("sign-small"), window);

        Assert.Equal(100 + 669 * 0.375, layout.Canvas.X1, 3);
        Assert.Equal(50 + 537 * 0.375, layout.Canvas.Y1, 3);
        Assert.Equal(100 + 2079 * 0.375, layout.Swatches.ColorGrid!.CellCenter(0).X, 3);
        Assert.Equal(50 + 906 * 0.375, layout.Free.FreeColor!.AddColor.Y2, 3);
    }

    [Fact]
    public void Refuses_a_minimized_window()
    {
        GameWindow window = new(1, 1, new ScreenRect(0, 0, 0, 0), true, false);
        Assert.Throws<ArgumentException>(() => Service.Resolve(Find("sign-small"), window));
    }

    [Fact]
    public void Refuses_a_resolution_without_size()
    {
        Assert.Throws<ArgumentException>(() => Service.Resolve(Find("sign-small"), 0, 1080));
    }

    // ---- validation ----

    private static SurfaceCatalog WithColors(string[] colors)
        => WithLayouts(Standard with { Swatches = Standard.Swatches with { ColorGrid = Standard.Swatches.ColorGrid! with { Colors = colors } } });

    [Fact]
    public void Rejects_a_duplicate_palette_color()
    {
        string[] colors = (string[])Standard.Swatches.ColorGrid!.Colors.Clone();
        colors[9] = colors[13];
        Assert.Contains("repeats", Assert.Throws<InvalidDataException>(() => new SurfaceService(WithColors(colors))).Message);
    }

    [Fact]
    public void Rejects_a_palette_that_does_not_match_the_grid()
    {
        string[] colors = Standard.Swatches.ColorGrid!.Colors[..60];
        Assert.Contains("palette has 60", Assert.Throws<InvalidDataException>(() => new SurfaceService(WithColors(colors))).Message);
    }

    [Fact]
    public void Rejects_a_malformed_color()
    {
        string[] colors = [.. Standard.Swatches.ColorGrid!.Colors[..63], "#12345"];
        Assert.Contains("not #RRGGBB", Assert.Throws<InvalidDataException>(() => new SurfaceService(WithColors(colors))).Message);
    }

    [Fact]
    public void Rejects_a_swatch_layout_without_a_grid()
    {
        SurfaceCatalog bad = WithLayouts(Standard with { Swatches = Standard.Swatches with { ColorGrid = null } });
        Assert.Contains("no color grid", Assert.Throws<InvalidDataException>(() => new SurfaceService(bad)).Message);
    }

    [Fact]
    public void Rejects_a_free_layout_without_free_controls()
    {
        SurfaceCatalog bad = WithLayouts(Standard with { Free = Standard.Free with { FreeColor = null } });
        Assert.Contains("free color controls", Assert.Throws<InvalidDataException>(() => new SurfaceService(bad)).Message);
    }

    [Fact]
    public void Rejects_a_control_without_area()
    {
        SurfaceCatalog bad = WithLayouts(Standard with { Free = Standard.Free with { Clear = new ScreenRect(5, 5, 5, 5) } });
        Assert.Contains("no area", Assert.Throws<InvalidDataException>(() => new SurfaceService(bad)).Message);
    }

    [Fact]
    public void Rejects_a_control_off_screen()
    {
        SurfaceCatalog bad = WithLayouts(Standard with { Swatches = Standard.Swatches with { Cancel = new ScreenRect(2000, 1300, 2700, 1400) } });
        Assert.Contains("outside", Assert.Throws<InvalidDataException>(() => new SurfaceService(bad)).Message);
    }

    [Fact]
    public void Rejects_a_layout_without_brush_shapes()
    {
        SurfaceCatalog bad = WithLayouts(Standard with { Swatches = Standard.Swatches with { BrushShapes = [] } });
        Assert.Contains("no brush shapes", Assert.Throws<InvalidDataException>(() => new SurfaceService(bad)).Message);
    }

    [Fact]
    public void Rejects_an_unknown_layout()
    {
        SurfaceCatalog bad = Catalog with { Surfaces = [Catalog.Surfaces[0] with { Layout = "nope" }] };
        Assert.Contains("unknown layout", Assert.Throws<InvalidDataException>(() => new SurfaceService(bad)).Message);
    }

    [Fact]
    public void Rejects_a_canvas_without_area()
    {
        SurfaceCatalog bad = Catalog with { Surfaces = [Catalog.Surfaces[0] with { Canvas = new ScreenRect(500, 500, 400, 600) }] };
        Assert.Contains("no area", Assert.Throws<InvalidDataException>(() => new SurfaceService(bad)).Message);
    }

    [Fact]
    public void Rejects_a_canvas_off_screen()
    {
        SurfaceCatalog bad = Catalog with { Surfaces = [Catalog.Surfaces[0] with { Canvas = new ScreenRect(100, 100, 3000, 600) }] };
        Assert.Contains("outside", Assert.Throws<InvalidDataException>(() => new SurfaceService(bad)).Message);
    }

    [Fact]
    public void Rejects_repeated_surface_ids()
    {
        SurfaceCatalog bad = Catalog with { Surfaces = [Catalog.Surfaces[0], Catalog.Surfaces[0]] };
        Assert.Contains("more than once", Assert.Throws<InvalidDataException>(() => new SurfaceService(bad)).Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(41)]
    public void Rejects_an_unreasonable_scroll_count(int scrollOut)
    {
        SurfaceCatalog bad = Catalog with { Surfaces = [Catalog.Surfaces[0] with { ScrollOut = scrollOut }] };
        Assert.Contains("scroll count", Assert.Throws<InvalidDataException>(() => new SurfaceService(bad)).Message);
    }
}
