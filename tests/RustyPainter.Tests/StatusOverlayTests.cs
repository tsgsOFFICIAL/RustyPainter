using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using RustyPainter.Controls;
using RustyPainter.Imaging;
using RustyPainter.Interface;
using RustyPainter.Painting;
using RustyPainter.Service;
using RustyPainter.ViewModels;

namespace RustyPainter.Tests;

public class StatusOverlayTests
{
    private static MainWindowViewModel Shell(FakeGame game, FakeMachine machine, FakeOverlay overlay, FakeHotkeys? hotkeys = null)
        => new(new UpdateService(), new DialogService(), new AppSettingsService(), new SurfaceService(), game, hotkeys ?? new FakeHotkeys(),
            new FakeEngines(machine), new FakeHologram(), overlay) { OverlayLinger = TimeSpan.FromMilliseconds(60) };

    private static void Open(MainWindowViewModel shell)
    {
        shell.Sidebar.NavigateTo(SidebarPage.Studio);
        StudioViewModel studio = Assert.IsType<StudioViewModel>(shell.CurrentPage);
        string path = Path.Combine(Path.GetTempPath(), $"rp-ov-{Guid.NewGuid():N}.png");
        TestImages.SavePng(TestImages.Rainbow(), path);
        Assert.True(studio.TryLoad(path));
        studio.ProcessNow();
        File.Delete(path);
        studio.PaintCommand.Execute(null);
    }

    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 300 && !condition(); i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(15);
        }

        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void The_box_sits_at_the_top_middle_of_the_game_and_scales_with_it()
    {
        ScreenRect big = MainWindowViewModel.OverlayArea(FakeGame.Rust(2560, 1440));
        Assert.Equal(new ScreenRect(1050, 14, 1510, 102), big);

        ScreenRect small = MainWindowViewModel.OverlayArea(FakeGame.Rust(1920, 1080));
        Assert.Equal(460 * 0.75, small.Width, 6);
        Assert.Equal(88 * 0.75, small.Height, 6);
        Assert.Equal((1920 - 460 * 0.75) / 2, small.X1, 6);

        // A game that sits somewhere other than the corner of the desktop.
        GameWindow shifted = new(1, 1, new ScreenRect(-2560, 100, 0, 1540), false, true);
        Assert.Equal(-2560 + 1050, MainWindowViewModel.OverlayArea(shifted).X1, 6);
        Assert.Equal(114, MainWindowViewModel.OverlayArea(shifted).Y1, 6);
    }

    [AvaloniaFact]
    public async Task The_box_appears_when_painting_starts_and_goes_a_little_after_it_ends()
    {
        FakeOverlay overlay = new();
        FakeHotkeys hotkeys = new();
        MainWindowViewModel shell = Shell(new FakeGame(FakeGame.Rust()), new FakeMachine(), overlay, hotkeys);
        Open(shell);
        Assert.False(overlay.IsShown);

        hotkeys.Press(HotkeyAction.StartPause);
        await Until(() => shell.CurrentPage is PaintingViewModel);

        PaintingViewModel painting = Assert.IsType<PaintingViewModel>(shell.CurrentPage);
        Assert.True(overlay.IsShown);
        Assert.Same(painting, overlay.Content);
        Assert.Equal(MainWindowViewModel.OverlayArea(FakeGame.Rust()), overlay.Area);

        await Until(() => painting.IsOver);
        Assert.Equal(PaintState.Finished, painting.State);

        // It stays for a moment so the last state can be read, then goes.
        await Until(() => !overlay.IsShown);
        Assert.False(overlay.IsShown);
    }

    [AvaloniaFact]
    public async Task The_box_goes_at_once_when_the_painting_page_is_left()
    {
        FakeOverlay overlay = new();
        FakeHotkeys hotkeys = new();
        MainWindowViewModel shell = Shell(new FakeGame(FakeGame.Rust()), new FakeMachine(), overlay, hotkeys);
        shell.OverlayLinger = TimeSpan.FromSeconds(30);
        Open(shell);

        hotkeys.Press(HotkeyAction.StartPause);
        await Until(() => shell.CurrentPage is PaintingViewModel);
        PaintingViewModel painting = Assert.IsType<PaintingViewModel>(shell.CurrentPage);
        await Until(() => painting.IsOver);
        Assert.True(overlay.IsShown);

        painting.DoneCommand.Execute(null);

        Assert.False(overlay.IsShown);
    }

    [AvaloniaFact]
    public async Task A_new_painting_keeps_the_box_that_an_earlier_one_was_about_to_remove()
    {
        FakeOverlay overlay = new();
        FakeHotkeys hotkeys = new();
        FakeMachine machine = new();
        MainWindowViewModel shell = Shell(new FakeGame(FakeGame.Rust()), machine, overlay, hotkeys);
        shell.OverlayLinger = TimeSpan.FromMilliseconds(250);
        Open(shell);

        hotkeys.Press(HotkeyAction.StartPause);
        await Until(() => shell.CurrentPage is PaintingViewModel);
        PaintingViewModel first = Assert.IsType<PaintingViewModel>(shell.CurrentPage);
        await Until(() => first.IsOver);

        // Back to Studio and a second painting before the box of the first was due to go.
        // This one waits for Rust, so it is still going.
        machine.GameInFront = false;
        first.DoneCommand.Execute(null);
        StudioViewModel studio = Assert.IsType<StudioViewModel>(shell.CurrentPage);
        studio.PaintCommand.Execute(null);
        hotkeys.Press(HotkeyAction.StartPause);
        await Until(() => shell.CurrentPage is PaintingViewModel);
        PaintingViewModel second = Assert.IsType<PaintingViewModel>(shell.CurrentPage);
        Assert.NotSame(first, second);
        Assert.Same(second, overlay.Content);

        // The timer of the first would have fired by now, but the box belongs to the second.
        await Task.Delay(400);
        Dispatcher.UIThread.RunJobs();

        Assert.False(second.IsOver);
        Assert.True(overlay.IsShown);
        Assert.Same(second, overlay.Content);

        second.AbortCommand.Execute(null);
    }

    private static PaintingViewModel Page(FakeMachine machine)
    {
        SurfaceService surfaces = new();
        Surface surface = surfaces.Catalog.Surfaces.Single(s => s.Id == "sign-small");
        IReadOnlyList<Rgb> swatches = surfaces.Catalog.Layouts[surface.Layout].Swatches.ColorGrid!.Colors.Select(Rgb.Parse).ToList();
        int[] cells = new int[8 * 4];
        for (int i = 0; i < cells.Length; i++)
            cells[i] = i % 8 < 4 ? 1 : 2;

        PaintRequest request = new(new PaintPlan(8, 4, swatches, cells), surface, ColorMode.Swatches, 8, "cat.png");
        PaintSession session = new(request, new PaintOptions(ColorMode.Swatches, 8, false), PaintTiming.Default);
        return new PaintingViewModel(session, new FakeGame(FakeGame.Rust()), surfaces, new FakeEngines(machine), "Space", "Esc");
    }

    [AvaloniaFact]
    public async Task The_words_in_the_box_follow_the_state()
    {
        FakeMachine machine = new() { GameInFront = false };
        PaintingViewModel page = Page(machine);

        Assert.Equal("Starting", page.OverlayTitle);

        Task run = page.StartAsync();
        await Until(() => page.IsPaused);

        Assert.StartsWith("Paused", page.OverlayTitle);
        Assert.Contains("Rust is not in front.", page.OverlayLine);
        Assert.Contains("Space carries on, Esc stops.", page.OverlayLine);

        machine.GameInFront = true;
        page.OnHotkey(HotkeyAction.StartPause);
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        await Until(() => page.IsOver);

        Assert.Equal("Finished", page.OverlayTitle);
        Assert.Equal("Save the picture in the game.", page.OverlayLine);
    }

    [AvaloniaFact]
    public async Task The_box_says_what_was_painted_when_it_was_stopped()
    {
        FakeMachine machine = new();
        PaintingViewModel page = Page(machine);
        machine.Before = what =>
        {
            if (what == "down")
            {
                machine.Before = null;
                page.OnHotkey(HotkeyAction.Abort);
            }
        };

        await page.StartAsync();
        await Until(() => page.IsOver);

        Assert.Equal("Stopped", page.OverlayTitle);
        Assert.Contains("still on the canvas", page.OverlayLine);
    }

    [AvaloniaFact]
    public async Task The_box_changes_with_the_progress()
    {
        FakeMachine machine = new();
        PaintingViewModel page = Page(machine);
        List<string> changed = [];
        page.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(PaintingViewModel.OverlayTitle) or nameof(PaintingViewModel.OverlayLine))
                changed.Add(e.PropertyName!);
        };

        await page.StartAsync();
        await Until(() => page.IsOver);

        Assert.Contains(nameof(PaintingViewModel.OverlayTitle), changed);
        Assert.Contains(nameof(PaintingViewModel.OverlayLine), changed);
    }

    [AvaloniaFact]
    public void The_window_draws_the_page_it_is_given()
    {
        PaintingViewModel page = Page(new FakeMachine());
        RustyPainter.Windows.StatusOverlayWindow window = new() { DataContext = page };
        window.MoveTo(new Avalonia.PixelRect(1050, 14, 460, 88));
        window.Show();

        Assert.Equal(new Avalonia.PixelPoint(1050, 14), window.Position);
        Assert.Equal(460 / window.RenderScaling, window.Width, 3);
        Assert.True(window.Topmost);
        Assert.False(window.ShowInTaskbar);

        window.Close();
    }

    [AvaloniaFact]
    public void The_footer_tells_whether_rust_is_running()
    {
        FakeGame game = new(null);
        MainWindowViewModel shell = Shell(game, new FakeMachine(), new FakeOverlay());

        shell.RefreshRustStatus();
        Assert.False(shell.IsRustRunning);
        Assert.Equal("Rust is not running", shell.RustStatusText);

        game.Window = FakeGame.Rust(1920, 1080);
        shell.RefreshRustStatus();
        Assert.True(shell.IsRustRunning);
        Assert.Equal("Rust is running at 1920 x 1080", shell.RustStatusText);

        game.Window = null;
        shell.RefreshRustStatus();
        Assert.False(shell.IsRustRunning);
    }
}
