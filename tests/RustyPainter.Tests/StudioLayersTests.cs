using Avalonia.Headless.XUnit;
using RustyPainter.Imaging;
using RustyPainter.Library;
using RustyPainter.Service;
using RustyPainter.ViewModels;

namespace RustyPainter.Tests;

public class LayerCompositingTests
{
    private static readonly IReadOnlyList<Rgb> Swatches = new SurfaceService().Catalog.Layouts["standard"].Swatches.ColorGrid!.Colors.Select(Rgb.Parse).ToList();

    private static StudioSettings Free() => new(new PlacementSettings(), new AdjustmentSettings(), ColorMode.Free, 16, Dithering.None);

    [Fact]
    public void A_solid_layer_covers_the_one_under_it()
    {
        PixelBuffer bottom = PixelBuffer.Solid(1, 1, 255, 0, 0);
        PixelBuffer top = PixelBuffer.Solid(1, 1, 0, 0, 255);

        StudioPipeline.Overlay(bottom, top);

        Assert.Equal([0, 0, 255, 255], bottom.Data);
    }

    [Fact]
    public void A_clear_layer_leaves_the_one_under_it_alone()
    {
        PixelBuffer bottom = PixelBuffer.Solid(1, 1, 255, 0, 0);

        StudioPipeline.Overlay(bottom, PixelBuffer.Solid(1, 1, 0, 0, 255, 0));

        Assert.Equal([255, 0, 0, 255], bottom.Data);
    }

    [Fact]
    public void A_half_clear_layer_blends_with_the_one_under_it()
    {
        PixelBuffer bottom = PixelBuffer.Solid(1, 1, 255, 0, 0);

        StudioPipeline.Overlay(bottom, PixelBuffer.Solid(1, 1, 0, 0, 255, 128));

        Assert.Equal(255, bottom.Data[3]);
        Assert.InRange(bottom.Data[0], 126, 128);
        Assert.InRange(bottom.Data[2], 127, 129);
    }

    [Fact]
    public void Where_nothing_is_under_a_half_clear_layer_it_stays_half_clear()
    {
        PixelBuffer bottom = new(1, 1);

        StudioPipeline.Overlay(bottom, PixelBuffer.Solid(1, 1, 10, 20, 30, 100));

        Assert.Equal([10, 20, 30, 100], bottom.Data);
    }

    [Fact]
    public void A_smaller_layer_sits_over_the_middle_of_a_bigger_one()
    {
        PlacedPicture background = new(PixelBuffer.Solid(1, 1, 255, 0, 0), new PlacementSettings(FitMode.Fill));
        PlacedPicture badge = new(PixelBuffer.Solid(1, 1, 0, 0, 255), new PlacementSettings(FitMode.Fit, Zoom: 0.5));

        PaintPlan plan = StudioPipeline.Run([background, badge], Free(), 8, 8, Swatches);

        Assert.Equal(64, plan.PaintedCells);
        Assert.Equal(2, plan.ColorsUsed);
        Assert.NotEqual(plan.Cells[0], plan.Cells[4 * 8 + 4]);
        Assert.Equal(new Rgb(255, 0, 0), plan.Palette[plan.Cells[0]]);
        Assert.Equal(new Rgb(0, 0, 255), plan.Palette[plan.Cells[4 * 8 + 4]]);
    }

    [Fact]
    public void Every_layer_has_its_own_place()
    {
        PlacedPicture left = new(PixelBuffer.Solid(1, 1, 255, 0, 0), new PlacementSettings(FitMode.Fit, Zoom: 0.25, OffsetX: -0.25));
        PlacedPicture right = new(PixelBuffer.Solid(1, 1, 0, 0, 255), new PlacementSettings(FitMode.Fit, Zoom: 0.25, OffsetX: 0.25));

        PaintPlan plan = StudioPipeline.Run([left, right], Free(), 8, 8, Swatches);

        Assert.Equal(new Rgb(255, 0, 0), plan.Palette[plan.Cells[3 * 8 + 1]]);
        Assert.Equal(new Rgb(0, 0, 255), plan.Palette[plan.Cells[3 * 8 + 6]]);
        Assert.Equal(Quantizer.Unpainted, plan.Cells[0]);
    }

    [Fact]
    public void A_background_goes_under_all_layers()
    {
        PlacedPicture badge = new(PixelBuffer.Solid(1, 1, 255, 0, 0), new PlacementSettings(FitMode.Fit, Zoom: 0.5));
        StudioSettings settings = Free() with { Background = new Rgb(0, 0, 0) };

        PaintPlan plan = StudioPipeline.Run([badge, badge], settings, 8, 8, Swatches);

        Assert.Equal(64, plan.PaintedCells);
    }

    [Fact]
    public void The_untouched_reference_shows_all_layers_too()
    {
        PlacedPicture background = new(PixelBuffer.Solid(1, 1, 255, 0, 0), new PlacementSettings(FitMode.Fill));
        PlacedPicture badge = new(PixelBuffer.Solid(1, 1, 0, 0, 255), new PlacementSettings(FitMode.Fit, Zoom: 0.5));

        PixelBuffer reference = StudioPipeline.Run([background, badge], Free(), 8, 8, Swatches).Reference!;

        int middle = reference.OffsetOf(reference.Width / 2, reference.Height / 2);
        Assert.Equal([0, 0, 255], reference.Data[middle..(middle + 3)]);
        Assert.Equal([255, 0, 0], reference.Data[0..3]);
    }

    [Fact]
    public void One_layer_gives_the_same_plan_as_the_single_picture_way()
    {
        PixelBuffer picture = TestImages.RedAndGreen(8, 4);
        StudioSettings settings = Free();

        PaintPlan single = StudioPipeline.Run(picture, settings, 6, 6, Swatches);
        PaintPlan layered = StudioPipeline.Run([new PlacedPicture(picture, settings.Placement)], settings, 6, 6, Swatches);

        Assert.Equal(single.Cells, layered.Cells);
    }
}

public class StudioLayersTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"rustypainter-layers-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private StudioViewModel Studio(string surface = "sign-large")
    {
        StudioViewModel studio = new(new SurfaceService(), projects: new ProjectStore(_root)) { ReprocessDelay = TimeSpan.Zero };
        studio.SelectedSurface = studio.AllSurfaces.Single(option => option.Surface.Id == surface);
        return studio;
    }

    private static void Load(StudioViewModel studio, PixelBuffer pixels, string name = "photo.png")
    {
        string path = Path.Combine(Path.GetTempPath(), $"rp-{Guid.NewGuid():N}-{name}");
        TestImages.SavePng(pixels, path);
        Assert.True(studio.TryLoad(path));
        studio.ProcessNow();
        File.Delete(path);
    }

    private static PixelBuffer Solid(byte r, byte g, byte b) => PixelBuffer.Solid(8, 8, r, g, b);

    /// <summary>A red picture with a small blue one over it, a half-size badge in the middle.</summary>
    private static StudioViewModel RedWithBlueBadge(StudioViewModel studio)
    {
        Load(studio, Solid(255, 0, 0), "red.png");
        studio.AddLayer();
        Load(studio, Solid(0, 0, 255), "blue.png");
        studio.PlacementChoices.Select(FitMode.Fit);
        studio.Zoom = 0.5;
        studio.ProcessNow();
        return studio;
    }

    private static Rgb CellColor(PaintPlan plan, int x, int y) => plan.Palette[plan.Cells[y * plan.Width + x]];

    [AvaloniaFact]
    public void A_painting_starts_with_one_layer()
    {
        StudioViewModel studio = Studio();
        Load(studio, Solid(255, 0, 0));

        Assert.Equal(1, studio.LayerCount);
        Assert.False(studio.HasMultipleLayers);
        Assert.True(studio.CanAddLayer);
        Assert.False(studio.CanRemoveLayer);
        Assert.False(studio.CanMoveLayerUp);
        Assert.False(studio.CanMoveLayerDown);
    }

    [AvaloniaFact]
    public void A_layer_needs_a_picture_before_another_can_go_over_it()
    {
        StudioViewModel studio = Studio();

        Assert.False(studio.CanAddLayer);
        studio.AddLayer();
        Assert.Equal(1, studio.LayerCount);
    }

    [AvaloniaFact]
    public void A_new_layer_is_empty_and_goes_on_top()
    {
        StudioViewModel studio = Studio();
        Load(studio, Solid(255, 0, 0), "red.png");

        studio.AddLayer();

        Assert.Equal(2, studio.LayerCount);
        Assert.Equal(1, studio.LayerIndex);
        Assert.False(studio.HasImage);
        Assert.True(studio.HasAnyPicture);

        // The top one is listed first, as it sits in front.
        Assert.Equal([2, 1], studio.LayerChips.Select(chip => chip.Number));
        Assert.Equal([true, false], studio.LayerChips.Select(chip => chip.IsSelected));
        Assert.EndsWith("red.png", studio.LayerChips[1].Label);
    }

    [AvaloniaFact]
    public void The_layers_below_still_show_while_an_empty_layer_is_open()
    {
        StudioViewModel studio = Studio();
        Load(studio, Solid(255, 0, 0));
        studio.AddLayer();

        PaintPlan? plan = studio.ProcessNow();

        Assert.NotNull(plan);
        Assert.True(plan.PaintedCells > 0);
        Assert.True(studio.CanPaint || studio.IsBusy);
    }

    [AvaloniaFact]
    public void A_picture_over_a_picture_ends_up_in_one_painting()
    {
        StudioViewModel studio = RedWithBlueBadge(Studio());

        PaintPlan plan = studio.Plan!;

        Assert.NotEqual(CellColor(plan, 0, 0), CellColor(plan, plan.Width / 2, plan.Height / 2));
        Assert.Equal(2, plan.ColorsUsed);
    }

    [AvaloniaFact]
    public void Each_layer_keeps_its_own_picture_and_place()
    {
        StudioViewModel studio = RedWithBlueBadge(Studio());

        studio.SelectLayer(0);
        Assert.EndsWith("red.png", studio.ImageName);
        Assert.Equal(1, studio.Zoom);
        Assert.Equal(FitMode.Fill, studio.PlacementChoices.Selected);

        studio.SelectLayer(1);
        Assert.EndsWith("blue.png", studio.ImageName);
        Assert.Equal(0.5, studio.Zoom);
        Assert.Equal(FitMode.Fit, studio.PlacementChoices.Selected);
    }

    [AvaloniaFact]
    public void Moving_the_open_layer_changes_only_that_layer()
    {
        StudioViewModel studio = RedWithBlueBadge(Studio());
        studio.OffsetX = -30;
        PaintPlan moved = studio.ProcessNow()!;

        // The badge is no longer in the middle, but the red picture still fills the canvas.
        Assert.Equal(moved.Width * moved.Height, moved.PaintedCells);
        Assert.NotEqual(CellColor(moved, 0, 0), CellColor(moved, moved.Width / 4, moved.Height / 2));
        Assert.Equal(CellColor(moved, 0, 0), CellColor(moved, moved.Width - 1, moved.Height / 2));
    }

    [AvaloniaFact]
    public void A_layer_can_be_moved_over_and_under_the_others()
    {
        StudioViewModel studio = RedWithBlueBadge(Studio());
        Assert.True(studio.CanMoveLayerDown);
        Assert.False(studio.CanMoveLayerUp);

        studio.MoveLayer(-1);

        // The blue badge is at the bottom now, hidden behind the red picture that fills the canvas.
        Assert.Equal(0, studio.LayerIndex);
        Assert.EndsWith("blue.png", studio.ImageName);
        PaintPlan plan = studio.ProcessNow()!;
        Assert.Equal(1, plan.ColorsUsed);

        studio.MoveLayer(1);
        Assert.Equal(1, studio.LayerIndex);
        Assert.Equal(2, studio.ProcessNow()!.ColorsUsed);
    }

    [AvaloniaFact]
    public void Dragging_a_layer_shows_the_layers_under_it_itself_and_the_layers_over_it()
    {
        StudioViewModel studio = RedWithBlueBadge(Studio());
        studio.AddLayer();
        Load(studio, Solid(0, 255, 0), "green.png");
        studio.SelectLayer(1);
        studio.ProcessNow();
        Assert.True(studio.ShowRender);

        studio.BeginLayerDrag();

        Assert.True(studio.IsLayerDrag);
        Assert.False(studio.ShowRender);
        Assert.NotNull(studio.DragUnder);
        Assert.NotNull(studio.DragLayer);
        Assert.NotNull(studio.DragOver);

        studio.EndLayerDrag();

        Assert.False(studio.IsLayerDrag);
        Assert.True(studio.ShowRender);
        Assert.Null(studio.DragLayer);
    }

    [AvaloniaFact]
    public void The_top_layer_has_nothing_over_it_while_dragged_and_one_layer_is_never_split()
    {
        StudioViewModel studio = RedWithBlueBadge(Studio());
        studio.BeginLayerDrag();
        Assert.NotNull(studio.DragUnder);
        Assert.Null(studio.DragOver);
        studio.EndLayerDrag();

        studio.RemoveLayer();
        studio.BeginLayerDrag();
        Assert.False(studio.IsLayerDrag);
    }

    [AvaloniaFact]
    public void Removing_a_layer_opens_the_one_below()
    {
        StudioViewModel studio = RedWithBlueBadge(Studio());

        studio.RemoveLayer();

        Assert.Equal(1, studio.LayerCount);
        Assert.Equal(0, studio.LayerIndex);
        Assert.EndsWith("red.png", studio.ImageName);
        Assert.Equal(1, studio.ProcessNow()!.ColorsUsed);
    }

    [AvaloniaFact]
    public void The_remove_button_for_a_picture_takes_its_layer_away_when_there_are_several()
    {
        StudioViewModel studio = RedWithBlueBadge(Studio());

        studio.ClearImageCommand.Execute(null);

        Assert.Equal(1, studio.LayerCount);
        Assert.EndsWith("red.png", studio.ImageName);
    }

    [AvaloniaFact]
    public void The_number_of_layers_is_limited()
    {
        StudioViewModel studio = Studio();
        Load(studio, Solid(255, 0, 0));

        for (int i = 1; i < StudioViewModel.MaxLayers; i++)
        {
            Assert.True(studio.CanAddLayer);
            studio.AddLayer();
            Load(studio, Solid((byte)(i * 20), 0, 0), $"{i}.png");
        }

        Assert.Equal(StudioViewModel.MaxLayers, studio.LayerCount);
        Assert.False(studio.CanAddLayer);
    }

    [AvaloniaFact]
    public void Text_can_go_over_a_picture()
    {
        StudioViewModel studio = Studio();
        Load(studio, Solid(0, 0, 255), "sky.png");
        studio.AddLayer();

        studio.SourceChoices.Select(SourceKind.Text);
        studio.Text = "HI";
        studio.ProcessNow();

        Assert.Equal(2, studio.LayerCount);
        Assert.True(studio.HasImage);
        Assert.Equal("Text", studio.ImageName);

        // The sky fills the canvas, and the white letters over it make a second color.
        PaintPlan plan = studio.Plan!;
        Assert.Equal(plan.Width * plan.Height, plan.PaintedCells);
        Assert.True(plan.ColorsUsed >= 2);

        // Going back to the picture brings its own settings back, not the text's.
        studio.SelectLayer(0);
        Assert.False(studio.IsTextSource);
        Assert.EndsWith("sky.png", studio.ImageName);
    }

    [AvaloniaFact]
    public void Layers_are_saved_and_come_back()
    {
        StudioViewModel studio = RedWithBlueBadge(Studio());
        studio.SaveCommand.Execute(null);
        string id = studio.CurrentProjectId!;

        ProjectStore store = new(_root);
        StudioProject saved = store.Load(id)!.Value.Project;
        Assert.EndsWith("red.png", saved.SourceName);
        Assert.EndsWith("blue.png", saved.MoreLayers.Single().SourceName);
        Assert.Equal(0.5, saved.MoreLayers.Single().Zoom);
        Assert.NotNull(store.LoadLayerPictures(id).Single().Single());

        StudioViewModel again = new(new SurfaceService(), projects: store) { ReprocessDelay = TimeSpan.Zero };
        Assert.True(again.OpenProject(id));
        PaintPlan plan = again.ProcessNow()!;

        Assert.Equal(2, again.LayerCount);
        Assert.Equal(2, plan.ColorsUsed);
        Assert.EndsWith("red.png", again.ImageName);

        again.SelectLayer(1);
        Assert.EndsWith("blue.png", again.ImageName);
        Assert.Equal(0.5, again.Zoom);
    }

    [AvaloniaFact]
    public void Saving_after_changing_a_layer_updates_the_same_project()
    {
        StudioViewModel studio = RedWithBlueBadge(Studio());
        studio.SaveCommand.Execute(null);
        string id = studio.CurrentProjectId!;

        // Putting another picture on a layer is editing the painting, not starting a new one.
        Load(studio, Solid(0, 255, 0), "green.png");
        Assert.Equal(id, studio.CurrentProjectId);
        studio.SaveCommand.Execute(null);

        Assert.Single(new ProjectStore(_root).List());
        Assert.EndsWith("green.png", new ProjectStore(_root).Load(id)!.Value.Project.MoreLayers.Single().SourceName);
    }

    [AvaloniaFact]
    public void A_layer_that_was_removed_is_gone_from_the_saved_project()
    {
        StudioViewModel studio = RedWithBlueBadge(Studio());
        studio.SaveCommand.Execute(null);
        string id = studio.CurrentProjectId!;
        Assert.Single(Directory.GetFiles(Path.Combine(_root, id), "layer-*.png"));

        studio.RemoveLayer();
        studio.SaveCommand.Execute(null);

        Assert.Empty(Directory.GetFiles(Path.Combine(_root, id), "layer-*.png"));
        Assert.Empty(new ProjectStore(_root).Load(id)!.Value.Project.MoreLayers);
    }

    [AvaloniaFact]
    public void A_copy_of_a_project_keeps_its_layers()
    {
        StudioViewModel studio = RedWithBlueBadge(Studio());
        studio.SaveCommand.Execute(null);

        ProjectStore store = new(_root);
        StudioProject copy = store.Duplicate(studio.CurrentProjectId!, "Copy")!;

        Assert.Single(copy.MoreLayers);
        Assert.Single(store.LoadLayerPictures(copy.Id).Single());
    }

    [AvaloniaFact]
    public void A_project_from_before_layers_opens_with_one_layer()
    {
        StudioViewModel studio = Studio();
        Load(studio, Solid(255, 0, 0));
        studio.SaveCommand.Execute(null);
        string id = studio.CurrentProjectId!;

        string path = Path.Combine(_root, id, "project.json");
        File.WriteAllText(path, System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(path), ",\\s*\"MoreLayers\":\\s*\\[\\s*\\]", ""));
        Assert.DoesNotContain("MoreLayers", File.ReadAllText(path));

        StudioViewModel again = new(new SurfaceService(), projects: new ProjectStore(_root)) { ReprocessDelay = TimeSpan.Zero };
        Assert.True(again.OpenProject(id));
        Assert.Equal(1, again.LayerCount);
    }

    [AvaloniaFact]
    public void Every_frame_of_an_animated_sign_has_its_own_layers()
    {
        StudioViewModel studio = Studio("animated-neon-large");
        Load(studio, Solid(255, 0, 0), "red.png");
        studio.AddLayer();
        Load(studio, Solid(0, 0, 255), "blue.png");

        studio.AddFrame();
        Assert.Equal(2, studio.FrameCount);
        Assert.Equal(2, studio.LayerCount);

        // The new frame is a copy, so changing its top layer leaves the first frame's alone.
        Load(studio, Solid(0, 255, 0), "green.png");
        studio.AddLayer();
        Load(studio, Solid(255, 255, 0), "yellow.png");
        Assert.Equal(3, studio.LayerCount);

        studio.SelectFrame(0);
        Assert.Equal(2, studio.LayerCount);
        Assert.EndsWith("blue.png", studio.ImageName);

        studio.SelectFrame(1);
        Assert.Equal(3, studio.LayerCount);
        Assert.EndsWith("yellow.png", studio.ImageName);
    }

    [AvaloniaFact]
    public void The_layers_of_all_frames_are_saved_and_come_back()
    {
        StudioViewModel studio = Studio("animated-neon-large");
        Load(studio, Solid(255, 0, 0), "red.png");
        studio.AddLayer();
        Load(studio, Solid(0, 0, 255), "blue.png");
        studio.AddFrame();
        studio.AddLayer();
        Load(studio, Solid(255, 255, 0), "yellow.png");

        studio.SaveCommand.Execute(null);
        string id = studio.CurrentProjectId!;

        ProjectStore store = new(_root);
        StudioProject saved = store.Load(id)!.Value.Project;
        Assert.Single(saved.MoreLayers);
        Assert.Single(saved.MoreFrames);
        Assert.Equal(2, saved.MoreFrames[0].MoreLayers.Count);
        Assert.Equal([1, 2], store.LoadLayerPictures(id).Select(frame => frame.Count));

        StudioViewModel again = new(new SurfaceService(), projects: store) { ReprocessDelay = TimeSpan.Zero };
        Assert.True(again.OpenProject(id));
        Assert.Equal(2, again.FrameCount);
        Assert.Equal(2, again.LayerCount);

        again.SelectFrame(1);
        Assert.Equal(3, again.LayerCount);
        again.SelectLayer(2);
        Assert.EndsWith("yellow.png", again.ImageName);
    }

    [AvaloniaFact]
    public async Task All_layers_are_part_of_every_frame_plan()
    {
        StudioViewModel studio = Studio("animated-neon-large");
        Load(studio, Solid(255, 0, 0), "red.png");
        studio.AddLayer();
        Load(studio, Solid(0, 0, 255), "blue.png");
        studio.PlacementChoices.Select(FitMode.Fit);
        studio.Zoom = 0.5;
        studio.AddFrame();

        IReadOnlyList<PaintPlan> plans = await studio.PreparePlansAsync();

        Assert.Equal(2, plans.Count);
        Assert.All(plans, plan => Assert.Equal(2, plan.ColorsUsed));
    }
}
