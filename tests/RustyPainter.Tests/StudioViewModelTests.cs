using RustyPainter.Service;
using RustyPainter.ViewModels;

namespace RustyPainter.Tests;

public class StudioViewModelTests
{
    private static StudioViewModel Create() => new(new SurfaceService());

    [Theory]
    [InlineData("a.png", true)]
    [InlineData("a.PNG", true)]
    [InlineData("a.jpg", true)]
    [InlineData("a.jpeg", true)]
    [InlineData("a.webp", true)]
    [InlineData("a.bmp", true)]
    [InlineData("a.gif", false)]
    [InlineData("a.tiff", false)]
    [InlineData("a.txt", false)]
    [InlineData("noextension", false)]
    public void Only_png_jpg_webp_and_bmp_are_supported(string path, bool expected)
    {
        Assert.Equal(expected, StudioViewModel.IsSupported(path));
    }

    [Fact]
    public void Lists_every_surface_and_selects_the_first()
    {
        StudioViewModel studio = Create();

        Assert.Equal(16, studio.AllSurfaces.Count);
        Assert.Equal(studio.AllSurfaces[0], studio.SelectedSurface);
        Assert.Equal(studio.AllSurfaces.Count, studio.VisibleSurfaces.Count);
    }

    [Fact]
    public void Offers_a_chip_per_category_with_all_selected()
    {
        StudioViewModel studio = Create();

        Assert.Equal(["All", "Signs", "Canvases", "Frames", "Neon", "Animated"], studio.Categories.Select(chip => chip.Name));
        Assert.Equal(["All"], studio.Categories.Where(chip => chip.IsSelected).Select(chip => chip.Name));
    }

    [Fact]
    public void Filtering_by_category_narrows_the_list_and_moves_the_chip()
    {
        StudioViewModel studio = Create();

        studio.SelectedCategory = "Neon";

        Assert.All(studio.VisibleSurfaces, option => Assert.Equal("Neon", option.Category));
        Assert.Equal(3, studio.VisibleSurfaces.Count);
        Assert.Equal(["Neon"], studio.Categories.Where(chip => chip.IsSelected).Select(chip => chip.Name));
    }

    [Fact]
    public void Filtering_keeps_a_valid_selection()
    {
        StudioViewModel studio = Create();
        Assert.Equal("Signs", studio.SelectedSurface.Category);

        studio.SelectedCategory = "Neon";

        Assert.Equal("Neon", studio.SelectedSurface.Category);
    }

    [Fact]
    public void Selecting_a_surface_updates_the_canvas_size()
    {
        StudioViewModel studio = Create();
        SurfaceOption large = studio.AllSurfaces.Single(option => option.Surface.Id == "neon-large");

        studio.SelectedSurface = large;

        Assert.Equal(large.Surface.Canvas.Width, studio.CanvasWidth, 3);
        Assert.Equal(large.Surface.Canvas.Height, studio.CanvasHeight, 3);
        Assert.Equal("891 x 890 px", studio.CanvasText);
    }

    [Fact]
    public void Warns_when_a_canvas_needs_scrolling_out_in_the_game()
    {
        StudioViewModel studio = Create();
        Assert.False(studio.HasScrollHint);

        studio.SelectedSurface = studio.AllSurfaces.Single(option => option.Surface.Id == "canvas-xxl");

        Assert.True(studio.HasScrollHint);
        Assert.Equal("Needs 11 scroll-outs in the game first.", studio.ScrollHint);

        studio.SelectedSurface = studio.AllSurfaces.Single(option => option.Surface.Id == "sign-small");
        Assert.False(studio.HasScrollHint);
    }

    [Fact]
    public void Starts_without_a_picture_and_without_an_error()
    {
        StudioViewModel studio = Create();

        Assert.False(studio.HasImage);
        Assert.True(studio.NoImage);
        Assert.False(studio.HasError);
        Assert.False(studio.ClearImageCommand.CanExecute(null));
    }

    [Fact]
    public void Refuses_a_gif_and_says_why()
    {
        StudioViewModel studio = Create();

        bool loaded = studio.TryLoad(@"C:\pictures\funny.gif");

        Assert.False(loaded);
        Assert.False(studio.HasImage);
        Assert.True(studio.HasError);
        Assert.Contains("funny.gif", studio.ErrorText);
        Assert.Contains("PNG, JPG, WebP or BMP", studio.ErrorText);
    }

    [Fact]
    public void Reports_a_missing_file_without_throwing()
    {
        StudioViewModel studio = Create();

        bool loaded = studio.TryLoad(Path.Combine(Path.GetTempPath(), "rustypainter-does-not-exist.png"));

        Assert.False(loaded);
        Assert.False(studio.HasImage);
        Assert.Contains("could not be opened", studio.ErrorText);
    }

    [Fact]
    public void Reports_a_file_that_is_not_really_a_picture()
    {
        string path = Path.Combine(Path.GetTempPath(), $"rustypainter-test-{Guid.NewGuid():N}.png");
        File.WriteAllText(path, "this is not a png");

        try
        {
            StudioViewModel studio = Create();

            Assert.False(studio.TryLoad(path));
            Assert.False(studio.HasImage);
            Assert.True(studio.HasError);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Nothing_to_load_is_reported()
    {
        StudioViewModel studio = Create();

        studio.ReportNothingToLoad();

        Assert.True(studio.HasError);
    }
}
