using RustyPainter.Library;

namespace RustyPainter.Tests;

public class ProjectStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"rustypainter-library-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private ProjectStore Store() => new(_root);

    private static readonly byte[] Picture = [1, 2, 3, 4, 5];
    private static readonly byte[] Thumbnail = [9, 8, 7];

    private static StudioProject Project(string name = "Sunset", string surface = "sign-small") => new() { Name = name, SurfaceId = surface };

    [Fact]
    public void An_empty_library_lists_nothing()
    {
        Assert.Empty(Store().List());
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public void A_saved_project_gets_an_id_and_comes_back_whole()
    {
        ProjectStore store = Store();
        StudioProject project = Project() with
        {
            Zoom = 1.5, OffsetX = 12.5, OffsetY = -3, QuarterTurns = 3, FlipX = true, ColorMode = "Free", MaxColors = 200,
            Dithering = "Ordered", Filter = "Sepia", Brightness = 10, Contrast = -5, Saturation = 20, FillBackground = true,
            BackgroundColor = "#112233", Quality = 8, Placement = "Fit", SourceName = "cat.png"
        };

        StudioProject saved = store.Save(project, Picture, Thumbnail);

        Assert.NotEqual("", saved.Id);
        (StudioProject Project, byte[] Picture)? back = store.Load(saved.Id);
        Assert.NotNull(back);
        Assert.Equal(saved, back!.Value.Project);
        Assert.Equal(Picture, back.Value.Picture);
    }

    [Fact]
    public void Text_projects_keep_their_text_and_look()
    {
        ProjectStore store = Store();
        StudioProject project = Project("Sign") with
        {
            Source = ProjectSource.Text, Text = "RUST\nPAINTER", TextColor = "#FF8800", TextBold = false,
            TextSolidBackground = true, TextBackgroundColor = "#101010"
        };

        StudioProject saved = store.Save(project, Picture, null);
        StudioProject back = store.Load(saved.Id)!.Value.Project;

        Assert.Equal(ProjectSource.Text, back.Source);
        Assert.Equal("RUST\nPAINTER", back.Text);
        Assert.Equal("#FF8800", back.TextColor);
        Assert.False(back.TextBold);
        Assert.True(back.TextSolidBackground);
    }

    [Fact]
    public void Saving_again_with_the_same_id_replaces_the_project()
    {
        ProjectStore store = Store();
        StudioProject first = store.Save(Project("One"), Picture, Thumbnail);

        StudioProject second = store.Save(first with { Name = "Two", Zoom = 2 }, [7, 7], null);

        Assert.Equal(first.Id, second.Id);
        ProjectEntry only = Assert.Single(store.List());
        Assert.Equal("Two", only.Project.Name);
        Assert.Equal(2, only.Project.Zoom);
        Assert.Equal<byte>([7, 7], store.Load(first.Id)!.Value.Picture);

        // No thumbnail was given this time, so the old one is gone.
        Assert.Null(only.ThumbnailPath);
    }

    [Fact]
    public void The_most_recently_changed_project_comes_first()
    {
        ProjectStore store = Store();
        StudioProject a = store.Save(Project("A"), Picture, null);
        Thread.Sleep(20);
        StudioProject b = store.Save(Project("B"), Picture, null);
        Thread.Sleep(20);
        store.Save(a with { Zoom = 3 }, Picture, null);

        Assert.Equal(["A", "B"], store.List().Select(entry => entry.Project.Name));
        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public void Listing_gives_the_thumbnail_when_there_is_one()
    {
        ProjectStore store = Store();
        store.Save(Project("With"), Picture, Thumbnail);
        store.Save(Project("Without"), Picture, null);

        Dictionary<string, ProjectEntry> byName = store.List().ToDictionary(entry => entry.Project.Name);

        Assert.NotNull(byName["With"].ThumbnailPath);
        Assert.Equal(Thumbnail, File.ReadAllBytes(byName["With"].ThumbnailPath!));
        Assert.Null(byName["Without"].ThumbnailPath);
    }

    [Fact]
    public void Renaming_changes_only_the_name_and_keeps_the_order()
    {
        ProjectStore store = Store();
        StudioProject a = store.Save(Project("A"), Picture, null);
        Thread.Sleep(20);
        store.Save(Project("B"), Picture, null);

        store.Rename(a.Id, "  New   name ");

        List<ProjectEntry> list = [.. store.List()];
        Assert.Equal(["B", "New name"], list.Select(entry => entry.Project.Name));
        Assert.Equal(a.Modified, list[1].Project.Modified);
    }

    [Fact]
    public void A_name_is_tidied_and_never_empty()
    {
        ProjectStore store = Store();

        StudioProject empty = store.Save(Project("   "), Picture, null);
        StudioProject longName = store.Save(Project(new string('x', 200)), Picture, null);

        Assert.Equal("Untitled", empty.Name);
        Assert.Equal(80, longName.Name.Length);
    }

    [Fact]
    public void A_copy_is_a_separate_project_with_the_same_content()
    {
        ProjectStore store = Store();
        StudioProject original = store.Save(Project("Original") with { Zoom = 2.5 }, Picture, Thumbnail);

        StudioProject copy = store.Duplicate(original.Id, "Original copy")!;

        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equal("Original copy", copy.Name);
        Assert.Equal(2.5, copy.Zoom);
        Assert.Equal(Picture, store.Load(copy.Id)!.Value.Picture);
        Assert.Equal(2, store.List().Count);

        // Changing the copy leaves the original alone.
        store.Save(copy with { Zoom = 1 }, [0], null);
        Assert.Equal(2.5, store.Load(original.Id)!.Value.Project.Zoom);
        Assert.Equal(Picture, store.Load(original.Id)!.Value.Picture);
    }

    [Fact]
    public void Copying_a_project_that_is_not_there_gives_nothing()
    {
        Assert.Null(Store().Duplicate("nothing", "Copy"));
    }

    [Fact]
    public void Deleting_removes_everything_of_the_project()
    {
        ProjectStore store = Store();
        StudioProject kept = store.Save(Project("Keep"), Picture, null);
        StudioProject gone = store.Save(Project("Go"), Picture, Thumbnail);

        store.Delete(gone.Id);

        Assert.Equal(["Keep"], store.List().Select(entry => entry.Project.Name));
        Assert.False(Directory.Exists(Path.Combine(_root, gone.Id)));
        Assert.Null(store.Load(gone.Id));
        Assert.NotNull(store.Load(kept.Id));

        // Deleting something that is already gone is harmless.
        store.Delete(gone.Id);
    }

    [Fact]
    public void A_damaged_project_is_left_out_and_does_not_hide_the_others()
    {
        ProjectStore store = Store();
        store.Save(Project("Good"), Picture, null);

        Directory.CreateDirectory(Path.Combine(_root, "broken"));
        File.WriteAllText(Path.Combine(_root, "broken", "project.json"), "{ this is not json");
        File.WriteAllBytes(Path.Combine(_root, "broken", "picture.png"), Picture);

        Directory.CreateDirectory(Path.Combine(_root, "nopicture"));
        File.WriteAllText(Path.Combine(_root, "nopicture", "project.json"), "{ \"SurfaceId\": \"sign-small\", \"Name\": \"x\" }");

        Directory.CreateDirectory(Path.Combine(_root, "empty"));

        Assert.Equal(["Good"], store.List().Select(entry => entry.Project.Name));
        Assert.Null(store.Load("broken"));
        Assert.Null(store.Load("nopicture"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("../outside")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("C:")]
    public void An_id_that_could_point_elsewhere_is_never_used_as_a_folder(string id)
    {
        ProjectStore store = Store();
        string outside = Path.Combine(Path.GetDirectoryName(_root)!, "outside");

        Assert.Null(store.Load(id));
        store.Delete(id);
        store.Rename(id, "x");
        Assert.Null(store.Duplicate(id, "x"));

        // A project that arrives with such an id is given a fresh one.
        StudioProject saved = store.Save(Project() with { Id = id }, Picture, null);
        Assert.NotEqual(id, saved.Id);
        Assert.True(Directory.Exists(Path.Combine(_root, saved.Id)));
        Assert.False(Directory.Exists(outside));
    }

    [Fact]
    public void A_folder_that_was_copied_by_hand_still_works_under_its_own_name()
    {
        ProjectStore store = Store();
        StudioProject saved = store.Save(Project("Moved"), Picture, null);

        Directory.Move(Path.Combine(_root, saved.Id), Path.Combine(_root, "renamed-folder"));

        ProjectEntry entry = Assert.Single(store.List());
        Assert.Equal("renamed-folder", entry.Project.Id);
        Assert.NotNull(store.Load("renamed-folder"));
    }

    [Fact]
    public void A_project_without_a_surface_is_not_usable()
    {
        ProjectStore store = Store();
        Directory.CreateDirectory(Path.Combine(_root, "nosurface"));
        File.WriteAllText(Path.Combine(_root, "nosurface", "project.json"), "{ \"Name\": \"x\" }");
        File.WriteAllBytes(Path.Combine(_root, "nosurface", "picture.png"), Picture);

        Assert.Empty(store.List());
    }

    [Fact]
    public void Nothing_is_left_half_written()
    {
        ProjectStore store = Store();
        StudioProject saved = store.Save(Project(), Picture, Thumbnail);

        Assert.Empty(Directory.GetFiles(Path.Combine(_root, saved.Id), "*.tmp"));
    }
}
