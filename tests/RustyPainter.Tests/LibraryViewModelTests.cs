using Avalonia.Headless.XUnit;
using RustyPainter.Controls;
using RustyPainter.Library;
using RustyPainter.Service;
using RustyPainter.ViewModels;

namespace RustyPainter.Tests;

public class LibraryViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"rustypainter-library-vm-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private ProjectStore Store() => new(_root);

    private static readonly byte[] Picture = [1, 2, 3];

    private static byte[] Png()
    {
        string path = Path.Combine(Path.GetTempPath(), $"rp-thumb-{Guid.NewGuid():N}.png");
        TestImages.SavePng(TestImages.RedAndGreen(4, 2), path);
        byte[] bytes = File.ReadAllBytes(path);
        File.Delete(path);
        return bytes;
    }

    private static StudioProject Project(string name, string surface = "sign-small") => new() { Name = name, SurfaceId = surface };

    private LibraryViewModel Library(ProjectStore store, Func<DateTimeOffset>? now = null) => new(store, new SurfaceService(), now);

    [AvaloniaFact]
    public void An_empty_library_says_so()
    {
        LibraryViewModel library = Library(Store());

        Assert.True(library.IsEmpty);
        Assert.False(library.HasProjects);
        Assert.Empty(library.Cards);
        Assert.Equal("", library.CountText);
        Assert.False(library.NothingFound);
    }

    [AvaloniaFact]
    public void Shows_a_card_for_each_project_with_its_surface_and_thumbnail()
    {
        ProjectStore store = Store();
        store.Save(Project("Sunset", "sign-large"), Picture, Png());
        store.Save(Project("No picture", "canvas-xl"), Picture, null);

        LibraryViewModel library = Library(store);

        Assert.Equal("2 projects", library.CountText);
        ProjectCard sunset = library.Cards.Single(card => card.Name == "Sunset");
        Assert.Equal("Large Wooden Sign", sunset.SurfaceName);
        Assert.True(sunset.HasThumbnail);
        Assert.False(library.Cards.Single(card => card.Name == "No picture").HasThumbnail);
        Assert.Equal("Artist Canvas XL", library.Cards.Single(card => card.Name == "No picture").SurfaceName);
    }

    [AvaloniaFact]
    public void A_surface_that_is_gone_is_shown_as_unknown()
    {
        ProjectStore store = Store();
        store.Save(Project("Old", "a-sign-that-was-removed"), Picture, null);

        Assert.Equal("An unknown surface", Library(store).Cards.Single().SurfaceName);
    }

    [AvaloniaFact]
    public void One_project_is_counted_in_the_singular()
    {
        ProjectStore store = Store();
        store.Save(Project("Only"), Picture, null);

        Assert.Equal("1 project", Library(store).CountText);
    }

    [AvaloniaFact]
    public void The_search_looks_at_names_and_surfaces_without_caring_about_case()
    {
        ProjectStore store = Store();
        store.Save(Project("Rusty logo", "sign-small"), Picture, null);
        store.Save(Project("Sunset", "canvas-xxl"), Picture, null);
        store.Save(Project("Portrait", "frame-portrait"), Picture, null);
        LibraryViewModel library = Library(store);

        library.Search = "RUSTY";
        Assert.Equal(["Rusty logo"], library.Cards.Select(card => card.Name));

        library.Search = "xxl";
        Assert.Equal(["Sunset"], library.Cards.Select(card => card.Name));

        library.Search = "  frame ";
        Assert.Equal(["Portrait"], library.Cards.Select(card => card.Name));

        library.Search = "nothing like this";
        Assert.True(library.NothingFound);
        Assert.False(library.IsEmpty);

        library.Search = "";
        Assert.Equal(3, library.Cards.Count);
    }

    [AvaloniaFact]
    public void Opening_a_card_asks_for_its_project()
    {
        ProjectStore store = Store();
        StudioProject saved = store.Save(Project("Open me"), Picture, null);
        LibraryViewModel library = Library(store);
        string? opened = null;
        library.OpenRequested += id => opened = id;
        library.Message = "An old message";

        library.Cards.Single().OpenCommand.Execute(null);

        Assert.Equal(saved.Id, opened);
        Assert.False(library.HasMessage);
    }

    [AvaloniaFact]
    public void Renaming_asks_for_the_new_name_and_keeps_the_old_when_it_is_empty_or_cancelled()
    {
        ProjectStore store = Store();
        store.Save(Project("Old name"), Picture, null);
        LibraryViewModel library = Library(store);
        ProjectCard card = library.Cards.Single();

        card.BeginRenameCommand.Execute(null);
        Assert.True(card.IsRenaming);
        Assert.Equal("Old name", card.EditName);

        card.EditName = "   ";
        card.CommitRenameCommand.Execute(null);
        Assert.False(card.IsRenaming);
        Assert.Equal("Old name", library.Cards.Single().Name);

        card.BeginRenameCommand.Execute(null);
        card.EditName = "Something else";
        card.CancelRenameCommand.Execute(null);
        Assert.False(card.IsRenaming);
        Assert.Equal("Old name", store.List().Single().Project.Name);

        card.BeginRenameCommand.Execute(null);
        card.EditName = "New name";
        card.CommitRenameCommand.Execute(null);
        Assert.Equal("New name", library.Cards.Single().Name);
        Assert.Equal("New name", store.List().Single().Project.Name);
    }

    [AvaloniaFact]
    public void A_copy_appears_next_to_the_original()
    {
        ProjectStore store = Store();
        store.Save(Project("Logo"), Picture, Png());
        LibraryViewModel library = Library(store);

        library.Cards.Single().DuplicateCommand.Execute(null);

        Assert.Equal(2, library.Cards.Count);
        Assert.Contains(library.Cards, card => card.Name == "Logo copy");
        Assert.Equal(2, library.Cards.Select(card => card.Id).Distinct().Count());
        Assert.All(library.Cards, card => Assert.True(card.HasThumbnail));
    }

    [AvaloniaFact]
    public void Deleting_asks_first_and_then_removes_everything()
    {
        ProjectStore store = Store();
        store.Save(Project("Keep"), Picture, null);
        StudioProject doomed = store.Save(Project("Doomed"), Picture, Png());
        LibraryViewModel library = Library(store);
        ProjectCard card = library.Cards.Single(c => c.Name == "Doomed");

        card.AskDeleteCommand.Execute(null);
        Assert.True(card.IsConfirmingDelete);
        Assert.Equal(2, store.List().Count);

        card.CancelDeleteCommand.Execute(null);
        Assert.False(card.IsConfirmingDelete);
        Assert.Equal(2, store.List().Count);

        card.AskDeleteCommand.Execute(null);
        card.ConfirmDeleteCommand.Execute(null);

        Assert.Equal(["Keep"], library.Cards.Select(c => c.Name));
        Assert.Null(store.Load(doomed.Id));
    }

    [AvaloniaFact]
    public void Deleting_the_last_project_brings_back_the_empty_message()
    {
        ProjectStore store = Store();
        store.Save(Project("Last"), Picture, null);
        LibraryViewModel library = Library(store);

        library.Cards.Single().ConfirmDeleteCommand.Execute(null);

        Assert.True(library.IsEmpty);
    }

    [AvaloniaFact]
    public void Refreshing_picks_up_projects_saved_in_the_meantime()
    {
        ProjectStore store = Store();
        LibraryViewModel library = Library(store);
        Assert.True(library.IsEmpty);

        store.Save(Project("Late"), Picture, null);
        library.Refresh();

        Assert.Equal(["Late"], library.Cards.Select(card => card.Name));
    }

    [AvaloniaFact]
    public void Times_are_told_as_today_yesterday_or_a_date()
    {
        ProjectStore store = Store();
        StudioProject saved = store.Save(Project("A"), Picture, null);
        DateTimeOffset changed = saved.Modified;

        string Text(DateTimeOffset now) => Library(store, () => now).Cards.Single().ModifiedText;

        Assert.StartsWith("Today", Text(changed));
        Assert.StartsWith("Yesterday", Text(changed.AddDays(1)));
        Assert.DoesNotContain("Today", Text(changed.AddDays(5)));
        Assert.DoesNotContain("Yesterday", Text(changed.AddDays(5)));
        Assert.Contains(changed.ToLocalTime().Year.ToString(), Text(changed.AddDays(5)));
    }
}

public class LibraryNavigationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"rustypainter-library-nav-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private MainWindowViewModel Shell(ProjectStore store)
        => new(new UpdateService(), new DialogService(), TestSettings.Create(), new SurfaceService(), new FakeGame(FakeGame.Rust()),
            new FakeHotkeys(), new FakeEngines(new FakeMachine()), new FakeHologram(), new FakeOverlay(), store);

    private static void LoadPicture(StudioViewModel studio)
    {
        string path = Path.Combine(Path.GetTempPath(), $"rp-lib-{Guid.NewGuid():N}.png");
        TestImages.SavePng(TestImages.Rainbow(), path);
        Assert.True(studio.TryLoad(path));
        File.Delete(path);
    }

    [AvaloniaFact]
    public void The_library_page_lists_what_studio_saved()
    {
        ProjectStore store = new(_root);
        MainWindowViewModel shell = Shell(store);

        shell.Sidebar.NavigateTo(SidebarPage.Studio);
        StudioViewModel studio = Assert.IsType<StudioViewModel>(shell.CurrentPage);
        LoadPicture(studio);
        studio.SaveCommand.Execute(null);

        shell.Sidebar.NavigateTo(SidebarPage.Library);

        LibraryViewModel library = Assert.IsType<LibraryViewModel>(shell.CurrentPage);
        Assert.Single(library.Cards);
    }

    [AvaloniaFact]
    public void Saving_while_the_library_is_open_somewhere_else_updates_it_when_it_is_shown_again()
    {
        ProjectStore store = new(_root);
        MainWindowViewModel shell = Shell(store);
        shell.Sidebar.NavigateTo(SidebarPage.Library);
        LibraryViewModel library = Assert.IsType<LibraryViewModel>(shell.CurrentPage);
        Assert.True(library.IsEmpty);

        shell.Sidebar.NavigateTo(SidebarPage.Studio);
        LoadPicture(Assert.IsType<StudioViewModel>(shell.CurrentPage));
        ((StudioViewModel)shell.CurrentPage!).SaveCommand.Execute(null);
        shell.Sidebar.NavigateTo(SidebarPage.Library);

        Assert.Same(library, shell.CurrentPage);
        Assert.Single(library.Cards);
    }

    [AvaloniaFact]
    public void Opening_a_project_shows_it_in_studio()
    {
        ProjectStore store = new(_root);
        MainWindowViewModel shell = Shell(store);
        shell.Sidebar.NavigateTo(SidebarPage.Studio);
        StudioViewModel studio = Assert.IsType<StudioViewModel>(shell.CurrentPage);
        LoadPicture(studio);
        studio.SelectedSurface = studio.AllSurfaces.Single(option => option.Surface.Id == "canvas-large");
        studio.Zoom = 1.75;
        studio.SaveCommand.Execute(null);
        string id = studio.CurrentProjectId!;

        // Studio is emptied, and the project is opened from the Library.
        studio.ClearImageCommand.Execute(null);
        shell.Sidebar.NavigateTo(SidebarPage.Library);
        LibraryViewModel library = Assert.IsType<LibraryViewModel>(shell.CurrentPage);
        library.Cards.Single().OpenCommand.Execute(null);

        Assert.Same(studio, shell.CurrentPage);
        Assert.Equal(SidebarPage.Studio, shell.Sidebar.ActivePage);
        Assert.True(studio.HasImage);
        Assert.Equal("canvas-large", studio.SelectedSurface.Surface.Id);
        Assert.Equal(1.75, studio.Zoom);
        Assert.Equal(id, studio.CurrentProjectId);
    }

    [AvaloniaFact]
    public void A_project_that_cannot_be_opened_is_explained_in_the_library()
    {
        ProjectStore store = new(_root);
        MainWindowViewModel shell = Shell(store);
        shell.Sidebar.NavigateTo(SidebarPage.Library);
        LibraryViewModel library = Assert.IsType<LibraryViewModel>(shell.CurrentPage);

        shell.OpenProject("not-there");

        // The user stays where they are and is told why nothing opened.
        Assert.Same(library, shell.CurrentPage);
        Assert.True(library.HasMessage);
        Assert.Contains("could not be opened", library.Message);
    }
}
