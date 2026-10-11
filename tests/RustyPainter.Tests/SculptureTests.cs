using Avalonia.Headless.XUnit;
using RustyPainter.Imaging;
using RustyPainter.Interface;
using RustyPainter.Painting;
using RustyPainter.Service;
using RustyPainter.ViewModels;

namespace RustyPainter.Tests;

public class SculptureTests
{
    private static readonly SurfaceService Surfaces = new();

    private static Surface Sculpture => Surfaces.Catalog.Surfaces.Single(surface => surface.Id == "stone-sculpture");

    private static ResolvedLayout Layout(int width = 2560, int height = 1440) => Surfaces.Resolve(Sculpture, width, height);

    [Fact]
    public void The_sculpture_has_carve_and_add_as_its_tools_and_a_color_that_leaves_cells_alone()
    {
        Assert.True(Sculpture.FixedPalette);
        Assert.False(Sculpture.HasCameraReset);
        Assert.Equal(2, Sculpture.LeaveIndex);

        ColorGrid grid = Layout().Swatches.ColorGrid!;
        Assert.Equal(["#000000", "#FFFFFF", "#808080"], grid.Colors);

        // The cells of the grid are where the carve and add buttons are, so picking a color clicks a tool.
        Assert.Equal(Layout().Swatches.ToolBrush.Center, grid.CellCenter(0));
        Assert.Equal(Layout().Swatches.ToolEraser.Center, grid.CellCenter(1));
        Assert.Equal(6, Layout().Swatches.BrushShapes.Length);
        Assert.NotNull(Layout().Swatches.Size);
        Assert.NotNull(Layout().Swatches.Hardness);
    }

    [Fact]
    public void Painting_the_sculpture_leaves_the_camera_alone_and_types_size_and_hardness_in_tenths()
    {
        ResolvedLayout layout = Layout();
        PaintPlan plan = new(3, 1, layout.Swatches.ColorGrid!.Colors.Select(Rgb.Parse).ToList(), [0, 1, Quantizer.Unpainted]);

        PaintScript script = PaintScriptBuilder.Build(plan, layout, new PaintOptions(ColorMode.Swatches, 4, ShapeIndex: Sculpture.BrushShape));

        Assert.Empty(script.Steps.OfType<ResetCameraStep>());
        Assert.Empty(script.Steps.OfType<EnsureColorModeStep>());

        Dictionary<string, SetBrushValueStep> values = script.Steps.OfType<SetBrushValueStep>().ToDictionary(step => step.Field);
        Assert.Equal(["Size", "Hardness"], values.Keys);
        Assert.Equal("0.4", values["Size"].Text);
        Assert.Equal("1", values["Hardness"].Text);

        Assert.Equal([0, 1], script.Steps.OfType<SelectSwatchStep>().Select(step => step.Index));
        ClickControlStep shape = script.Steps.OfType<ClickControlStep>().Single(step => step.Control == "Brush shape");
        Assert.Equal(layout.Swatches.BrushShapes[1].Center, shape.At);
    }

    [Fact]
    public void Other_surfaces_still_type_whole_sizes_and_reset_the_camera()
    {
        ResolvedLayout layout = Surfaces.Resolve(Surfaces.Catalog.Surfaces.Single(surface => surface.Id == "sign-medium"), 2560, 1440);
        PaintPlan plan = new(1, 1, layout.Swatches.ColorGrid!.Colors.Select(Rgb.Parse).ToList(), [0]);

        PaintScript script = PaintScriptBuilder.Build(plan, layout, new PaintOptions(ColorMode.Swatches, 8));

        Assert.IsType<ResetCameraStep>(script.Steps[0]);
        Assert.Equal("8", script.Steps.OfType<SetBrushValueStep>().First(step => step.Field == "Size").Text);
        Assert.DoesNotContain(script.Steps.OfType<SetBrushValueStep>(), step => step.Field == "Hardness");
    }

    [Fact]
    public void The_middle_gray_cells_are_left_without_a_stroke()
    {
        PixelBuffer picture = new(3, 1);
        byte[] grays = [0, 128, 255];
        for (int x = 0; x < 3; x++)
        {
            picture.Data[x * 4] = picture.Data[x * 4 + 1] = picture.Data[x * 4 + 2] = grays[x];
            picture.Data[x * 4 + 3] = 255;
        }

        IReadOnlyList<Rgb> palette = Layout().Swatches.ColorGrid!.Colors.Select(Rgb.Parse).ToList();
        StudioSettings settings = new(new PlacementSettings(FitMode.Stretch), new AdjustmentSettings(0, 0, 0, ImageFilter.None), ColorMode.Swatches, 16, Dithering.None, null, 2);

        PaintPlan plan = StudioPipeline.Run(picture, settings, 3, 1, palette);

        Assert.Equal([0, Quantizer.Unpainted, 1], plan.Cells);
        Assert.Equal(2, plan.Strokes.StrokeCount);
        Assert.Equal([0, 1], plan.Strokes.Passes.Select(pass => pass.PaletteIndex).Order());
    }

    [Fact]
    public void The_grid_follows_the_canvas_and_the_tenths_of_the_size()
    {
        (int width, int height) = StudioGeometry.Grid(Sculpture, 4);

        // 135 by 162 tenths of a unit in cells of four tenths.
        Assert.Equal(34, width);
        Assert.Equal(41, height);
    }

    [AvaloniaFact]
    public void Studio_turns_a_picture_into_carved_light_and_dark_and_explains_it()
    {
        StudioViewModel studio = new(Surfaces) { ReprocessDelay = TimeSpan.Zero };
        studio.SelectedSurface = studio.AllSurfaces.Single(option => option.Surface.Id == "stone-sculpture");

        Assert.Equal("Carving", studio.SelectedSurface.Category);
        Assert.True(studio.IsRelief);
        Assert.False(studio.IsCarvingNote);
        Assert.False(studio.HasChoosableColors);

        string path = Path.Combine(Path.GetTempPath(), $"rp-sculpture-{Guid.NewGuid():N}.png");
        TestImages.SavePng(TestImages.Rainbow(), path);
        Assert.True(studio.TryLoad(path));
        studio.ProcessNow();
        File.Delete(path);

        Assert.NotNull(studio.Plan);
        Assert.DoesNotContain(2, studio.Plan!.Strokes.Passes.Select(pass => pass.PaletteIndex));

        studio.SelectedSurface = studio.AllSurfaces.Single(option => option.Surface.Id == "pumpkin");
        Assert.False(studio.IsRelief);
        Assert.True(studio.IsCarvingNote);
    }
}
