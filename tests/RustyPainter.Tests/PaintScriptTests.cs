using RustyPainter.Imaging;
using RustyPainter.Interface;
using RustyPainter.Painting;
using RustyPainter.Service;

namespace RustyPainter.Tests;

public class PaintScriptTests
{
    private static readonly SurfaceService Surfaces = new();

    private static Surface Find(string id) => Surfaces.Catalog.Surfaces.Single(surface => surface.Id == id);

    private static ResolvedLayout Layout(string id, int width = 2560, int height = 1440) => Surfaces.Resolve(Find(id), width, height);

    /// <summary>A small picture: a red row on top of a green row, with one blue dot.</summary>
    private static PaintPlan SmallPlan(IReadOnlyList<Rgb> palette)
        => new(4, 2, palette, [0, 0, 0, 0, 1, 1, 1, 2]);

    private static IReadOnlyList<Rgb> SwatchPalette(string id)
        => Layout(id).Swatches.ColorGrid!.Colors.Select(Rgb.Parse).ToList();

    [Fact]
    public void Starts_by_setting_up_the_game_and_ends_with_the_strokes()
    {
        ResolvedLayout layout = Layout("sign-medium");
        PaintScript script = PaintScriptBuilder.Build(SmallPlan(SwatchPalette("sign-medium")), layout, new PaintOptions(ColorMode.Swatches, 4));

        Assert.IsType<EnsureColorModeStep>(script.Steps[0]);
        Assert.Equal("Brush", Assert.IsType<ClickControlStep>(script.Steps[1]).Control);
        Assert.Equal("Clear", Assert.IsType<ClickControlStep>(script.Steps[2]).Control);
        Assert.Equal("Brush shape", Assert.IsType<ClickControlStep>(script.Steps[3]).Control);

        // Brush values, then a color and its strokes, repeated for each color.
        Assert.Equal(["Size", "Spacing", "Opacity"], script.Steps.OfType<SetBrushValueStep>().Select(step => step.Field));
        Assert.IsType<SelectSwatchStep>(script.Steps[7]);
        Assert.IsType<PaintStrokeStep>(script.Steps[8]);
        Assert.IsType<PaintStrokeStep>(script.Steps[^1]);
    }

    [Fact]
    public void Types_the_brush_values()
    {
        ResolvedLayout layout = Layout("sign-medium");
        PaintScript script = PaintScriptBuilder.Build(SmallPlan(SwatchPalette("sign-medium")), layout,
            new PaintOptions(ColorMode.Swatches, 8, Spacing: 0.05, Opacity: 1));

        Dictionary<string, SetBrushValueStep> values = script.Steps.OfType<SetBrushValueStep>().ToDictionary(step => step.Field);

        Assert.Equal("8", values["Size"].Text);
        Assert.Equal("0.05", values["Spacing"].Text);
        Assert.Equal("1", values["Opacity"].Text);
        Assert.Equal(layout.Swatches.Size!.Value.Center, values["Size"].At);
    }

    [Fact]
    public void Uses_the_hard_square_brush_by_default()
    {
        ResolvedLayout layout = Layout("sign-medium");
        PaintScript script = PaintScriptBuilder.Build(SmallPlan(SwatchPalette("sign-medium")), layout, new PaintOptions(ColorMode.Swatches, 4));

        ClickControlStep shape = script.Steps.OfType<ClickControlStep>().Single(step => step.Control == "Brush shape");

        Assert.Equal(layout.Swatches.BrushShapes[3].Center, shape.At);
    }

    [Fact]
    public void Can_skip_clearing_the_canvas()
    {
        ResolvedLayout layout = Layout("sign-medium");
        PaintScript script = PaintScriptBuilder.Build(SmallPlan(SwatchPalette("sign-medium")), layout, new PaintOptions(ColorMode.Swatches, 4, ClearFirst: false));

        Assert.DoesNotContain(script.Steps.OfType<ClickControlStep>(), step => step.Control == "Clear");
    }

    [Fact]
    public void Swatch_mode_clicks_the_swatch_of_each_color()
    {
        ResolvedLayout layout = Layout("sign-medium");
        IReadOnlyList<Rgb> palette = SwatchPalette("sign-medium");
        PaintScript script = PaintScriptBuilder.Build(SmallPlan(palette), layout, new PaintOptions(ColorMode.Swatches, 4));

        List<SelectSwatchStep> picks = script.Steps.OfType<SelectSwatchStep>().ToList();

        // Red has the most cells, then green, then blue, and each color is picked once.
        Assert.Equal([0, 1, 2], picks.Select(step => step.Index));
        Assert.Equal(layout.Swatches.ColorGrid!.CellCenter(1), picks[1].At);
        Assert.Equal(palette[2], picks[2].Color);
        Assert.Equal(3, script.ColorChanges);
    }

    [Fact]
    public void Free_mode_types_each_color_into_the_hex_field_of_the_free_layout()
    {
        ResolvedLayout layout = Layout("sign-medium");
        Rgb[] palette = [new Rgb(10, 20, 30), new Rgb(200, 100, 50), new Rgb(1, 2, 3)];
        PaintScript script = PaintScriptBuilder.Build(SmallPlan(palette), layout, new PaintOptions(ColorMode.Free, 4));

        List<TypeColorStep> typed = script.Steps.OfType<TypeColorStep>().ToList();

        Assert.Equal(3, typed.Count);
        Assert.Equal(palette[0], typed[0].Color);
        Assert.All(typed, step => Assert.Equal(layout.Free.FreeColor!.ColorHex.Center, step.At));
        Assert.Empty(script.Steps.OfType<SelectSwatchStep>());

        // The panel sits lower in free mode, so the controls are not where the swatch layout has them.
        Assert.NotEqual(layout.Swatches.Size!.Value.Center, script.Steps.OfType<SetBrushValueStep>().First().At);
        Assert.Equal(layout.Free.Size!.Value.Center, script.Steps.OfType<SetBrushValueStep>().First().At);
        Assert.Equal(ColorMode.Free, ((EnsureColorModeStep)script.Steps[0]).Mode);
    }

    [Fact]
    public void Neon_signs_only_choose_a_preset_and_have_no_brush_numbers()
    {
        ResolvedLayout layout = Layout("neon-medium");
        PaintScript script = PaintScriptBuilder.Build(SmallPlan(SwatchPalette("neon-medium")), layout, new PaintOptions(ColorMode.Swatches, 4));

        Assert.Empty(script.Steps.OfType<SetBrushValueStep>());

        ClickControlStep shape = script.Steps.OfType<ClickControlStep>().Single(step => step.Control == "Brush shape");
        Assert.Equal(layout.Swatches.BrushShapes[0].Center, shape.At);
    }

    [Fact]
    public void Canvases_that_open_cropped_start_with_the_scroll_out()
    {
        PaintScript script = PaintScriptBuilder.Build(SmallPlan(SwatchPalette("canvas-xxl")), Layout("canvas-xxl"), new PaintOptions(ColorMode.Swatches, 4));
        Assert.Equal(new ScrollOutStep(11), script.Steps[0]);

        PaintScript sign = PaintScriptBuilder.Build(SmallPlan(SwatchPalette("sign-medium")), Layout("sign-medium"), new PaintOptions(ColorMode.Swatches, 4));
        Assert.IsNotType<ScrollOutStep>(sign.Steps[0]);
    }

    [Fact]
    public void Strokes_go_through_the_middle_of_the_cells_laid_over_the_canvas()
    {
        ResolvedLayout layout = Layout("sign-medium");
        ScreenRect canvas = layout.Canvas;
        PaintScript script = PaintScriptBuilder.Build(SmallPlan(SwatchPalette("sign-medium")), layout, new PaintOptions(ColorMode.Swatches, 4));

        PaintStrokeStep first = script.Steps.OfType<PaintStrokeStep>().First();
        double cellWidth = canvas.Width / 4, cellHeight = canvas.Height / 2;

        // The first stroke is the red row along the top: cell 0 to cell 3 of row 0.
        Assert.Equal(new Stroke(0, 0, 4, Vertical: false), first.Stroke);
        Assert.Equal(canvas.X1 + 0.5 * cellWidth, first.From.X, 6);
        Assert.Equal(canvas.Y1 + 0.5 * cellHeight, first.From.Y, 6);
        Assert.Equal(canvas.X1 + 3.5 * cellWidth, first.To.X, 6);
        Assert.Equal(first.From.Y, first.To.Y, 6);
    }

    [Fact]
    public void A_click_starts_and_ends_on_the_same_point()
    {
        PaintScript script = PaintScriptBuilder.Build(SmallPlan(SwatchPalette("sign-medium")), Layout("sign-medium"), new PaintOptions(ColorMode.Swatches, 4));

        PaintStrokeStep dot = script.Steps.OfType<PaintStrokeStep>().Single(step => step.Stroke.IsClick && step.Stroke.X == 3);

        Assert.Equal(dot.From, dot.To);
    }

    [Fact]
    public void Vertical_strokes_run_down_the_column()
    {
        PaintPlan plan = new(2, 4, SwatchPalette("sign-medium"), [0, 1, 0, 1, 0, 1, 0, 1]);
        ResolvedLayout layout = Layout("sign-medium");

        PaintScript script = PaintScriptBuilder.Build(plan, layout, new PaintOptions(ColorMode.Swatches, 4));

        PaintStrokeStep first = script.Steps.OfType<PaintStrokeStep>().First();
        Assert.True(first.Stroke.Vertical);
        Assert.Equal(first.From.X, first.To.X, 6);
        Assert.True(first.To.Y > first.From.Y);
    }

    [Fact]
    public void Every_point_is_inside_the_game_window_at_any_resolution()
    {
        foreach ((int w, int h) in new[] { (2560, 1440), (1920, 1080), (3440, 1440), (1280, 720) })
        {
            foreach (Surface surface in Surfaces.Catalog.Surfaces)
            {
                ResolvedLayout layout = Layout(surface.Id, w, h);
                PaintScript script = PaintScriptBuilder.Build(SmallPlan(SwatchPalette(surface.Id)), layout, new PaintOptions(ColorMode.Swatches, 4));

                foreach (PaintStep step in script.Steps)
                {
                    foreach (ScreenPoint point in PointsOf(step))
                    {
                        Assert.InRange(point.X, 0, w);
                        Assert.InRange(point.Y, 0, h);
                    }
                }
            }
        }
    }

    private static IEnumerable<ScreenPoint> PointsOf(PaintStep step) => step switch
    {
        EnsureColorModeStep s => [s.Toggle],
        ClickControlStep s => [s.At],
        SetBrushValueStep s => [s.At],
        SelectSwatchStep s => [s.At],
        TypeColorStep s => [s.At],
        PaintStrokeStep s => [s.From, s.To],
        _ => []
    };

    [Fact]
    public void Positions_follow_the_game_resolution()
    {
        PaintScript big = PaintScriptBuilder.Build(SmallPlan(SwatchPalette("sign-medium")), Layout("sign-medium"), new PaintOptions(ColorMode.Swatches, 4));
        PaintScript small = PaintScriptBuilder.Build(SmallPlan(SwatchPalette("sign-medium")), Layout("sign-medium", 1920, 1080), new PaintOptions(ColorMode.Swatches, 4));

        PaintStrokeStep a = big.Steps.OfType<PaintStrokeStep>().First();
        PaintStrokeStep b = small.Steps.OfType<PaintStrokeStep>().First();

        Assert.Equal(a.From.X * 0.75, b.From.X, 6);
        Assert.Equal(a.To.Y * 0.75, b.To.Y, 6);
    }

    [Fact]
    public void Fine_tuning_moves_the_strokes_but_not_the_controls()
    {
        ResolvedLayout layout = Layout("sign-medium");
        IReadOnlyList<Rgb> palette = SwatchPalette("sign-medium");
        PaintScript plain = PaintScriptBuilder.Build(SmallPlan(palette), layout, new PaintOptions(ColorMode.Swatches, 4));
        PaintScript moved = PaintScriptBuilder.Build(SmallPlan(palette), layout, new PaintOptions(ColorMode.Swatches, 4, NudgeX: 3, NudgeY: -2));

        PaintStrokeStep a = plain.Steps.OfType<PaintStrokeStep>().First();
        PaintStrokeStep b = moved.Steps.OfType<PaintStrokeStep>().First();

        Assert.Equal(a.From.X + 3, b.From.X, 6);
        Assert.Equal(a.From.Y - 2, b.From.Y, 6);
        Assert.Equal(plain.Steps.OfType<SelectSwatchStep>().First().At, moved.Steps.OfType<SelectSwatchStep>().First().At);
    }

    [Fact]
    public void Counts_match_the_plan()
    {
        PaintPlan plan = SmallPlan(SwatchPalette("sign-medium"));
        PaintScript script = PaintScriptBuilder.Build(plan, Layout("sign-medium"), new PaintOptions(ColorMode.Swatches, 4));

        Assert.Equal(plan.StrokeCount, script.StrokeCount);
        Assert.Equal(plan.ColorsUsed, script.ColorChanges);
    }
}
