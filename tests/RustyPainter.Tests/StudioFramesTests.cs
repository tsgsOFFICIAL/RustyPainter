using Avalonia.Headless.XUnit;
using RustyPainter.Imaging;
using RustyPainter.Library;
using RustyPainter.Painting;
using RustyPainter.Service;
using RustyPainter.ViewModels;

namespace RustyPainter.Tests;

public class StudioFramesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"rustypainter-frames-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private StudioViewModel Studio(bool animated = true)
    {
        StudioViewModel studio = new(new SurfaceService(), projects: new ProjectStore(_root)) { ReprocessDelay = TimeSpan.Zero };
        studio.SelectedSurface = studio.AllSurfaces.Single(option => option.Surface.Id == (animated ? "animated-neon-large" : "neon-large"));
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

    private static PixelBuffer Solid(byte r, byte g, byte b)
    {
        PixelBuffer pixels = new(8, 8);
        for (int i = 0; i < 64; i++)
        {
            pixels.Data[i * 4] = r;
            pixels.Data[i * 4 + 1] = g;
            pixels.Data[i * 4 + 2] = b;
            pixels.Data[i * 4 + 3] = 255;
        }

        return pixels;
    }

    [AvaloniaFact]
    public void Only_animated_signs_have_frames()
    {
        StudioViewModel plain = Studio(animated: false);
        Assert.False(plain.IsAnimatedSurface);
        Assert.False(plain.CanAddFrame);
        Assert.False(plain.CanPlay);

        StudioViewModel animated = Studio();
        Assert.True(animated.IsAnimatedSurface);
        Assert.Equal(5, animated.MaxFrames);
        Assert.Equal(1, animated.FrameCount);
        Assert.True(animated.CanAddFrame);
        Assert.False(animated.CanRemoveFrame);
        Assert.False(animated.CanPlay);
    }

    [AvaloniaFact]
    public void A_new_frame_starts_as_a_copy_and_is_opened()
    {
        StudioViewModel studio = Studio();
        Load(studio, Solid(255, 0, 0), "red.png");

        studio.AddFrame();

        Assert.Equal(2, studio.FrameCount);
        Assert.Equal(1, studio.FrameIndex);
        Assert.Equal([false, true], studio.FrameChips.Select(chip => chip.IsSelected));
        Assert.Equal([1, 2], studio.FrameChips.Select(chip => chip.Number));
        Assert.True(studio.CanRemoveFrame);
        Assert.True(studio.CanPlay);
    }

    [AvaloniaFact]
    public void Every_frame_keeps_its_own_picture()
    {
        StudioViewModel studio = Studio();
        Load(studio, Solid(255, 0, 0), "red.png");
        studio.AddFrame();
        Load(studio, Solid(0, 0, 255), "blue.png");

        Assert.EndsWith("blue.png", studio.ImageName);

        studio.SelectFrame(0);
        Assert.EndsWith("red.png", studio.ImageName);
        Assert.Equal(0, studio.FrameIndex);

        studio.SelectFrame(1);
        Assert.EndsWith("blue.png", studio.ImageName);
    }

    [AvaloniaFact]
    public void Every_frame_keeps_its_own_place_on_the_canvas()
    {
        StudioViewModel studio = Studio();
        Load(studio, Solid(255, 0, 0));
        studio.Zoom = 1.5;
        studio.OffsetX = 12;

        studio.AddFrame();
        studio.Zoom = 2;
        studio.OffsetX = -30;

        studio.SelectFrame(0);
        Assert.Equal(1.5, studio.Zoom);
        Assert.Equal(12, studio.OffsetX);

        studio.SelectFrame(1);
        Assert.Equal(2, studio.Zoom);
        Assert.Equal(-30, studio.OffsetX);
    }

    [AvaloniaFact]
    public void There_are_never_more_frames_than_the_sign_has_and_the_last_one_stays()
    {
        StudioViewModel studio = Studio();
        Load(studio, Solid(255, 0, 0));

        for (int i = 0; i < 10; i++)
            studio.AddFrame();

        Assert.Equal(5, studio.FrameCount);
        Assert.False(studio.CanAddFrame);

        for (int i = 0; i < 10; i++)
            studio.RemoveFrame();

        Assert.Equal(1, studio.FrameCount);
        Assert.Equal(0, studio.FrameIndex);
        Assert.False(studio.CanRemoveFrame);
    }

    [AvaloniaFact]
    public void Removing_a_frame_opens_the_one_before_it()
    {
        StudioViewModel studio = Studio();
        Load(studio, Solid(255, 0, 0), "one.png");
        studio.AddFrame();
        Load(studio, Solid(0, 255, 0), "two.png");
        studio.AddFrame();
        Load(studio, Solid(0, 0, 255), "three.png");

        studio.RemoveFrame();

        Assert.Equal(2, studio.FrameCount);
        Assert.Equal(1, studio.FrameIndex);
        Assert.EndsWith("two.png", studio.ImageName);
    }

    [AvaloniaFact]
    public async Task The_plans_of_all_frames_are_made_and_a_frame_without_a_picture_is_blank()
    {
        StudioViewModel studio = Studio();
        Load(studio, Solid(255, 0, 0));
        studio.AddFrame();
        studio.ClearImageCommand.Execute(null);
        studio.AddFrame();
        studio.SelectFrame(1);
        Assert.False(studio.HasImage);
        studio.SelectFrame(2);

        IReadOnlyList<PaintPlan> plans = await studio.PreparePlansAsync();

        Assert.Equal(3, plans.Count);
        Assert.True(plans[0].PaintedCells > 0);
        Assert.Equal(0, plans[1].PaintedCells);
        Assert.Equal(plans[0].Width, plans[1].Width);
    }

    [AvaloniaFact]
    public async Task Painting_asks_for_every_frame()
    {
        StudioViewModel studio = Studio();
        Load(studio, Solid(255, 0, 0));
        studio.AddFrame();
        Load(studio, Solid(0, 0, 255));

        PaintRequest? request = null;
        studio.PaintRequested += r => request = r;
        Assert.True(studio.CanPaint);
        studio.PaintCommand.Execute(null);

        for (int i = 0; i < 100 && request is null; i++)
            await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.NotNull(request);
        Assert.Equal(2, request.Plans.Count);
        Assert.Equal(request.Plans[0], request.Plan);
        Assert.NotEqual(request.Plans[0].Strokes.Passes[0].Color, request.Plans[1].Strokes.Passes[0].Color);
    }

    [AvaloniaFact]
    public void A_sign_that_does_not_animate_paints_the_open_picture_alone()
    {
        StudioViewModel studio = Studio(animated: false);
        Load(studio, Solid(255, 0, 0));

        PaintRequest? request = null;
        studio.PaintRequested += r => request = r;
        studio.PaintCommand.Execute(null);

        Assert.NotNull(request);
        Assert.Single(request.Plans);
    }

    [AvaloniaFact]
    public void Switching_to_a_sign_with_fewer_frames_keeps_only_the_frames_it_has()
    {
        StudioViewModel studio = Studio();
        Load(studio, Solid(255, 0, 0));
        studio.AddFrame();
        studio.AddFrame();

        studio.SelectedSurface = studio.AllSurfaces.Single(option => option.Surface.Id == "neon-large");

        // A plain sign hides the frames but keeps them, so going back to an animated sign finds them again.
        Assert.False(studio.IsAnimatedSurface);
        studio.SelectedSurface = studio.AllSurfaces.Single(option => option.Surface.Id == "animated-neon-small");
        Assert.Equal(3, studio.FrameCount);
    }

    [AvaloniaFact]
    public async Task Playing_shows_the_frames_one_after_the_other_and_stopping_goes_back_to_the_result()
    {
        StudioViewModel studio = Studio();
        Load(studio, Solid(255, 0, 0));
        studio.AddFrame();
        Load(studio, Solid(0, 0, 255));
        studio.AddFrame();
        Load(studio, Solid(0, 255, 0));

        await studio.TogglePlayAsync();

        Assert.True(studio.IsPlaying);
        Assert.Equal("Stop", studio.PlayText);
        Assert.False(studio.ShowSplit);
        Assert.True(studio.ShowResult);

        Assert.Equal(1, studio.PlayingFrame);
        var first = studio.ShownPreview;
        studio.AdvancePlayback();
        Assert.Equal(2, studio.PlayingFrame);
        Assert.NotSame(first, studio.ShownPreview);
        studio.AdvancePlayback();
        studio.AdvancePlayback();
        Assert.Equal(1, studio.PlayingFrame);

        await studio.TogglePlayAsync();
        Assert.False(studio.IsPlaying);
        Assert.Equal("Play", studio.PlayText);
        Assert.Same(studio.Preview, studio.ShownPreview);
    }

    [AvaloniaFact]
    public void The_speeds_are_a_frame_every_two_seconds_every_second_and_twice_a_second()
    {
        StudioViewModel studio = Studio();

        Assert.Equal([2.0, 1.0, 0.5], studio.SpeedChoices.Choices.Select(choice => (double)choice.Value));
        Assert.Equal(["Slow", "Medium", "Fast"], studio.SpeedChoices.Choices.Select(choice => choice.Label));
        Assert.Equal(1.0, studio.SpeedChoices.Selected);
    }

    [AvaloniaFact]
    public void Changing_frames_stops_the_playing()
    {
        StudioViewModel studio = Studio();
        Load(studio, Solid(255, 0, 0));
        studio.AddFrame();

        _ = studio.TogglePlayAsync();
        Task.Delay(300, TestContext.Current.CancellationToken).Wait(TestContext.Current.CancellationToken);

        studio.SelectFrame(0);
        Assert.False(studio.IsPlaying);
    }

    [AvaloniaFact]
    public void A_saved_animation_comes_back_with_all_its_frames()
    {
        StudioViewModel studio = Studio();
        Load(studio, Solid(255, 0, 0), "red.png");
        studio.AddFrame();
        Load(studio, Solid(0, 0, 255), "blue.png");
        studio.Zoom = 1.7;
        studio.AddFrame();
        Load(studio, Solid(0, 255, 0), "green.png");

        studio.SaveCommand.Execute(null);
        string id = studio.CurrentProjectId!;

        ProjectStore store = new(_root);
        StudioProject saved = store.Load(id)!.Value.Project;
        Assert.Equal(2, saved.MoreFrames.Count);
        Assert.Equal(2, store.LoadFramePictures(id).Count);
        Assert.All(store.LoadFramePictures(id), picture => Assert.NotNull(picture));

        StudioViewModel again = new(new SurfaceService(), projects: store) { ReprocessDelay = TimeSpan.Zero };
        Assert.True(again.OpenProject(id));
        again.ProcessNow();

        Assert.True(again.IsAnimatedSurface);
        Assert.Equal(3, again.FrameCount);
        Assert.Equal(0, again.FrameIndex);
        Assert.EndsWith("red.png", again.ImageName);

        again.SelectFrame(1);
        Assert.EndsWith("blue.png", again.ImageName);
        Assert.Equal(1.7, again.Zoom);

        again.SelectFrame(2);
        Assert.EndsWith("green.png", again.ImageName);
    }

    [AvaloniaFact]
    public void Saving_while_another_frame_is_open_still_saves_the_first_frame_first()
    {
        StudioViewModel studio = Studio();
        Load(studio, Solid(255, 0, 0), "first.png");
        studio.AddFrame();
        Load(studio, Solid(0, 0, 255), "second.png");

        studio.SaveCommand.Execute(null);

        StudioProject saved = new ProjectStore(_root).Load(studio.CurrentProjectId!)!.Value.Project;
        Assert.EndsWith("first.png", saved.SourceName);
        Assert.EndsWith("second.png", saved.MoreFrames.Single().SourceName);
    }

    [AvaloniaFact]
    public void A_plain_project_has_no_extra_frames_and_an_old_one_still_opens()
    {
        StudioViewModel studio = Studio(animated: false);
        Load(studio, Solid(255, 0, 0));
        studio.SaveCommand.Execute(null);

        ProjectStore store = new(_root);
        StudioProject saved = store.Load(studio.CurrentProjectId!)!.Value.Project;
        Assert.Empty(saved.MoreFrames);
        Assert.Empty(store.LoadFramePictures(studio.CurrentProjectId!));

        // A project file from before frames existed has no list of frames at all.
        string path = Path.Combine(_root, studio.CurrentProjectId!, "project.json");
        File.WriteAllText(path, System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(path), ",\\s*\"MoreFrames\":\\s*\\[\\s*\\]", ""));

        StudioViewModel again = new(new SurfaceService(), projects: store);
        Assert.True(again.OpenProject(studio.CurrentProjectId!));
        Assert.Equal(1, again.FrameCount);
    }


    [AvaloniaFact]
    public void Frames_of_typed_text_keep_their_own_text()
    {
        StudioViewModel studio = Studio();
        studio.SourceChoices.Select(SourceKind.Text);
        studio.Text = "A";
        studio.ProcessNow();
        studio.AddFrame();
        studio.Text = "B";
        studio.ProcessNow();
        studio.AddFrame();
        studio.Text = "C";
        studio.ProcessNow();

        studio.SelectFrame(0);
        Assert.Equal("A", studio.Text);
        Assert.Equal("Text", studio.ImageName);

        studio.SelectFrame(2);
        Assert.Equal("C", studio.Text);

        studio.SelectFrame(1);
        Assert.Equal("B", studio.Text);

        // Typing in a frame leaves the others as they were.
        studio.Text = "BB";
        studio.SelectFrame(0);
        Assert.Equal("A", studio.Text);
        studio.SelectFrame(1);
        Assert.Equal("BB", studio.Text);
    }

    [AvaloniaFact]
    public async Task A_picture_frame_and_a_text_frame_can_be_mixed()
    {
        StudioViewModel studio = Studio();
        Load(studio, Solid(255, 0, 0), "red.png");
        studio.AddFrame();
        studio.SourceChoices.Select(SourceKind.Text);
        studio.Text = "Hi";
        studio.ProcessNow();

        studio.SelectFrame(0);
        Assert.EndsWith("red.png", studio.ImageName);
        Assert.True(studio.IsPictureSource);

        studio.SelectFrame(1);
        Assert.Equal("Text", studio.ImageName);
        Assert.True(studio.IsTextSource);

        IReadOnlyList<PaintPlan> plans = await studio.PreparePlansAsync();
        Assert.All(plans, plan => Assert.True(plan.PaintedCells > 0));
    }

    [Fact]
    public void Copying_a_project_copies_its_frames()
    {
        ProjectStore store = new(_root);
        StudioProject project = new() { SurfaceId = "animated-neon-large", Name = "blink", MoreFrames = [new FrameSettings { SourceName = "two.png" }] };
        StudioProject saved = store.Save(project, [1, 2, 3], null, [[4, 5, 6]]);

        StudioProject copy = store.Duplicate(saved.Id, "blink copy")!;

        Assert.Equal([[(byte)4, (byte)5, (byte)6]], store.LoadFramePictures(copy.Id));
        Assert.Single(store.LoadFramePictures(saved.Id));
    }

    [Fact]
    public void Saving_with_fewer_frames_removes_the_pictures_that_are_no_longer_used()
    {
        ProjectStore store = new(_root);
        StudioProject project = new() { SurfaceId = "animated-neon-large", MoreFrames = [new FrameSettings(), new FrameSettings()] };
        StudioProject saved = store.Save(project, [1], null, [[2], [3]]);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(_root, saved.Id), "frame-*.png").Length);

        store.Save(saved with { MoreFrames = [new FrameSettings()] }, [1], null, [[2]]);

        Assert.Single(Directory.GetFiles(Path.Combine(_root, saved.Id), "frame-*.png"));
    }
}
