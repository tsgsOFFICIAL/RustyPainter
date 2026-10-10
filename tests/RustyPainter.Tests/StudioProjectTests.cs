using Avalonia.Headless.XUnit;
using RustyPainter.Imaging;
using RustyPainter.Library;
using RustyPainter.Service;
using RustyPainter.ViewModels;

namespace RustyPainter.Tests;

public class StudioProjectTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"rustypainter-studio-library-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private ProjectStore Store() => new(_root);

    private StudioViewModel Studio(ProjectStore? store = null) => new(new SurfaceService(), projects: store ?? Store());

    private static string WritePicture(PixelBuffer pixels, string name = "photo.png")
    {
        string path = Path.Combine(Path.GetTempPath(), $"rp-{Guid.NewGuid():N}-{name}");
        TestImages.SavePng(pixels, path);
        return path;
    }

    private static StudioViewModel Loaded(StudioViewModel studio, PixelBuffer? pixels = null)
    {
        string path = WritePicture(pixels ?? TestImages.Rainbow(64, 32));
        Assert.True(studio.TryLoad(path));
        File.Delete(path);
        return studio;
    }

    private static void Customize(StudioViewModel studio)
    {
        studio.SelectedSurface = studio.AllSurfaces.Single(option => option.Surface.Id == "sign-large");
        studio.PlacementChoices.Select(FitMode.Fit);
        studio.Zoom = 1.5;
        studio.OffsetX = 12;
        studio.OffsetY = -7.5;
        studio.RotateCommand.Execute(null);
        studio.RotateCommand.Execute(null);
        studio.RotateCommand.Execute(null);
        studio.FlipHorizontalCommand.Execute(null);
        studio.ColorModeChoices.Select(ColorMode.Free);
        studio.MaxColors = 37;
        studio.DitheringChoices.Select(Dithering.Ordered);
        studio.FilterChoices.Select(ImageFilter.Sepia);
        studio.Brightness = 15;
        studio.Contrast = -10;
        studio.Saturation = 25;
        studio.FillBackground = true;
        studio.BackgroundHex = "#223344";
        studio.QualityChoices.Select(8);
    }

    [AvaloniaFact]
    public void Nothing_can_be_saved_without_a_picture_or_a_library()
    {
        Assert.False(Studio().SaveCommand.CanExecute(null));

        StudioViewModel withoutLibrary = Loaded(new StudioViewModel(new SurfaceService()));
        Assert.False(withoutLibrary.SaveCommand.CanExecute(null));

        StudioViewModel ready = Loaded(Studio());
        Assert.True(ready.SaveCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void Saving_puts_the_picture_the_thumbnail_and_the_settings_in_the_library()
    {
        ProjectStore store = Store();
        StudioViewModel studio = Loaded(Studio(store));
        studio.ProcessNow();

        studio.SaveCommand.Execute(null);

        ProjectEntry entry = Assert.Single(store.List());
        Assert.EndsWith("photo", entry.Project.Name);
        Assert.Equal("sign-small", entry.Project.SurfaceId);
        Assert.NotNull(entry.ThumbnailPath);
        Assert.True(new FileInfo(entry.ThumbnailPath!).Length > 0);
        Assert.Equal(entry.Project.Id, studio.CurrentProjectId);
        Assert.True(studio.IsSavedProject);
        Assert.True(studio.HasSaveStatus);
    }

    [AvaloniaFact]
    public void Saving_again_updates_the_same_project()
    {
        ProjectStore store = Store();
        StudioViewModel studio = Loaded(Studio(store));
        studio.SaveCommand.Execute(null);
        string id = studio.CurrentProjectId!;

        studio.Zoom = 2;
        studio.SaveCommand.Execute(null);

        ProjectEntry entry = Assert.Single(store.List());
        Assert.Equal(id, entry.Project.Id);
        Assert.Equal(2, entry.Project.Zoom);
    }

    [AvaloniaFact]
    public void Everything_comes_back_exactly_as_it_was_saved()
    {
        ProjectStore store = Store();
        StudioViewModel first = Loaded(Studio(store), TestImages.Rainbow(80, 40));
        Customize(first);
        PaintPlan before = first.ProcessNow()!;
        first.SaveCommand.Execute(null);
        string id = first.CurrentProjectId!;

        // A different Studio, set up completely differently.
        StudioViewModel second = Loaded(Studio(store), TestImages.RedAndGreen(10, 10));
        second.SelectedSurface = second.AllSurfaces.Single(option => option.Surface.Id == "canvas-xl");
        second.PlacementChoices.Select(FitMode.Stretch);
        second.ColorModeChoices.Select(ColorMode.Swatches);

        Assert.True(second.OpenProject(id));
        PaintPlan after = second.ProcessNow()!;

        Assert.Equal("sign-large", second.SelectedSurface.Surface.Id);
        Assert.Equal(FitMode.Fit, second.PlacementChoices.Selected);
        Assert.Equal((1.5, 12d, -7.5), (second.Zoom, second.OffsetX, second.OffsetY));
        Assert.Equal(ColorMode.Free, second.ColorModeChoices.Selected);
        Assert.Equal(37, second.MaxColors);
        Assert.Equal(Dithering.Ordered, second.DitheringChoices.Selected);
        Assert.Equal(ImageFilter.Sepia, second.FilterChoices.Selected);
        Assert.Equal((15d, -10d, 25d), (second.Brightness, second.Contrast, second.Saturation));
        Assert.True(second.FillBackground);
        Assert.Equal("#223344", second.BackgroundHex);
        Assert.Equal(8, second.BlockSize);
        Assert.Equal(id, second.CurrentProjectId);

        // The plan, which depends on all of it together, is the same down to every cell.
        Assert.Equal(before.Width, after.Width);
        Assert.Equal(before.Cells, after.Cells);
        Assert.Equal(before.Palette, after.Palette);
    }

    [AvaloniaFact]
    public void A_neon_project_keeps_its_brush()
    {
        ProjectStore store = Store();
        StudioViewModel first = Loaded(Studio(store));
        first.SelectedSurface = first.AllSurfaces.Single(option => option.Surface.Id == "neon-medium");
        first.PresetChoices.Select(3);
        first.SaveCommand.Execute(null);

        StudioViewModel second = Studio(store);
        Assert.True(second.OpenProject(first.CurrentProjectId!));

        Assert.True(second.IsPresetSurface);
        Assert.Equal(3, second.PresetChoices.Selected);
        Assert.Equal(3, second.BlockSize);
    }

    [AvaloniaFact]
    public void A_text_project_comes_back_as_editable_text()
    {
        ProjectStore store = Store();
        StudioViewModel first = Studio(store);
        first.SourceChoices.Select(SourceKind.Text);
        first.TextColorHex = "#FF8800";
        first.TextWeightChoices.Select(false);
        first.TextBackgroundChoices.Select(true);
        first.TextBackgroundHex = "#101020";
        first.Text = "RUST\nPAINTER";
        first.ProcessNow();
        first.SaveCommand.Execute(null);

        StudioViewModel second = Studio(store);
        Assert.True(second.OpenProject(first.CurrentProjectId!));

        Assert.True(second.IsTextSource);
        Assert.Equal("RUST\nPAINTER", second.Text);
        Assert.Equal("#FF8800", second.TextColorHex);
        Assert.False(second.TextWeightChoices.Selected);
        Assert.True(second.IsTextBackgroundSolid);
        Assert.Equal("#101020", second.TextBackgroundHex);
        Assert.True(second.HasImage);
        Assert.Equal("Text", second.ImageName);
        Assert.StartsWith("RUST", store.List().Single().Project.Name);

        // Editing the text after opening carries on with the same project.
        second.Text = "NEW TEXT";
        second.SaveCommand.Execute(null);
        Assert.Single(store.List());
        Assert.Equal("NEW TEXT", store.List().Single().Project.Text);
    }

    [AvaloniaFact]
    public void Putting_in_another_picture_starts_a_new_project()
    {
        ProjectStore store = Store();
        StudioViewModel studio = Loaded(Studio(store));
        studio.SaveCommand.Execute(null);
        Assert.True(studio.IsSavedProject);

        Loaded(studio, TestImages.RedAndGreen(8, 8));
        Assert.False(studio.IsSavedProject);

        studio.SaveCommand.Execute(null);
        Assert.Equal(2, store.List().Count);
    }

    [AvaloniaFact]
    public void Removing_the_picture_forgets_the_open_project()
    {
        StudioViewModel studio = Loaded(Studio());
        studio.SaveCommand.Execute(null);

        studio.ClearImageCommand.Execute(null);

        Assert.False(studio.IsSavedProject);
        Assert.False(studio.SaveCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void A_project_that_is_gone_is_explained()
    {
        StudioViewModel studio = Studio();

        Assert.False(studio.OpenProject("nothinghere"));

        Assert.True(studio.HasError);
    }

    [AvaloniaFact]
    public void A_surface_that_no_longer_exists_keeps_the_current_one_and_says_so()
    {
        ProjectStore store = Store();
        StudioViewModel first = Loaded(Studio(store));
        first.SaveCommand.Execute(null);
        StudioProject saved = store.List().Single().Project;
        byte[] picture = store.Load(saved.Id)!.Value.Picture;
        store.Save(saved with { SurfaceId = "a-sign-that-was-removed" }, picture, null);

        StudioViewModel second = Studio(store);
        second.SelectedSurface = second.AllSurfaces.Single(option => option.Surface.Id == "canvas-large");

        Assert.True(second.OpenProject(saved.Id));

        Assert.Equal("canvas-large", second.SelectedSurface.Surface.Id);
        Assert.Contains("not known any more", second.ErrorText);
        Assert.True(second.HasImage);
    }

    [AvaloniaFact]
    public void Settings_from_a_newer_version_with_unknown_values_do_not_break_opening()
    {
        ProjectStore store = Store();
        StudioViewModel first = Loaded(Studio(store));
        first.SaveCommand.Execute(null);
        StudioProject saved = store.List().Single().Project;
        byte[] picture = store.Load(saved.Id)!.Value.Picture;
        store.Save(saved with { Placement = "Teleport", ColorMode = "Quantum", Dithering = "Magic", Filter = "Glitter", QuarterTurns = 99, Quality = 9999 }, picture, null);

        StudioViewModel second = Studio(store);

        Assert.True(second.OpenProject(saved.Id));
        Assert.NotNull(second.ProcessNow());
        Assert.InRange(second.BlockSize, 1, 32);
    }

    [AvaloniaFact]
    public void The_thumbnail_is_small_and_shows_the_result()
    {
        ProjectStore store = Store();
        StudioViewModel studio = Loaded(Studio(store), TestImages.Rainbow(600, 300));
        studio.QualityChoices.Select(1);
        studio.ProcessNow();
        studio.SaveCommand.Execute(null);

        using var thumbnail = new Avalonia.Media.Imaging.Bitmap(store.List().Single().ThumbnailPath!);

        Assert.True(Math.Max(thumbnail.PixelSize.Width, thumbnail.PixelSize.Height) <= StudioViewModel.ThumbnailSize);
        Assert.True(thumbnail.PixelSize.Width >= 100);
    }
}
