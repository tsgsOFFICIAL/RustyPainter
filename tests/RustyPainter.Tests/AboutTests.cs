using Avalonia.Headless.XUnit;
using RustyPainter.Interface;
using RustyPainter.Service;
using RustyPainter.ViewModels;

namespace RustyPainter.Tests;

public class AboutTests
{
    private sealed class RecordingDialogs : IDialogService
    {
        public List<string> Changelogs { get; } = [];

        public Task ShowUpdateAvailableAsync(IUpdateService updates, UpdateInfo update, string changelog) => Task.CompletedTask;

        public Task ShowChangelogAsync(string markdown)
        {
            Changelogs.Add(markdown);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void The_changelog_ships_inside_the_app_and_starts_with_the_newest_version()
    {
        string markdown = BundledChangelog.Load();

        Assert.False(string.IsNullOrWhiteSpace(markdown));
        Assert.Contains("# Changelog", markdown);
        Assert.Contains("## 0.1.1", markdown);
    }

    [Fact]
    public async Task Viewing_the_changelog_hands_the_bundled_text_to_the_dialog()
    {
        RecordingDialogs dialogs = new();
        AboutViewModel about = new(new UpdateService(), dialogs);

        about.ViewChangelogCommand.Execute(null);
        for (int i = 0; i < 100 && dialogs.Changelogs.Count == 0; i++)
            await Task.Delay(20, TestContext.Current.CancellationToken);

        Assert.Equal(BundledChangelog.Load(), Assert.Single(dialogs.Changelogs));
    }

    [Fact]
    public void The_page_tells_which_build_is_running()
    {
        AboutViewModel about = new(new UpdateService(), new RecordingDialogs());

        Assert.Equal(Utility.GetDisplayVersion(), about.VersionText);
        Assert.False(string.IsNullOrWhiteSpace(about.BuildText));
        Assert.Contains("UTC", about.BuildText);
        Assert.Contains(Environment.OSVersion.Version.ToString(), about.OsText);
        Assert.Equal("Free and open source", about.LicenseText);
    }

    [AvaloniaFact]
    public void About_is_reached_from_the_sidebar()
    {
        AppSettingsService settings = TestSettings.Create();
        MainWindowViewModel shell = new(new UpdateService(), new RecordingDialogs(), settings, new SurfaceService(), new FakeGame(null),
            new FakeHotkeys(), new FakeEngines(new FakeMachine()), new FakeHologram(), new FakeOverlay(), TestLibrary.Create());

        shell.Sidebar.NavigateTo(RustyPainter.Controls.SidebarPage.About);

        Assert.IsType<AboutViewModel>(shell.CurrentPage);
        Assert.Equal(RustyPainter.Controls.SidebarPage.About, shell.Sidebar.ActivePage);
    }

    [Fact]
    public void The_changelog_window_knows_when_there_is_nothing_to_show()
    {
        Assert.False(new ChangelogViewModel("  \n").HasMarkdown);
        Assert.True(new ChangelogViewModel("## 1.0.0").HasMarkdown);

        bool closed = false;
        ChangelogViewModel viewModel = new("x");
        viewModel.CloseRequested += () => closed = true;
        viewModel.CloseCommand.Execute(null);
        Assert.True(closed);
    }
}
