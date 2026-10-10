using Avalonia.Headless.XUnit;
using RustyPainter.Imaging;
using RustyPainter.Interface;
using RustyPainter.Painting;
using RustyPainter.Service;
using RustyPainter.ViewModels;

namespace RustyPainter.Tests;

public class NeonPresetTests
{
    private static readonly SurfaceService Surfaces = new();

    private static Surface Find(string id) => Surfaces.Catalog.Surfaces.Single(surface => surface.Id == id);

    [Fact]
    public void Every_neon_sign_has_its_four_brushes_measured()
    {
        foreach (string id in new[] { "neon-small", "neon-medium", "neon-large" })
        {
            Surface sign = Find(id);

            Assert.NotNull(sign.BrushPresets);
            Assert.Equal(4, sign.BrushPresets!.Length);
            Assert.True(StudioGeometry.HasPresets(sign));
            Assert.True(StudioGeometry.IsTextureKnown(sign));

            // Each brush paints a thicker line than the one before it.
            for (int i = 1; i < sign.BrushPresets.Length; i++)
                Assert.True(sign.BrushPresets[i].Width > sign.BrushPresets[i - 1].Width, $"{id} preset {i}");
        }
    }

    [Fact]
    public void The_surfaces_with_a_brush_size_have_no_presets()
    {
        Assert.All(Surfaces.Catalog.Surfaces.Where(s => !s.Id.StartsWith("neon") && !s.IsAnimated), s => Assert.False(StudioGeometry.HasPresets(s), s.Id));
    }

    [Fact]
    public void The_grid_of_a_neon_sign_is_the_canvas_divided_by_the_stamp_of_the_brush()
    {
        Surface small = Find("neon-small");

        // 522 by 523 pixels, with stamps of 26 x 26, 38 x 38, 58 x 59 and 92 x 91.
        Assert.Equal((21, 21), StudioGeometry.Grid(small, 0));
        Assert.Equal((14, 14), StudioGeometry.Grid(small, 1));
        Assert.Equal((9, 9), StudioGeometry.Grid(small, 2));
        Assert.Equal((6, 6), StudioGeometry.Grid(small, 3));
    }

    [Fact]
    public void A_stretched_neon_sign_has_cells_that_are_taller_than_wide()
    {
        Surface medium = Find("neon-medium");

        (int w, int h) = StudioGeometry.Grid(medium, 3);

        // 894 x 519 pixels, stamps of 79 x 90: 12 across and 6 down.
        Assert.Equal((12, 6), (w, h));
    }

    [Fact]
    public void A_preset_that_does_not_exist_is_kept_to_the_nearest_one()
    {
        Surface small = Find("neon-small");

        Assert.Equal(StudioGeometry.Grid(small, 0), StudioGeometry.Grid(small, -4));
        Assert.Equal(StudioGeometry.Grid(small, 3), StudioGeometry.Grid(small, 40));
    }

    [Fact]
    public void A_surface_without_a_size_field_must_have_presets()
    {
        SurfaceCatalog catalog = Surfaces.Catalog;
        Surface neon = Find("neon-small") with { BrushPresets = null };

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => SurfaceService.Validate(catalog with { Surfaces = [.. catalog.Surfaces.Where(s => s.Id != "neon-small"), neon] }));

        Assert.Contains("neon-small", error.Message);
    }

    [Fact]
    public void The_number_of_presets_must_match_the_brushes_on_the_paint_screen()
    {
        SurfaceCatalog catalog = Surfaces.Catalog;
        Surface neon = Find("neon-small") with { BrushPresets = [new BrushPreset(10, 10), new BrushPreset(20, 20)] };

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => SurfaceService.Validate(catalog with { Surfaces = [.. catalog.Surfaces.Where(s => s.Id != "neon-small"), neon] }));

        Assert.Contains("2 brush presets", error.Message);
    }

    [AvaloniaFact]
    public void Studio_offers_the_four_brushes_for_a_neon_sign_and_sizes_for_the_others()
    {
        StudioViewModel studio = new(Surfaces);

        Assert.True(studio.IsSizeSurface);
        Assert.False(studio.IsPresetSurface);

        studio.SelectedSurface = studio.AllSurfaces.Single(option => option.Surface.Id == "neon-small");

        Assert.True(studio.IsPresetSurface);
        Assert.False(studio.IsSizeSurface);
        Assert.Equal(4, studio.PresetChoices.Choices.Count);
        Assert.Equal(1, studio.BlockSize);
        Assert.Equal("14 x 14 cells", studio.GridText);

        studio.PresetChoices.Select(3);
        Assert.Equal(3, studio.BlockSize);
        Assert.Equal("6 x 6 cells", studio.GridText);
    }

    [AvaloniaFact]
    public void The_size_and_the_brush_of_the_two_kinds_of_surface_do_not_disturb_each_other()
    {
        StudioViewModel studio = new(Surfaces);
        studio.QualityChoices.Select(16);
        studio.SelectedSurface = studio.AllSurfaces.Single(option => option.Surface.Id == "neon-large");
        studio.PresetChoices.Select(0);

        studio.SelectedSurface = studio.AllSurfaces.Single(option => option.Surface.Id == "sign-medium");

        Assert.Equal(16, studio.BlockSize);
    }

    [AvaloniaFact]
    public void Painting_a_neon_sign_clicks_the_chosen_brush_and_types_no_size()
    {
        SurfaceService surfaces = Surfaces;
        Surface neon = Find("neon-medium");
        ResolvedLayout layout = surfaces.Resolve(neon, 2560, 1440);
        IReadOnlyList<Rgb> swatches = layout.Swatches.ColorGrid!.Colors.Select(Rgb.Parse).ToList();
        (int w, int h) = StudioGeometry.Grid(neon, 2);
        PaintPlan plan = new(w, h, swatches, Enumerable.Repeat(1, w * h).ToArray());
        PaintRequest request = new(plan, neon, ColorMode.Swatches, 2, "line.png");

        GetReadyViewModel page = new(request, new FakeGame(FakeGame.Rust()), surfaces, TestSettings.Create(), new FakeHologram(), new Hotkey(0, 0x20), new Hotkey(0, 0x1B));
        PaintSession? session = null;
        page.StartRequested += s => session = s;
        page.StartCommand.Execute(null);

        Assert.Equal(2, session!.Options.ShapeIndex);

        PaintScript script = PaintScriptBuilder.Build(plan, layout, session.Options);
        ClickControlStep brush = script.Steps.OfType<ClickControlStep>().Single(step => step.Control == "Brush shape");

        Assert.Equal(layout.Swatches.BrushShapes[2].Center, brush.At);
        Assert.Empty(script.Steps.OfType<SetBrushValueStep>());
    }

    [AvaloniaFact]
    public void A_surface_with_a_brush_size_still_types_it_and_uses_the_square_brush()
    {
        Surface sign = Find("sign-medium");
        ResolvedLayout layout = Surfaces.Resolve(sign, 2560, 1440);
        IReadOnlyList<Rgb> swatches = layout.Swatches.ColorGrid!.Colors.Select(Rgb.Parse).ToList();
        PaintPlan plan = new(4, 2, swatches, [1, 1, 1, 1, 1, 1, 1, 1]);
        PaintRequest request = new(plan, sign, ColorMode.Swatches, 8, "line.png");

        GetReadyViewModel page = new(request, new FakeGame(FakeGame.Rust()), Surfaces, TestSettings.Create(), new FakeHologram(), new Hotkey(0, 0x20), new Hotkey(0, 0x1B));
        PaintSession? session = null;
        page.StartRequested += s => session = s;
        page.StartCommand.Execute(null);

        Assert.Null(session!.Options.ShapeIndex);
        Assert.Equal("8", PaintScriptBuilder.Build(plan, layout, session.Options).Steps.OfType<SetBrushValueStep>().First(step => step.Field == "Size").Text);
    }
}
