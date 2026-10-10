using Avalonia.Headless.XUnit;
using RustyPainter.Imaging;
using RustyPainter.Interface;
using RustyPainter.Painting;
using RustyPainter.Service;
using RustyPainter.ViewModels;

namespace RustyPainter.Tests;

public class PumpkinTests
{
    private static readonly SurfaceService Surfaces = new();

    private static Surface Pumpkin => Surfaces.Catalog.Surfaces.Single(surface => surface.Id == "pumpkin");

    private static ResolvedLayout Layout(int width = 2560, int height = 1440) => Surfaces.Resolve(Pumpkin, width, height);

    [Fact]
    public void The_pumpkin_has_eight_grays_from_white_to_black_and_nothing_else_to_choose()
    {
        Assert.True(Pumpkin.FixedPalette);
        Assert.Null(Surfaces.Catalog.Layouts["carving"].Free);

        string[] grays = Layout().Swatches.ColorGrid!.Colors;
        Assert.Equal(["#FFFFFF", "#E0E0E0", "#BFBFBF", "#A1A1A1", "#808080", "#404040", "#212121", "#000000"], grays);
        Assert.Null(Layout().Swatches.Spacing);
        Assert.Null(Layout().Swatches.Opacity);
        Assert.NotNull(Layout().Swatches.Size);
        Assert.Equal(4, Layout().Swatches.BrushShapes.Length);
    }

    [Fact]
    public void A_surface_without_a_free_color_screen_has_to_say_so()
    {
        SurfaceCatalog catalog = Surfaces.Catalog;
        Surface forgot = Pumpkin with { FixedPalette = false, Id = "forgot" };

        Assert.Throws<InvalidDataException>(() => SurfaceService.Validate(catalog with { Surfaces = [forgot] }));
    }

    [Fact]
    public void The_free_layout_of_the_pumpkin_is_the_same_screen_so_nothing_can_be_missing()
    {
        ResolvedLayout layout = Layout();

        Assert.Equal(layout.Swatches.ToolBrush, layout.Free.ToolBrush);
        Assert.Equal(layout.Swatches.Size, layout.Free.Size);
        Assert.Equal(layout.Swatches.ColorGrid!.Colors, layout.Free.ColorGrid!.Colors);
    }

    [Fact]
    public void Painting_the_pumpkin_never_touches_a_color_mode_and_types_only_the_size()
    {
        ResolvedLayout layout = Layout();
        IReadOnlyList<Rgb> grays = layout.Swatches.ColorGrid!.Colors.Select(Rgb.Parse).ToList();
        PaintPlan plan = new(3, 1, grays, [0, 3, 7]);

        PaintScript script = PaintScriptBuilder.Build(plan, layout, new PaintOptions(ColorMode.Free, 4, ShapeIndex: PaintOptions.CarvingShape));

        Assert.Empty(script.Steps.OfType<EnsureColorModeStep>());
        Assert.Empty(script.Steps.OfType<TypeColorStep>());
        Assert.Equal(["Size"], script.Steps.OfType<SetBrushValueStep>().Select(step => step.Field));
        Assert.Equal("4", script.Steps.OfType<SetBrushValueStep>().Single().Text);
        Assert.Equal([0, 3, 7], script.Steps.OfType<SelectSwatchStep>().Select(step => step.Index));

        ClickControlStep shape = script.Steps.OfType<ClickControlStep>().Single(step => step.Control == "Brush shape");
        Assert.Equal(layout.Swatches.BrushShapes[2].Center, shape.At);
    }

    [Fact]
    public void The_carving_screen_follows_the_game_resolution()
    {
        ScreenPoint full = Layout().Swatches.ColorGrid!.CellCenter(0);
        ScreenPoint half = Layout(1280, 720).Swatches.ColorGrid!.CellCenter(0);

        Assert.Equal(full.X / 2, half.X, 1);
        Assert.Equal(full.Y / 2, half.Y, 1);
    }

    [AvaloniaFact]
    public void Studio_gives_the_pumpkin_no_color_choice_and_carves_a_picture_by_how_light_it_is()
    {
        StudioViewModel studio = new(Surfaces) { ReprocessDelay = TimeSpan.Zero };
        studio.SelectedSurface = studio.AllSurfaces.Single(option => option.Surface.Id == "pumpkin");

        Assert.Equal("Carving", studio.SelectedSurface.Category);
        Assert.True(studio.IsFixedPalette);
        Assert.False(studio.HasChoosableColors);

        // Any color was chosen before and the pumpkin has to ignore it.
        studio.ColorModeChoices.Choices[1].SelectCommand.Execute(null);
        Assert.False(studio.IsFreeMode);

        string path = Path.Combine(Path.GetTempPath(), $"rp-pumpkin-{Guid.NewGuid():N}.png");
        TestImages.SavePng(TestImages.RedAndGreen(), path);
        Assert.True(studio.TryLoad(path));
        studio.ProcessNow();
        File.Delete(path);

        Assert.NotNull(studio.Plan);
        string[] grays = Layout().Swatches.ColorGrid!.Colors;
        Assert.All(studio.Plan!.Strokes.Passes, pass => Assert.Contains(pass.Color.Hex, grays, StringComparer.OrdinalIgnoreCase));

        PaintRequest? request = null;
        studio.PaintRequested += r => request = r;
        studio.PaintCommand.Execute(null);

        Assert.NotNull(request);
        Assert.Equal(ColorMode.Swatches, request.Mode);
    }

    [AvaloniaFact]
    public void Another_surface_keeps_its_colors_and_its_choice()
    {
        StudioViewModel studio = new(Surfaces);
        studio.SelectedSurface = studio.AllSurfaces.Single(option => option.Surface.Id == "pumpkin");
        studio.SelectedSurface = studio.AllSurfaces.Single(option => option.Surface.Id == "sign-small");

        Assert.False(studio.IsFixedPalette);
        Assert.True(studio.HasChoosableColors);
    }
}
