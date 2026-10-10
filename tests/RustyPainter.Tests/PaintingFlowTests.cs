using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using RustyPainter.Controls;
using RustyPainter.Imaging;
using RustyPainter.Interface;
using RustyPainter.Painting;
using RustyPainter.Service;
using RustyPainter.ViewModels;

namespace RustyPainter.Tests;

public class HotkeyTests
{
    [Theory]
    [InlineData("F8", 0u, 0x77u)]
    [InlineData("f9", 0u, 0x78u)]
    [InlineData("Ctrl+F8", Hotkey.Ctrl, 0x77u)]
    [InlineData("Shift + Alt + P", Hotkey.Shift | Hotkey.Alt, (uint)'P')]
    [InlineData("control+5", Hotkey.Ctrl, (uint)'5')]
    [InlineData("F24", 0u, 0x87u)]
    public void Reads_hotkey_text(string text, uint modifiers, uint key)
    {
        Assert.True(Hotkey.TryParse(text, out Hotkey hotkey));
        Assert.Equal(new Hotkey(modifiers, key), hotkey);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+")]
    [InlineData("F25")]
    [InlineData("F0")]
    [InlineData("A+B")]
    [InlineData("Win+F8")]
    public void Rejects_text_that_is_not_a_hotkey(string? text) => Assert.False(Hotkey.TryParse(text, out _));

    [Theory]
    [InlineData("F8")]
    [InlineData("Ctrl+F9")]
    [InlineData("Ctrl+Alt+Shift+Q")]
    public void Shows_a_hotkey_the_way_it_was_written(string text)
    {
        Hotkey.TryParse(text, out Hotkey hotkey);
        Assert.Equal(text, hotkey.Display);
    }

    [Fact]
    public void The_defaults_are_space_and_escape()
    {
        IReadOnlyDictionary<HotkeyAction, Hotkey> keys = HotkeyDefaults.FromSettings(null, null);

        Assert.Equal("Space", keys[HotkeyAction.StartPause].Display);
        Assert.Equal("Esc", keys[HotkeyAction.Abort].Display);
        Assert.Equal(new Hotkey(0, 0x20), keys[HotkeyAction.StartPause]);
        Assert.Equal(new Hotkey(0, 0x1B), keys[HotkeyAction.Abort]);
    }

    [Fact]
    public void The_first_versions_defaults_of_F8_and_F9_move_to_the_new_ones()
    {
        IReadOnlyDictionary<HotkeyAction, Hotkey> keys = HotkeyDefaults.FromSettings("F8", "F9");

        Assert.Equal("Space", keys[HotkeyAction.StartPause].Display);
        Assert.Equal("Esc", keys[HotkeyAction.Abort].Display);

        // One of them changed on purpose is kept.
        Assert.Equal("F8", HotkeyDefaults.FromSettings("F8", "Esc")[HotkeyAction.StartPause].Display);
    }

    [Theory]
    [InlineData("Space", 0x20u)]
    [InlineData("space", 0x20u)]
    [InlineData("Esc", 0x1Bu)]
    [InlineData("Escape", 0x1Bu)]
    [InlineData("PageUp", 0x21u)]
    [InlineData("Ctrl+Space", 0x20u)]
    public void Reads_the_names_of_the_common_keys(string text, uint key)
    {
        Assert.True(Hotkey.TryParse(text, out Hotkey hotkey));
        Assert.Equal(key, hotkey.VirtualKey);
    }

    [Fact]
    public void Only_sensible_keys_can_be_used()
    {
        Assert.True(Hotkey.IsUsableKey(0x20));
        Assert.True(Hotkey.IsUsableKey(0x1B));
        Assert.True(Hotkey.IsUsableKey('A'));
        Assert.True(Hotkey.IsUsableKey('7'));
        Assert.True(Hotkey.IsUsableKey(0x77));
        Assert.False(Hotkey.IsUsableKey(0x5B));   // the Windows key
        Assert.False(Hotkey.IsUsableKey(0x10));   // Shift on its own
        Assert.False(Hotkey.IsUsableKey(0));
    }

    [Fact]
    public void Unreadable_settings_fall_back_to_the_defaults()
    {
        IReadOnlyDictionary<HotkeyAction, Hotkey> keys = HotkeyDefaults.FromSettings("nonsense", "Ctrl+F5");

        Assert.Equal("Space", keys[HotkeyAction.StartPause].Display);
        Assert.Equal("Ctrl+F5", keys[HotkeyAction.Abort].Display);
    }

    [Fact]
    public void The_same_key_for_both_actions_falls_back_to_the_defaults()
    {
        IReadOnlyDictionary<HotkeyAction, Hotkey> keys = HotkeyDefaults.FromSettings("F5", "F5");

        Assert.Equal("Space", keys[HotkeyAction.StartPause].Display);
        Assert.Equal("Esc", keys[HotkeyAction.Abort].Display);
    }

    private static readonly Dictionary<HotkeyAction, Hotkey> Bindings = new()
    {
        [HotkeyAction.StartPause] = HotkeyDefaults.StartPause,
        [HotkeyAction.Abort] = HotkeyDefaults.Abort
    };

    [Fact]
    public void A_key_is_only_taken_while_the_game_is_in_front()
    {
        HotkeyMatcher matcher = new(Bindings);

        Assert.Equal(HotkeyAction.StartPause, matcher.Match(0x20, 0, gameInFront: true));
        Assert.Equal(HotkeyAction.Abort, matcher.Match(0x1B, 0, gameInFront: true));

        Assert.Null(matcher.Match(0x20, 0, gameInFront: false));
        Assert.Null(matcher.Match(0x1B, 0, gameInFront: false));
    }

    [Fact]
    public void Other_keys_and_other_modifiers_are_left_alone()
    {
        HotkeyMatcher matcher = new(Bindings);

        Assert.Null(matcher.Match('A', 0, gameInFront: true));
        Assert.Null(matcher.Match(0x20, Hotkey.Ctrl, gameInFront: true));
        Assert.Null(matcher.Match(0x1B, Hotkey.Shift, gameInFront: true));
    }

    [Fact]
    public void A_key_with_modifiers_needs_exactly_those_modifiers()
    {
        HotkeyMatcher matcher = new(new Dictionary<HotkeyAction, Hotkey> { [HotkeyAction.Abort] = new Hotkey(Hotkey.Ctrl | Hotkey.Shift, 'Q') });

        Assert.Equal(HotkeyAction.Abort, matcher.Match('Q', Hotkey.Ctrl | Hotkey.Shift, gameInFront: true));
        Assert.Null(matcher.Match('Q', Hotkey.Ctrl, gameInFront: true));
        Assert.Null(matcher.Match('Q', 0, gameInFront: true));
    }

    /// <summary>
    /// Installs the real keyboard hook and takes it away again. The game is never in front as far as the hook is told,
    /// so no key is taken and no key press is simulated.
    /// </summary>
    [Fact]
    public void The_real_service_installs_the_hook_and_removes_it_again()
    {
        using GlobalHotkeyService service = new(() => false);
        bool pressed = false;
        service.Pressed += _ => pressed = true;

        Assert.Empty(service.Start(Bindings));
        Assert.True(service.IsActive);

        // It can be started again with other keys, and stopped.
        Assert.Empty(service.Start(Bindings));
        Assert.True(service.IsActive);

        service.Stop();
        Assert.False(service.IsActive);
        Assert.False(pressed);

        // Stopping twice is harmless.
        service.Stop();
    }
}

public class PaintTimeEstimatorTests
{
    private static PaintPlan Plan(int width, int height, Func<int, int, int> cell)
    {
        Rgb[] palette = Enumerable.Range(0, 64).Select(i => new Rgb((byte)i, (byte)i, (byte)i)).ToArray();
        int[] cells = new int[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                cells[y * width + x] = cell(x, y);

        return new PaintPlan(width, height, palette, cells);
    }

    [Fact]
    public void Matches_what_was_measured_in_the_game()
    {
        // The first live run: a 47 x 25 border with diagonal dots took 11.6 seconds at the default pace.
        PaintPlan plan = Plan(47, 25, (x, y) => x == 0 || y == 0 || x == 46 || y == 24 ? 20 : (x + y) % 8 == 0 ? 40 : Quantizer.Unpainted);

        TimeSpan estimate = PaintTimeEstimator.Estimate(plan.Strokes, PaintTiming.Default, ColorMode.Swatches, clearFirst: false);

        Assert.InRange(estimate.TotalSeconds, 8.5, 15);
    }

    [Fact]
    public void More_strokes_take_longer()
    {
        PaintPlan few = Plan(20, 20, (_, _) => 1);
        PaintPlan many = Plan(20, 20, (x, y) => (x + y) % 2);

        Assert.True(PaintTimeEstimator.Estimate(many.Strokes, PaintTiming.Default, ColorMode.Swatches)
            > PaintTimeEstimator.Estimate(few.Strokes, PaintTiming.Default, ColorMode.Swatches));
    }

    [Fact]
    public void A_faster_pace_is_quicker()
    {
        PaintPlan plan = Plan(40, 40, (x, y) => (x / 3 + y / 3) % 5);

        TimeSpan slow = PaintTimeEstimator.Estimate(plan.Strokes, PaintTiming.FromSpeed(10), ColorMode.Swatches);
        TimeSpan fast = PaintTimeEstimator.Estimate(plan.Strokes, PaintTiming.FromSpeed(100), ColorMode.Swatches);

        Assert.True(fast < slow, $"{fast} vs {slow}");
    }

    [Fact]
    public void Typed_colors_cost_more_than_clicked_swatches()
    {
        PaintPlan plan = Plan(30, 30, (x, y) => (x + y) % 12);

        Assert.True(PaintTimeEstimator.Estimate(plan.Strokes, PaintTiming.Default, ColorMode.Free)
            > PaintTimeEstimator.Estimate(plan.Strokes, PaintTiming.Default, ColorMode.Swatches));
    }

    [Fact]
    public void Scrolling_and_clearing_add_time()
    {
        PaintPlan plan = Plan(10, 10, (_, _) => 3);
        TimeSpan basic = PaintTimeEstimator.Estimate(plan.Strokes, PaintTiming.Default, ColorMode.Swatches, 0, clearFirst: false);

        Assert.True(PaintTimeEstimator.Estimate(plan.Strokes, PaintTiming.Default, ColorMode.Swatches, 11, clearFirst: false) > basic);
        Assert.True(PaintTimeEstimator.Estimate(plan.Strokes, PaintTiming.Default, ColorMode.Swatches, 0, clearFirst: true) > basic);
    }

    [Theory]
    [InlineData(4, "A few seconds")]
    [InlineData(30, "About 30 seconds")]
    [InlineData(60, "About 1 minute")]
    [InlineData(80, "About 1 minute")]
    [InlineData(240, "About 4 minutes")]
    [InlineData(5340, "About 89 minutes")]
    public void Describes_a_time_in_words(int seconds, string text)
        => Assert.Equal(text, PaintTimeEstimator.Describe(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Long_times_are_given_in_hours()
        => Assert.Equal($"About {2.0:0.0} hours", PaintTimeEstimator.Describe(TimeSpan.FromHours(2)));
}

public class GetReadyTests
{
    private static PaintRequest Request(string surfaceId = "sign-small", ColorMode mode = ColorMode.Swatches)
    {
        SurfaceService surfaces = new();
        Surface surface = surfaces.Catalog.Surfaces.Single(s => s.Id == surfaceId);
        IReadOnlyList<Rgb> swatches = surfaces.Catalog.Layouts[surface.Layout].Swatches.ColorGrid!.Colors.Select(Rgb.Parse).ToList();
        int[] cells = new int[8 * 4];
        for (int i = 0; i < cells.Length; i++)
            cells[i] = i % 8 < 4 ? 1 : 2;

        return new PaintRequest(new PaintPlan(8, 4, swatches, cells), surface, mode, 8, "cat.png");
    }

    private static GetReadyViewModel Page(FakeGame game, AppSettingsService? settings = null, PaintRequest? request = null, FakeHologram? hologram = null, FakeOverlay? overlay = null)
    {
        settings ??= TestSettings.Create();
        return new GetReadyViewModel(request ?? Request(), game, new SurfaceService(), settings, hologram ?? new FakeHologram(), new Hotkey(0, 0x77), new Hotkey(0, 0x78));
    }

    [AvaloniaFact]
    public void Says_what_is_about_to_be_painted()
    {
        GetReadyViewModel page = Page(new FakeGame(FakeGame.Rust()));

        Assert.Equal("cat.png", page.PictureName);
        Assert.Equal("Small Wooden Sign", page.SurfaceName);
        Assert.Equal("F8", page.StartKeyText);
        Assert.Equal("F9", page.AbortKeyText);
        Assert.Contains("2 swatches", page.ColorsText);
        Assert.Contains("strokes", page.StrokesText);
        Assert.NotEqual("", page.TimeText);
    }

    [AvaloniaFact]
    public void Starting_needs_rust_to_be_running()
    {
        FakeGame game = new(null);
        GetReadyViewModel page = Page(game);

        Assert.False(page.IsRustRunning);
        Assert.False(page.StartCommand.CanExecute(null));
        Assert.Contains("not running", page.RustText);

        game.Window = FakeGame.Rust(1920, 1080);
        page.Refresh();

        Assert.True(page.IsRustRunning);
        Assert.True(page.StartCommand.CanExecute(null));
        Assert.Equal("Rust is running at 1920 x 1080.", page.RustText);
    }

    [AvaloniaFact]
    public void Starting_hands_over_the_options()
    {
        GetReadyViewModel page = Page(new FakeGame(FakeGame.Rust()));
        page.ClearFirst = false;
        page.Speed = 80;
        page.NudgeX = 3;
        page.NudgeY = -2;
        PaintSession? session = null;
        page.StartRequested += s => session = s;

        page.StartCommand.Execute(null);

        Assert.NotNull(session);
        Assert.False(session!.Options.ClearFirst);
        Assert.Equal((3d, -2d), (session.Options.NudgeX, session.Options.NudgeY));
        Assert.Equal(8, session.Options.BlockSize);
        Assert.Equal(PaintTiming.FromSpeed(80), session.Timing);
        Assert.Equal("cat.png", session.Request.PictureName);
    }

    [AvaloniaFact]
    public void The_start_key_starts_only_when_rust_is_running()
    {
        FakeGame game = new(null);
        GetReadyViewModel page = Page(game);
        int starts = 0;
        page.StartRequested += _ => starts++;

        page.OnHotkey(HotkeyAction.StartPause);
        Assert.Equal(0, starts);

        game.Window = FakeGame.Rust();
        page.Refresh();
        page.OnHotkey(HotkeyAction.StartPause);
        page.OnHotkey(HotkeyAction.Abort);

        Assert.Equal(1, starts);
    }

    [AvaloniaFact]
    public void The_speed_is_remembered_but_clearing_is_on_every_time()
    {
        AppSettingsService settings = TestSettings.Create();
        GetReadyViewModel page = Page(new FakeGame(FakeGame.Rust()), settings);

        Assert.True(page.ClearFirst);
        page.ClearFirst = false;
        page.Speed = 35;

        Assert.Equal(35, settings.Current.PaintSpeed);

        GetReadyViewModel next = Page(new FakeGame(FakeGame.Rust()), settings);
        Assert.Equal(35, next.Speed);

        // The canvas is cleared again unless that is turned off for this painting.
        Assert.True(next.ClearFirst);
    }

    [AvaloniaFact]
    public void The_fine_tuning_and_speed_stay_within_their_limits()
    {
        GetReadyViewModel page = Page(new FakeGame(FakeGame.Rust()));

        page.NudgeX = 500;
        page.NudgeY = -500;
        page.Speed = 0;

        Assert.Equal((30d, -30d, 1d), (page.NudgeX, page.NudgeY, page.Speed));
        Assert.Equal("+30 px", page.NudgeXText);
        Assert.Equal("-30 px", page.NudgeYText);
    }

    [AvaloniaFact]
    public void A_faster_speed_gives_a_shorter_time()
    {
        GetReadyViewModel page = Page(new FakeGame(FakeGame.Rust()), request: Request("sign-small"));
        page.Speed = 1;
        TimeSpan slow = PaintTimeEstimator.Estimate(page.Request.Plan.Strokes, PaintTiming.FromSpeed(1), page.Request.Mode);
        page.Speed = 100;
        TimeSpan fast = PaintTimeEstimator.Estimate(page.Request.Plan.Strokes, PaintTiming.FromSpeed(100), page.Request.Mode);

        Assert.True(fast < slow);
    }

    [AvaloniaFact]
    public void Tells_about_scrolling_for_cropped_canvases()
    {
        GetReadyViewModel page = Page(new FakeGame(FakeGame.Rust()), request: Request("canvas-xxl"));

        Assert.Contains("11 times", page.SetupText);
        Assert.DoesNotContain("times", Page(new FakeGame(FakeGame.Rust())).SetupText);
    }

    [AvaloniaFact]
    public void Back_asks_to_return()
    {
        GetReadyViewModel page = Page(new FakeGame(FakeGame.Rust()));
        bool back = false;
        page.BackRequested += () => back = true;

        page.BackCommand.Execute(null);

        Assert.True(back);
    }
}

public class PaintingPageTests
{
    private static PaintSession Session(bool clear = false)
    {
        SurfaceService surfaces = new();
        Surface surface = surfaces.Catalog.Surfaces.Single(s => s.Id == "sign-small");
        IReadOnlyList<Rgb> swatches = surfaces.Catalog.Layouts[surface.Layout].Swatches.ColorGrid!.Colors.Select(Rgb.Parse).ToList();
        int[] cells = new int[8 * 4];
        for (int i = 0; i < cells.Length; i++)
            cells[i] = i % 8 < 4 ? 1 : 2;

        PaintRequest request = new(new PaintPlan(8, 4, swatches, cells), surface, ColorMode.Swatches, 8, "cat.png");
        return new PaintSession(request, new PaintOptions(ColorMode.Swatches, 8, clear), PaintTiming.Default);
    }

    private static async Task Settle(Func<bool> done)
    {
        for (int i = 0; i < 200 && !done(); i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(15);
        }

        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task Paints_the_whole_picture_and_says_so()
    {
        FakeMachine machine = new();
        PaintingViewModel page = new(Session(), new FakeGame(FakeGame.Rust()), new SurfaceService(), new FakeEngines(machine), "F8", "F9");

        await page.StartAsync();
        await Settle(() => page.IsOver);

        Assert.Equal(PaintState.Finished, page.State);
        Assert.Equal("Finished", page.StatusText);
        Assert.Equal(1.0, page.Fraction, 3);
        int strokes = Session().Request.Plan.StrokeCount;
        Assert.Equal($"{strokes:N0} of {strokes:N0} strokes", page.ProgressText);
        Assert.True(page.DoneCommand.CanExecute(null));
        Assert.False(page.PauseResumeCommand.CanExecute(null));
        Assert.Contains("Save it in the game", page.Hint);
        Assert.False(machine.ButtonDown);
    }

    [AvaloniaFact]
    public async Task Fails_with_an_explanation_when_rust_is_not_running()
    {
        PaintingViewModel page = new(Session(), new FakeGame(null), new SurfaceService(), new FakeEngines(new FakeMachine()), "F8", "F9");

        await page.StartAsync();

        Assert.Equal(PaintState.Failed, page.State);
        Assert.True(page.IsFailed);
        Assert.Contains("not running", page.Detail);
    }

    [AvaloniaFact]
    public async Task Waits_for_rust_and_carries_on_when_the_start_key_is_pressed_in_it()
    {
        FakeMachine machine = new() { GameInFront = false };
        PaintingViewModel page = new(Session(), new FakeGame(FakeGame.Rust()), new SurfaceService(), new FakeEngines(machine), "F8", "F9");

        Task run = page.StartAsync();
        await Settle(() => page.IsPaused);

        Assert.True(page.IsPaused);
        Assert.Equal("Rust is not in front.", page.Detail);
        Assert.Empty(machine.Log);
        Assert.Contains("press F8", page.Hint);
        Assert.Equal("Resume", page.PauseResumeText);

        machine.GameInFront = true;
        page.OnHotkey(HotkeyAction.StartPause);
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        await Settle(() => page.IsOver);

        Assert.Equal(PaintState.Finished, page.State);
        Assert.NotEmpty(machine.Log);
    }

    [AvaloniaFact]
    public async Task The_pause_key_pauses_and_the_same_key_resumes()
    {
        FakeMachine machine = new();
        PaintingViewModel page = new(Session(), new FakeGame(FakeGame.Rust()), new SurfaceService(), new FakeEngines(machine), "F8", "F9");

        // The key is pressed while the first stroke is being painted.
        machine.Before = what =>
        {
            if (what == "down")
            {
                machine.Before = null;
                page.OnHotkey(HotkeyAction.StartPause);
            }
        };

        Task run = page.StartAsync();
        await Settle(() => page.IsPaused);

        Assert.True(page.IsPaused);
        Assert.Contains("Paused with F8", page.Detail);

        page.OnHotkey(HotkeyAction.StartPause);
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        await Settle(() => page.IsOver);

        Assert.Equal(PaintState.Finished, page.State);
    }

    [AvaloniaFact]
    public async Task The_abort_key_stops_for_good()
    {
        FakeMachine machine = new();
        PaintingViewModel page = new(Session(), new FakeGame(FakeGame.Rust()), new SurfaceService(), new FakeEngines(machine), "F8", "F9");

        machine.Before = what =>
        {
            if (what == "down")
            {
                machine.Before = null;
                page.OnHotkey(HotkeyAction.Abort);
            }
        };

        await page.StartAsync();
        await Settle(() => page.IsOver);

        Assert.Equal(PaintState.Aborted, page.State);
        Assert.Equal("Stopped", page.StatusText);
        Assert.False(machine.ButtonDown);
        Assert.True(page.DoneCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Moving_the_mouse_shows_why_it_paused()
    {
        FakeMachine machine = new();
        PaintingViewModel page = new(Session(), new FakeGame(FakeGame.Rust()), new SurfaceService(), new FakeEngines(machine), "F8", "F9");

        machine.Before = what =>
        {
            if (what == "up")
            {
                machine.Before = null;
                machine.Cursor = new ScreenPoint(5, 5);
            }
        };

        Task run = page.StartAsync();
        await Settle(() => page.IsPaused);

        Assert.Equal("The mouse was moved.", page.Detail);

        page.AbortCommand.Execute(null);
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [AvaloniaFact]
    public async Task Done_asks_to_leave_the_page()
    {
        PaintingViewModel page = new(Session(), new FakeGame(FakeGame.Rust()), new SurfaceService(), new FakeEngines(new FakeMachine()), "F8", "F9");
        bool done = false;
        page.DoneRequested += () => done = true;

        await page.StartAsync();
        await Settle(() => page.IsOver);
        page.DoneCommand.Execute(null);

        Assert.True(done);
    }

    [AvaloniaFact]
    public async Task Gives_a_time_left_that_shrinks_to_nothing()
    {
        PaintingViewModel page = new(Session(), new FakeGame(FakeGame.Rust()), new SurfaceService(), new FakeEngines(new FakeMachine()), "F8", "F9");
        string before = page.RemainingText;

        await page.StartAsync();
        await Settle(() => page.IsOver);

        Assert.NotEqual("", before);
        Assert.Equal("", page.RemainingText);
    }
}

public class PaintingNavigationTests
{
    private static MainWindowViewModel Shell(FakeGame game, FakeHotkeys hotkeys, FakeMachine machine, FakeHologram? hologram = null, FakeOverlay? overlay = null)
    {
        AppSettingsService settings = TestSettings.Create();
        return new MainWindowViewModel(new UpdateService(), new DialogService(), settings, new SurfaceService(), game, hotkeys, new FakeEngines(machine), hologram ?? new FakeHologram(), overlay ?? new FakeOverlay());
    }

    private static StudioViewModel PreparedStudio(MainWindowViewModel shell)
    {
        shell.Sidebar.NavigateTo(SidebarPage.Studio);
        StudioViewModel studio = Assert.IsType<StudioViewModel>(shell.CurrentPage);

        string path = Path.Combine(Path.GetTempPath(), $"rp-nav-{Guid.NewGuid():N}.png");
        TestImages.SavePng(TestImages.Rainbow(), path);
        Assert.True(studio.TryLoad(path));
        studio.ProcessNow();
        File.Delete(path);
        return studio;
    }

    [AvaloniaFact]
    public void Painting_from_studio_shows_the_get_ready_page_and_switches_the_hotkeys_on()
    {
        FakeHotkeys hotkeys = new();
        MainWindowViewModel shell = Shell(new FakeGame(FakeGame.Rust()), hotkeys, new FakeMachine());
        StudioViewModel studio = PreparedStudio(shell);
        Assert.False(hotkeys.IsActive);

        Assert.True(studio.PaintCommand.CanExecute(null));
        studio.PaintCommand.Execute(null);

        GetReadyViewModel ready = Assert.IsType<GetReadyViewModel>(shell.CurrentPage);
        Assert.True(hotkeys.IsActive);

        // Waiting only takes the start key, so Escape still works in the game's own screens.
        KeyValuePair<HotkeyAction, Hotkey> only = Assert.Single(hotkeys.Bindings!);
        Assert.Equal(HotkeyAction.StartPause, only.Key);
        Assert.Equal("Space", only.Value.Display);
        Assert.Equal(studio.Plan, ready.Request.Plan);
    }

    [AvaloniaFact]
    public void Painting_is_not_offered_without_a_finished_plan()
    {
        MainWindowViewModel shell = Shell(new FakeGame(FakeGame.Rust()), new FakeHotkeys(), new FakeMachine());
        shell.Sidebar.NavigateTo(SidebarPage.Studio);
        StudioViewModel studio = Assert.IsType<StudioViewModel>(shell.CurrentPage);

        Assert.False(studio.PaintCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void Back_returns_to_studio_and_gives_the_hotkeys_back()
    {
        FakeHotkeys hotkeys = new();
        MainWindowViewModel shell = Shell(new FakeGame(FakeGame.Rust()), hotkeys, new FakeMachine());
        StudioViewModel studio = PreparedStudio(shell);
        studio.PaintCommand.Execute(null);
        GetReadyViewModel ready = Assert.IsType<GetReadyViewModel>(shell.CurrentPage);

        ready.BackCommand.Execute(null);

        Assert.Same(studio, shell.CurrentPage);
        Assert.False(hotkeys.IsActive);
    }

    [AvaloniaFact]
    public void A_refused_hotkey_is_explained_on_the_page()
    {
        FakeHotkeys hotkeys = new();
        hotkeys.Refuse.Add(HotkeyAction.StartPause);
        MainWindowViewModel shell = Shell(new FakeGame(FakeGame.Rust()), hotkeys, new FakeMachine());
        PreparedStudio(shell).PaintCommand.Execute(null);

        GetReadyViewModel ready = Assert.IsType<GetReadyViewModel>(shell.CurrentPage);

        Assert.True(ready.HasHotkeyProblem);
        Assert.Contains("Space", ready.HotkeyProblem);
        Assert.DoesNotContain("Esc", ready.HotkeyProblem);
    }

    [AvaloniaFact]
    public async Task The_start_key_on_the_get_ready_page_begins_painting_and_the_page_follows()
    {
        FakeHotkeys hotkeys = new();
        FakeMachine machine = new();
        MainWindowViewModel shell = Shell(new FakeGame(FakeGame.Rust()), hotkeys, machine);
        PreparedStudio(shell).PaintCommand.Execute(null);

        hotkeys.Press(HotkeyAction.StartPause);
        for (int i = 0; i < 200 && shell.CurrentPage is not PaintingViewModel; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(15);
        }

        PaintingViewModel painting = Assert.IsType<PaintingViewModel>(shell.CurrentPage);
        for (int i = 0; i < 400 && !painting.IsOver; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(15);
        }

        Assert.Equal(PaintState.Finished, painting.State);

        // Painting took both keys while it ran, and gave them back the moment it was over.
        Assert.Equal(2, hotkeys.Bindings!.Count);
        Assert.False(hotkeys.IsActive);

        // Back to studio once it is over.
        painting.DoneCommand.Execute(null);
        Assert.IsType<StudioViewModel>(shell.CurrentPage);
        Assert.False(hotkeys.IsActive);
    }

    [AvaloniaFact]
    public async Task Clicking_around_does_not_leave_a_painting_that_is_going()
    {
        FakeHotkeys hotkeys = new();
        FakeMachine machine = new() { GameInFront = false };
        MainWindowViewModel shell = Shell(new FakeGame(FakeGame.Rust()), hotkeys, machine);
        PreparedStudio(shell).PaintCommand.Execute(null);

        GetReadyViewModel ready = Assert.IsType<GetReadyViewModel>(shell.CurrentPage);
        ready.StartCommand.Execute(null);
        PaintingViewModel painting = Assert.IsType<PaintingViewModel>(shell.CurrentPage);

        for (int i = 0; i < 200 && !painting.IsPaused; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(15);
        }

        shell.Sidebar.NavigateTo(SidebarPage.Home);
        Assert.Same(painting, shell.CurrentPage);
        Assert.True(hotkeys.IsActive);

        // The abort key still reaches it.
        hotkeys.Press(HotkeyAction.Abort);
        for (int i = 0; i < 200 && !painting.IsOver; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(15);
        }

        Assert.Equal(PaintState.Aborted, painting.State);

        // Now that it is over, navigating works again.
        shell.Sidebar.NavigateTo(SidebarPage.Home);
        Assert.IsType<HomeViewModel>(shell.CurrentPage);
        Assert.False(hotkeys.IsActive);
    }
}

public class PaintingKeySettingsTests
{
    private static SettingsViewModel Page(AppSettingsService? settings = null)
        => new(settings ?? TestSettings.Create(), new UpdateService(), new DialogService());

    [Fact]
    public void Starts_with_space_and_escape()
    {
        SettingsViewModel page = Page();

        Assert.Equal("Space", page.StartPauseKeyText);
        Assert.Equal("Esc", page.AbortKeyText);
        Assert.Equal("Space", page.StartPauseButtonText);
        Assert.Null(page.CapturingAction);
    }

    [Fact]
    public void Choosing_a_key_saves_it()
    {
        AppSettingsService settings = TestSettings.Create();
        SettingsViewModel page = Page(settings);

        page.CaptureStartPauseCommand.Execute(null);
        Assert.Equal(HotkeyAction.StartPause, page.CapturingAction);
        Assert.Equal("Press a key...", page.StartPauseButtonText);

        Assert.True(page.CaptureKey(0x50, 0));   // P

        Assert.Equal("P", page.StartPauseKeyText);
        Assert.Equal("P", settings.Current.StartPauseHotkey);
        Assert.Null(page.CapturingAction);
        Assert.Equal("P", page.StartPauseButtonText);
    }

    [Fact]
    public void A_key_with_modifiers_can_be_chosen()
    {
        SettingsViewModel page = Page();
        page.CaptureAbortCommand.Execute(null);

        page.CaptureKey(0x77, Hotkey.Ctrl | Hotkey.Shift);

        Assert.Equal("Ctrl+Shift+F8", page.AbortKeyText);
    }

    [Fact]
    public void Keys_pressed_when_nothing_is_being_chosen_are_not_used_up()
    {
        SettingsViewModel page = Page();

        Assert.False(page.CaptureKey(0x50, 0));
        Assert.Equal("Space", page.StartPauseKeyText);
    }

    [Fact]
    public void A_key_that_cannot_be_used_is_refused_and_it_keeps_waiting()
    {
        SettingsViewModel page = Page();
        page.CaptureStartPauseCommand.Execute(null);

        Assert.True(page.CaptureKey(0x5B, 0));   // the Windows key

        Assert.True(page.HasHotkeyMessage);
        Assert.Equal(HotkeyAction.StartPause, page.CapturingAction);
        Assert.Equal("Space", page.StartPauseKeyText);

        page.CaptureKey(0x4B, 0);
        Assert.False(page.HasHotkeyMessage);
        Assert.Equal("K", page.StartPauseKeyText);
    }

    [Fact]
    public void The_same_key_cannot_do_both_jobs()
    {
        SettingsViewModel page = Page();
        page.CaptureStartPauseCommand.Execute(null);

        // Escape is the stop key.
        page.CaptureKey(0x1B, 0);

        Assert.Equal("Space", page.StartPauseKeyText);
        Assert.Contains("Esc is already used for stopping", page.HotkeyMessage);
        Assert.Equal(HotkeyAction.StartPause, page.CapturingAction);
    }

    [Fact]
    public void Cancelling_leaves_the_key_alone()
    {
        SettingsViewModel page = Page();
        page.CaptureAbortCommand.Execute(null);

        page.CancelCapture();

        Assert.Null(page.CapturingAction);
        Assert.Equal("Esc", page.AbortKeyText);
        Assert.Equal("Esc", page.AbortButtonText);
    }

    [Fact]
    public void Resetting_brings_the_defaults_back()
    {
        AppSettingsService settings = TestSettings.Create();
        SettingsViewModel page = Page(settings);
        page.CaptureStartPauseCommand.Execute(null);
        page.CaptureKey(0x50, 0);
        page.CaptureAbortCommand.Execute(null);
        page.CaptureKey(0x51, 0);

        page.ResetHotkeysCommand.Execute(null);

        Assert.Equal("Space", page.StartPauseKeyText);
        Assert.Equal("Esc", page.AbortKeyText);
        Assert.Equal("Space", settings.Current.StartPauseHotkey);
        Assert.Equal("Esc", settings.Current.AbortHotkey);
    }

    [Fact]
    public void The_chosen_keys_are_what_painting_uses()
    {
        AppSettingsService settings = TestSettings.Create();
        SettingsViewModel page = Page(settings);
        page.CaptureStartPauseCommand.Execute(null);
        page.CaptureKey(0x50, 0);

        IReadOnlyDictionary<HotkeyAction, Hotkey> keys = HotkeyDefaults.FromSettings(settings.Current.StartPauseHotkey, settings.Current.AbortHotkey);

        Assert.Equal(new Hotkey(0, 0x50), keys[HotkeyAction.StartPause]);
        Assert.Equal(HotkeyDefaults.Abort, keys[HotkeyAction.Abort]);
    }

    [Theory]
    [InlineData(Avalonia.Input.Key.Space, 0x20u)]
    [InlineData(Avalonia.Input.Key.Escape, 0x1Bu)]
    [InlineData(Avalonia.Input.Key.A, (uint)'A')]
    [InlineData(Avalonia.Input.Key.Z, (uint)'Z')]
    [InlineData(Avalonia.Input.Key.D5, (uint)'5')]
    [InlineData(Avalonia.Input.Key.F1, 0x70u)]
    [InlineData(Avalonia.Input.Key.F8, 0x77u)]
    [InlineData(Avalonia.Input.Key.F24, 0x87u)]
    [InlineData(Avalonia.Input.Key.PageDown, 0x22u)]
    [InlineData(Avalonia.Input.Key.LWin, 0u)]
    public void Turns_pressed_keys_into_windows_keys(Avalonia.Input.Key key, uint expected)
        => Assert.Equal(expected, RustyPainter.Views.SettingsView.ToVirtualKey(key));
}

public class HologramTests
{
    private static PaintRequest Request(string surfaceId = "sign-small")
    {
        SurfaceService surfaces = new();
        Surface surface = surfaces.Catalog.Surfaces.Single(s => s.Id == surfaceId);
        IReadOnlyList<Rgb> swatches = surfaces.Catalog.Layouts[surface.Layout].Swatches.ColorGrid!.Colors.Select(Rgb.Parse).ToList();
        int[] cells = new int[8 * 4];
        for (int i = 0; i < cells.Length; i++)
            cells[i] = i % 8 < 4 ? 1 : Quantizer.Unpainted;

        return new PaintRequest(new PaintPlan(8, 4, swatches, cells), surface, ColorMode.Swatches, 8, "cat.png");
    }

    private static GetReadyViewModel Page(FakeGame game, FakeHologram hologram, PaintRequest? request = null)
        => new(request ?? Request(), game, new SurfaceService(), TestSettings.Create(), hologram, new Hotkey(0, 0x20), new Hotkey(0, 0x1B));

    [AvaloniaFact]
    public void The_picture_is_shown_over_the_game_where_it_will_be_painted()
    {
        FakeHologram hologram = new();

        GetReadyViewModel page = Page(new FakeGame(FakeGame.Rust()), hologram);

        Assert.True(hologram.IsShown);
        Assert.Equal(new ScreenRect(669, 537, 1317, 880), hologram.Area);
        Assert.Equal(0.55, hologram.Opacity, 3);
        Assert.Equal((8, 4), (hologram.Picture!.Width, hologram.Picture.Height));
        Assert.Equal(hologram.Area, page.PaintArea);
    }

    [AvaloniaFact]
    public void The_area_follows_the_game_resolution()
    {
        FakeHologram hologram = new();

        Page(new FakeGame(FakeGame.Rust(1920, 1080)), hologram);

        Assert.Equal(669 * 0.75, hologram.Area.X1, 6);
        Assert.Equal(880 * 0.75, hologram.Area.Y2, 6);
    }

    [AvaloniaFact]
    public void Nothing_is_shown_while_rust_is_not_running_and_it_appears_when_it_does()
    {
        FakeHologram hologram = new();
        FakeGame game = new(null);
        GetReadyViewModel page = Page(game, hologram);

        Assert.False(hologram.IsShown);
        Assert.Null(page.PaintArea);

        game.Window = FakeGame.Rust();
        page.Refresh();

        Assert.True(hologram.IsShown);

        game.Window = null;
        page.Refresh();

        Assert.False(hologram.IsShown);
    }

    [AvaloniaFact]
    public void The_fine_tuning_moves_the_picture_with_the_painting()
    {
        FakeHologram hologram = new();
        GetReadyViewModel page = Page(new FakeGame(FakeGame.Rust()), hologram);

        page.NudgeX = 5;
        page.NudgeY = -3;

        Assert.Equal(new ScreenRect(674, 534, 1322, 877), hologram.Area);

        // What is painted moves by exactly the same amount.
        PaintSession? session = null;
        page.StartRequested += s => session = s;
        page.StartCommand.Execute(null);
        Assert.Equal((5d, -3d), (session!.Options.NudgeX, session.Options.NudgeY));
    }

    [AvaloniaFact]
    public void The_picture_can_be_turned_off_and_its_strength_changed()
    {
        FakeHologram hologram = new();
        GetReadyViewModel page = Page(new FakeGame(FakeGame.Rust()), hologram);

        page.HologramOpacity = 80;
        Assert.Equal(0.8, hologram.Opacity, 3);
        Assert.Equal("80%", page.HologramOpacityText);

        page.HologramOpacity = 500;
        Assert.Equal(90, page.HologramOpacity);

        page.ShowHologram = false;
        Assert.False(hologram.IsShown);

        page.ShowHologram = true;
        Assert.True(hologram.IsShown);
    }

    [AvaloniaFact]
    public void The_picture_is_gone_before_the_first_stroke_and_when_the_page_is_left()
    {
        FakeHologram hologram = new();
        GetReadyViewModel page = Page(new FakeGame(FakeGame.Rust()), hologram);
        bool shownAtStart = true;
        page.StartRequested += _ => shownAtStart = hologram.IsShown;

        page.StartCommand.Execute(null);

        Assert.False(shownAtStart);

        GetReadyViewModel other = Page(new FakeGame(FakeGame.Rust()), hologram);
        Assert.True(hologram.IsShown);

        other.Dispose();
        Assert.False(hologram.IsShown);
    }

    [AvaloniaFact]
    public void The_window_that_shows_it_takes_a_picture_and_a_place()
    {
        RustyPainter.Windows.HologramWindow window = new();
        PixelBuffer picture = PixelBuffer.Solid(2, 2, 10, 20, 30);

        using var bitmap = BitmapPixels.ToBitmap(picture);
        window.Image = bitmap;
        window.PictureOpacity = 0.4;
        window.MoveTo(new Avalonia.PixelRect(100, 200, 640, 320));
        window.Show();

        Assert.Equal(new Avalonia.PixelPoint(100, 200), window.Position);
        Assert.Equal(640 / window.RenderScaling, window.Width, 3);
        Assert.Equal(320 / window.RenderScaling, window.Height, 3);
        Assert.Equal(0.4, window.PictureOpacity, 3);
        Assert.True(window.Topmost);
        Assert.False(window.ShowInTaskbar);

        window.Close();
    }
}

public class ForegroundGameCheckTests
{
    private sealed class Clock
    {
        public TimeSpan Now { get; set; } = TimeSpan.FromSeconds(100);
    }

    private sealed class CountingGame(GameWindow? window) : IGameWindowService
    {
        public int Lookups { get; private set; }
        public GameWindow? Window { get; set; } = window;

        public GameWindow? Find(string processName = IGameWindowService.RustProcessName)
        {
            Lookups++;
            return Window;
        }
    }

    private static GameWindow RustWithProcess(int pid) => new(1, pid, new ScreenRect(0, 0, 2560, 1440), false, true);

    [Fact]
    public void Knows_the_game_is_in_front_the_very_first_time_it_is_asked()
    {
        CountingGame game = new(RustWithProcess(4242));
        ForegroundGameCheck check = new(game, () => 4242, () => new Clock().Now);

        Assert.True(check.IsGameInFront());
        Assert.Equal(1, game.Lookups);
    }

    [Fact]
    public void Does_not_look_for_the_game_again_while_it_is_in_front()
    {
        CountingGame game = new(RustWithProcess(4242));
        ForegroundGameCheck check = new(game, () => 4242, () => new Clock().Now);

        for (int i = 0; i < 50; i++)
            Assert.True(check.IsGameInFront());

        Assert.Equal(1, game.Lookups);
    }

    [Fact]
    public void Another_program_in_front_is_not_the_game_and_lookups_are_rationed()
    {
        Clock clock = new();
        CountingGame game = new(RustWithProcess(4242));
        ForegroundGameCheck check = new(game, () => 777, () => clock.Now);

        for (int i = 0; i < 100; i++)
            Assert.False(check.IsGameInFront());

        // A hundred keys typed in another program cause one lookup, not a hundred.
        Assert.Equal(1, game.Lookups);

        clock.Now += ForegroundGameCheck.LookupEvery + TimeSpan.FromMilliseconds(1);
        Assert.False(check.IsGameInFront());
        Assert.Equal(2, game.Lookups);
    }

    [Fact]
    public void Notices_a_game_that_was_started_after_the_check_began()
    {
        Clock clock = new();
        CountingGame game = new(null);
        int front = 4242;
        ForegroundGameCheck check = new(game, () => front, () => clock.Now);

        Assert.False(check.IsGameInFront());

        game.Window = RustWithProcess(4242);
        clock.Now += TimeSpan.FromSeconds(3);

        Assert.True(check.IsGameInFront());
    }

    [Fact]
    public void Follows_a_game_that_was_restarted_with_a_new_process()
    {
        Clock clock = new();
        CountingGame game = new(RustWithProcess(100));
        int front = 100;
        ForegroundGameCheck check = new(game, () => front, () => clock.Now);

        Assert.True(check.IsGameInFront());

        game.Window = RustWithProcess(200);
        front = 200;
        clock.Now += TimeSpan.FromSeconds(3);

        Assert.True(check.IsGameInFront());
    }

    [Fact]
    public void Nothing_in_front_is_not_the_game()
    {
        ForegroundGameCheck check = new(new CountingGame(RustWithProcess(1)), () => 0, () => TimeSpan.Zero);

        Assert.False(check.IsGameInFront());
    }
}

public class RealKeyboardHookTests
{
    /// <summary>
    /// Sends one real key through Windows and checks that the hook sees it. F24 is used because no program binds it, and
    /// the hook takes it, so nothing else ever sees it either.
    /// </summary>
    private static bool SendF24(bool up)
    {
        RustyPainter.Service.Native.InputRecord record = new()
        {
            Type = RustyPainter.Service.Native.InputNativeMethods.InputKeyboard,
            Union = new RustyPainter.Service.Native.InputUnion
            {
                Keyboard = new RustyPainter.Service.Native.KeyboardInput
                {
                    VirtualKey = 0x87,
                    Flags = up ? RustyPainter.Service.Native.InputNativeMethods.KeyUp : 0
                }
            }
        };

        return RustyPainter.Service.Native.InputNativeMethods.SendInput(1, [record], System.Runtime.InteropServices.Marshal.SizeOf<RustyPainter.Service.Native.InputRecord>()) == 1;
    }

    private static async Task<bool> WaitFor(Func<bool> condition)
    {
        for (int i = 0; i < 100 && !condition(); i++)
            await Task.Delay(20);

        return condition();
    }

    [Fact]
    public async Task A_key_pressed_while_the_game_is_in_front_reaches_the_service_once()
    {
        List<HotkeyAction> pressed = [];
        using GlobalHotkeyService service = new(() => true, acceptInjectedKeys: true);
        service.Pressed += action => { lock (pressed) pressed.Add(action); };
        Assert.Empty(service.Start(new Dictionary<HotkeyAction, Hotkey> { [HotkeyAction.StartPause] = new Hotkey(0, 0x87) }));

        Assert.SkipUnless(SendF24(up: false), "Windows would not take simulated input here.");
        Assert.True(await WaitFor(() => { lock (pressed) return pressed.Count > 0; }), "The hook never saw the key.");

        // A held key repeats, which must not count again.
        SendF24(up: false);
        SendF24(up: false);
        SendF24(up: true);
        await Task.Delay(150);

        lock (pressed)
            Assert.Equal([HotkeyAction.StartPause], pressed);
    }

    [Fact]
    public async Task A_key_pressed_while_another_program_is_in_front_is_left_alone()
    {
        int pressed = 0;
        using GlobalHotkeyService service = new(() => false, acceptInjectedKeys: true);
        service.Pressed += _ => Interlocked.Increment(ref pressed);
        service.Start(new Dictionary<HotkeyAction, Hotkey> { [HotkeyAction.StartPause] = new Hotkey(0, 0x87) });

        Assert.SkipUnless(SendF24(up: false), "Windows would not take simulated input here.");
        SendF24(up: true);
        await Task.Delay(300);

        Assert.Equal(0, pressed);
    }

    [Fact]
    public async Task Keys_sent_by_programs_are_ignored_unless_asked_for()
    {
        int pressed = 0;
        using GlobalHotkeyService service = new(() => true);
        service.Pressed += _ => Interlocked.Increment(ref pressed);
        service.Start(new Dictionary<HotkeyAction, Hotkey> { [HotkeyAction.StartPause] = new Hotkey(0, 0x87) });

        Assert.SkipUnless(SendF24(up: false), "Windows would not take simulated input here.");
        SendF24(up: true);
        await Task.Delay(300);

        Assert.Equal(0, pressed);
    }

    [Fact]
    public async Task Stopping_stops_listening()
    {
        int pressed = 0;
        using GlobalHotkeyService service = new(() => true, acceptInjectedKeys: true);
        service.Pressed += _ => Interlocked.Increment(ref pressed);
        service.Start(new Dictionary<HotkeyAction, Hotkey> { [HotkeyAction.StartPause] = new Hotkey(0, 0x87) });
        service.Stop();

        Assert.SkipUnless(SendF24(up: false), "Windows would not take simulated input here.");
        SendF24(up: true);
        await Task.Delay(300);

        Assert.Equal(0, pressed);
    }
}
