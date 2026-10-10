using Avalonia.Headless.XUnit;
using RustyPainter.ViewModels;
using RustyPainter.Imaging;
using RustyPainter.Interface;
using RustyPainter.Painting;
using RustyPainter.Service;

namespace RustyPainter.Tests;

public class AnimatedSignTests
{
    private static readonly SurfaceService Surfaces = new();

    private static Surface Find(string id) => Surfaces.Catalog.Surfaces.Single(surface => surface.Id == id);

    private static ResolvedLayout Layout(string id, int width = 2560, int height = 1440) => Surfaces.Resolve(Find(id), width, height);

    private static IReadOnlyList<Rgb> Palette() => Layout("animated-neon-large").Swatches.ColorGrid!.Colors.Select(Rgb.Parse).ToList();

    private static PaintPlan Plan(params int[] cells) => new(cells.Length, 1, Palette(), cells);

    [Fact]
    public void The_three_animated_signs_have_five_frames_and_the_others_have_one()
    {
        Assert.Equal(["animated-neon-large", "animated-neon-medium", "animated-neon-small"],
            Surfaces.Catalog.Surfaces.Where(surface => surface.IsAnimated).Select(surface => surface.Id));

        Assert.All(Surfaces.Catalog.Surfaces.Where(surface => surface.IsAnimated), surface => Assert.Equal(5, surface.Frames));
        Assert.All(Surfaces.Catalog.Surfaces.Where(surface => !surface.IsAnimated), surface => Assert.Equal(1, surface.Frames));
    }

    [Fact]
    public void The_animated_paint_screen_is_the_neon_one_moved_down_by_the_frames_panel()
    {
        LayoutFamily neon = Surfaces.Catalog.Layouts["neon"], animated = Surfaces.Catalog.Layouts["animated"];

        foreach ((PaintLayout plain, PaintLayout moved) in new[] { (neon.Swatches, animated.Swatches), (neon.Free, animated.Free) })
        {
            Assert.Equal(130, moved.ToolBrush.Y1 - plain.ToolBrush.Y1, 1);
            Assert.Equal(130, moved.BrushShapes[0].Y1 - plain.BrushShapes[0].Y1, 1);
            Assert.Equal(130, moved.ColorModeToggle.Y1 - plain.ColorModeToggle.Y1, 1);

            // The toolbar, the saved colors and the buttons at the bottom stay where they were.
            Assert.Equal(plain.Clear, moved.Clear);
            Assert.Equal(plain.SaveChanges, moved.SaveChanges);
        }

        Assert.Equal(130, animated.Swatches.ColorGrid!.Area.Y1 - neon.Swatches.ColorGrid!.Area.Y1, 1);
        Assert.Equal(130, animated.Free.FreeColor!.ColorHex.Y1 - neon.Free.FreeColor!.ColorHex.Y1, 1);
        Assert.Equal(neon.Free.FreeColor.SavedColors, animated.Free.FreeColor.SavedColors);
    }

    [Fact]
    public void The_frame_buttons_sit_in_one_row_a_brush_preset_apart()
    {
        FrameControls frames = Surfaces.Catalog.Layouts["animated"].Swatches.Frames!;

        Assert.Equal(5, frames.Buttons.Length);
        Assert.All(frames.Buttons, button => Assert.Equal(frames.Buttons[0].Center.Y, button.Center.Y, 1));
        for (int i = 1; i < 5; i++)
            Assert.Equal(64, frames.Buttons[i].Center.X - frames.Buttons[i - 1].Center.X, 1);

        // The row sits above the copy button, and the copy button above the tools.
        Assert.True(frames.Buttons[0].Y2 < frames.CopyPrevious.Y1);
        Assert.True(frames.CopyPrevious.Y2 < Surfaces.Catalog.Layouts["animated"].Swatches.ToolBrush.Y1);
        Assert.Equal(frames.Buttons.Select(button => button.Center.X), Surfaces.Catalog.Layouts["animated"].Free.Frames!.Buttons.Select(button => button.Center.X));
    }

    [Fact]
    public void The_frame_buttons_follow_the_game_resolution()
    {
        ScreenPoint full = Layout("animated-neon-large").Swatches.Frames!.Buttons[2].Center;
        ScreenPoint half = Layout("animated-neon-large", 1280, 720).Swatches.Frames!.Buttons[2].Center;

        Assert.Equal(full.X / 2, half.X, 1);
        Assert.Equal(full.Y / 2, half.Y, 1);
    }

    [Fact]
    public void Each_frame_is_opened_cleared_and_painted_in_turn_and_the_frames_left_over_are_cleared_first()
    {
        ResolvedLayout layout = Layout("animated-neon-large");
        PaintScript script = PaintScriptBuilder.Build([Plan(0, 1), Plan(2, 2), Plan(3)], layout, new PaintOptions(ColorMode.Swatches, 1));

        FrameControls frames = layout.Swatches.Frames!;
        List<PaintStep> steps = [.. script.Steps];

        // Frames 4 and 5 hold nothing of this picture, so they are cleared before anything is painted.
        List<PaintStep> beforeStrokes = steps.Take(steps.FindIndex(step => step is PaintStrokeStep)).ToList();
        Assert.Equal([3, 4, 0], beforeStrokes.OfType<SelectFrameStep>().Select(step => step.Index));
        Assert.Equal([3, 4, 0, 1, 2], steps.OfType<SelectFrameStep>().Select(step => step.Index));

        int firstOpened = beforeStrokes.FindIndex(step => step is SelectFrameStep);
        Assert.Equal("Clear", Assert.IsType<ClickControlStep>(beforeStrokes[firstOpened + 1]).Control);

        // Each painted frame: its number, the brush, clear, the shape, then its colors and strokes.
        List<SelectFrameStep> opened = steps.OfType<SelectFrameStep>().Skip(2).ToList();
        Assert.Equal(frames.Buttons[1].Center, opened[1].At);

        int second = steps.FindIndex(step => ReferenceEquals(step, opened[1]));
        Assert.Equal("Brush", Assert.IsType<ClickControlStep>(steps[second + 1]).Control);
        Assert.Equal("Clear", Assert.IsType<ClickControlStep>(steps[second + 2]).Control);
        Assert.Equal("Brush shape", Assert.IsType<ClickControlStep>(steps[second + 3]).Control);
        Assert.IsType<SelectSwatchStep>(steps[second + 4]);

        // Every picture is painted, with its own colors.
        Assert.Equal(4, script.Steps.OfType<PaintStrokeStep>().Count());
        Assert.Equal([0, 1, 2, 3], script.Steps.OfType<SelectSwatchStep>().Select(step => step.Index));
    }

    [Fact]
    public void A_full_set_of_frames_leaves_nothing_over_to_clear()
    {
        PaintScript script = PaintScriptBuilder.Build([Plan(0), Plan(1), Plan(2), Plan(3), Plan(4)], Layout("animated-neon-large"), new PaintOptions(ColorMode.Swatches, 1));

        Assert.Equal([0, 1, 2, 3, 4], script.Steps.OfType<SelectFrameStep>().Select(step => step.Index));
        Assert.Equal(5, script.Steps.OfType<ClickControlStep>().Count(step => step.Control == "Clear"));
    }

    [Fact]
    public void A_frame_is_painted_with_the_colors_of_its_own_picture()
    {
        ResolvedLayout layout = Layout("animated-neon-large");
        PaintScript script = PaintScriptBuilder.Build([Plan(0), Plan(5)], layout, new PaintOptions(ColorMode.Swatches, 1, ClearFirst: false));
        List<PaintStep> steps = [.. script.Steps];

        int second = steps.FindIndex(step => step is SelectFrameStep { Index: 1 });
        Assert.Equal(5, steps.Skip(second).OfType<SelectSwatchStep>().First().Index);
        Assert.Equal(0, steps.Take(second).OfType<SelectSwatchStep>().Single().Index);
    }

    [Fact]
    public void Without_clearing_no_frame_is_cleared()
    {
        PaintScript script = PaintScriptBuilder.Build([Plan(0), Plan(1)], Layout("animated-neon-large"), new PaintOptions(ColorMode.Swatches, 1, ClearFirst: false));

        Assert.DoesNotContain(script.Steps.OfType<ClickControlStep>(), step => step.Control == "Clear");
        Assert.Equal(2, script.Steps.OfType<SelectFrameStep>().Count());
    }

    [Fact]
    public void Free_colors_are_typed_on_the_moved_paint_screen()
    {
        ResolvedLayout layout = Layout("animated-neon-large");
        PaintScript script = PaintScriptBuilder.Build([Plan(0), Plan(1)], layout, new PaintOptions(ColorMode.Free, 1));

        Assert.All(script.Steps.OfType<TypeColorStep>(), step => Assert.Equal(layout.Free.FreeColor!.ColorHex.Center, step.At));
        Assert.All(script.Steps.OfType<SelectFrameStep>(), step => Assert.Contains(layout.Free.Frames!.Buttons, button => button.Center == step.At));
    }

    [Fact]
    public void The_frame_check_looks_inside_the_green_circle_of_the_button()
    {
        ResolvedLayout layout = Layout("animated-neon-large");
        PaintScript script = PaintScriptBuilder.Build([Plan(0)], layout, new PaintOptions(ColorMode.Swatches, 1, ClearFirst: false));

        SelectFrameStep step = script.Steps.OfType<SelectFrameStep>().Single();
        ScreenRect button = layout.Swatches.Frames!.Buttons[0];

        Assert.Equal(button.Center.X, step.Probe.X, 1);
        Assert.InRange(step.Probe.Y, button.Y1, button.Center.Y);
    }

    [Fact]
    public void More_pictures_than_frames_are_refused_and_so_is_nothing()
    {
        ResolvedLayout layout = Layout("animated-neon-large");
        Assert.Throws<ArgumentException>(() => PaintScriptBuilder.Build([Plan(0), Plan(0), Plan(0), Plan(0), Plan(0), Plan(0)], layout, new PaintOptions(ColorMode.Swatches, 1)));
        Assert.Throws<ArgumentException>(() => PaintScriptBuilder.Build([], layout, new PaintOptions(ColorMode.Swatches, 1)));

        ResolvedLayout plain = Surfaces.Resolve(Find("neon-large"), 2560, 1440);
        Assert.Throws<ArgumentException>(() => PaintScriptBuilder.Build([Plan(0), Plan(1)], plain, new PaintOptions(ColorMode.Swatches, 1)));
    }

    [Fact]
    public void A_sign_that_does_not_animate_gets_no_frame_steps()
    {
        PaintScript script = PaintScriptBuilder.Build(Plan(0, 1), Surfaces.Resolve(Find("neon-large"), 2560, 1440), new PaintOptions(ColorMode.Swatches, 1));

        Assert.Empty(script.Steps.OfType<SelectFrameStep>());
    }

    [Fact]
    public void A_request_counts_the_strokes_and_colors_of_every_frame()
    {
        Surface surface = Find("animated-neon-large");
        PaintPlan one = Plan(0, 1, 1), two = Plan(1, 2);
        PaintRequest request = new(one, surface, ColorMode.Swatches, 1, "frames", [one, two]);

        Assert.Equal(one.StrokeCount + two.StrokeCount, request.StrokeCount);
        Assert.Equal(3, request.ColorsUsed);
        Assert.Equal(2, request.Plans.Count);

        PaintRequest single = new(one, surface, ColorMode.Swatches, 1, "frames");
        Assert.Equal([one], single.Plans);
        Assert.Equal(one.StrokeCount, single.StrokeCount);
    }


    private static GetReadyViewModel GetReady(PaintRequest request, FakeHologram hologram)
        => new(request, new FakeGame(FakeGame.Rust()), Surfaces, TestSettings.Create(), hologram, new Hotkey(0, 0x77), new Hotkey(0, 0x78));

    [AvaloniaFact]
    public void The_picture_over_the_game_goes_through_the_frames_of_an_animation()
    {
        Surface surface = Find("animated-neon-large");
        PaintPlan one = Plan(0, 0, 0), two = Plan(1, 1, 1), three = Plan(2, 2, 2);
        FakeHologram hologram = new();
        using GetReadyViewModel page = GetReady(new PaintRequest(one, surface, ColorMode.Swatches, 1, "frames", [one, two, three]), hologram);

        Assert.True(page.IsAnimation);
        Assert.Equal(1, page.ShownFrame);
        PixelBuffer first = hologram.Picture!;

        page.AdvanceFrame();
        Assert.Equal(2, page.ShownFrame);
        Assert.NotSame(first, hologram.Picture);
        Assert.NotEqual(first.Data, hologram.Picture!.Data);

        page.AdvanceFrame();
        page.AdvanceFrame();
        Assert.Equal(1, page.ShownFrame);
        Assert.Equal(first.Data, hologram.Picture!.Data);
    }

    [AvaloniaFact]
    public void A_picture_that_does_not_animate_stays_as_it_is_and_has_no_speed_to_choose()
    {
        Surface surface = Find("neon-large");
        PaintPlan one = Plan(0, 1);
        FakeHologram hologram = new();
        using GetReadyViewModel page = GetReady(new PaintRequest(one, surface, ColorMode.Swatches, 1, "single"), hologram);

        Assert.False(page.IsAnimation);
        PixelBuffer shown = hologram.Picture!;
        page.AdvanceFrame();
        Assert.Equal(1, page.ShownFrame);
        Assert.Equal(shown.Data, hologram.Picture!.Data);
    }

    [AvaloniaFact]
    public void The_speed_chosen_in_studio_is_the_speed_over_the_game()
    {
        Surface surface = Find("animated-neon-large");
        PaintPlan one = Plan(0), two = Plan(1);

        using GetReadyViewModel fast = GetReady(new PaintRequest(one, surface, ColorMode.Swatches, 1, "x", [one, two], 0.5), new FakeHologram());
        using GetReadyViewModel normal = GetReady(new PaintRequest(one, surface, ColorMode.Swatches, 1, "x", [one, two]), new FakeHologram());

        Assert.Equal(0.5, fast.FrameSpeedChoices.Selected);
        Assert.Equal(1.0, normal.FrameSpeedChoices.Selected);

        normal.FrameSpeedChoices.Choices[0].SelectCommand.Execute(null);
        Assert.Equal(2.0, normal.FrameSpeedChoices.Selected);
    }

    [AvaloniaFact]
    public void Starting_to_paint_takes_the_picture_off_the_game()
    {
        Surface surface = Find("animated-neon-large");
        PaintPlan one = Plan(0), two = Plan(1);
        FakeHologram hologram = new();
        using GetReadyViewModel page = GetReady(new PaintRequest(one, surface, ColorMode.Swatches, 1, "x", [one, two]), hologram);
        PaintSession? started = null;
        page.StartRequested += session => started = session;

        page.StartCommand.Execute(null);

        Assert.NotNull(started);
        Assert.False(hologram.IsShown);
        Assert.Equal(2, started.Request.Plans.Count);
    }

    [Fact]
    public void Painting_more_frames_takes_longer()
    {
        PaintPlan one = Plan(0, 1, 0, 1, 0, 1), two = Plan(1, 0, 1, 0, 1, 0);
        PaintTiming timing = PaintTiming.FromSpeed(60);

        TimeSpan alone = PaintTimeEstimator.Estimate([one.Strokes], timing, ColorMode.Swatches);
        TimeSpan both = PaintTimeEstimator.Estimate([one.Strokes, two.Strokes], timing, ColorMode.Swatches);

        Assert.True(both > alone);
        Assert.Equal(alone, PaintTimeEstimator.Estimate(one.Strokes, timing, ColorMode.Swatches));
    }
}
