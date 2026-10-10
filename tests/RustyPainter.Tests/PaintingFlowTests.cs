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
    [InlineData("Enter")]
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
    public void The_defaults_are_F8_and_F9()
    {
        IReadOnlyDictionary<HotkeyAction, Hotkey> keys = HotkeyDefaults.FromSettings(null, null);

        Assert.Equal("F8", keys[HotkeyAction.StartPause].Display);
        Assert.Equal("F9", keys[HotkeyAction.Abort].Display);
    }

    [Fact]
    public void Unreadable_settings_fall_back_to_the_defaults()
    {
        IReadOnlyDictionary<HotkeyAction, Hotkey> keys = HotkeyDefaults.FromSettings("nonsense", "Ctrl+F5");

        Assert.Equal("F8", keys[HotkeyAction.StartPause].Display);
        Assert.Equal("Ctrl+F5", keys[HotkeyAction.Abort].Display);
    }

    [Fact]
    public void The_same_key_for_both_actions_falls_back_to_the_defaults()
    {
        IReadOnlyDictionary<HotkeyAction, Hotkey> keys = HotkeyDefaults.FromSettings("F5", "F5");

        Assert.Equal("F8", keys[HotkeyAction.StartPause].Display);
        Assert.Equal("F9", keys[HotkeyAction.Abort].Display);
    }

    /// <summary>
    /// Registers a real system wide hotkey and lets go of it again. A key nobody uses is picked so nothing in
    /// the game or anywhere else is disturbed, and no key press is simulated.
    /// </summary>
    [Fact]
    public void The_real_service_registers_and_gives_the_key_back()
    {
        using GlobalHotkeyService service = new();
        Dictionary<HotkeyAction, Hotkey> keys = new()
        {
            [HotkeyAction.StartPause] = new Hotkey(Hotkey.Ctrl | Hotkey.Alt | Hotkey.Shift, 0x87),
            [HotkeyAction.Abort] = new Hotkey(Hotkey.Ctrl | Hotkey.Alt | Hotkey.Shift, 0x86)
        };

        IReadOnlyList<HotkeyAction> failed = service.Start(keys);

        Assert.Empty(failed);
        Assert.True(service.IsActive);

        // A second service cannot take a key that is already registered, which is how a clash shows up.
        using GlobalHotkeyService other = new();
        Assert.Equal(2, other.Start(keys).Count);
        other.Stop();

        service.Stop();
        Assert.False(service.IsActive);

        // Once it was given back anyone can have it again.
        using GlobalHotkeyService again = new();
        Assert.Empty(again.Start(keys));
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

    private static GetReadyViewModel Page(FakeGame game, AppSettingsService? settings = null, PaintRequest? request = null)
    {
        settings ??= new AppSettingsService();
        return new GetReadyViewModel(request ?? Request(), game, settings, new Hotkey(0, 0x77), new Hotkey(0, 0x78));
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
    public void The_choices_are_remembered_in_the_settings()
    {
        AppSettingsService settings = new();
        GetReadyViewModel page = Page(new FakeGame(FakeGame.Rust()), settings);

        page.ClearFirst = false;
        page.Speed = 35;

        Assert.False(settings.Current.ClearBeforePainting);
        Assert.Equal(35, settings.Current.PaintSpeed);

        GetReadyViewModel next = Page(new FakeGame(FakeGame.Rust()), settings);
        Assert.False(next.ClearFirst);
        Assert.Equal(35, next.Speed);
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
    private static MainWindowViewModel Shell(FakeGame game, FakeHotkeys hotkeys, FakeMachine machine)
    {
        AppSettingsService settings = new();
        return new MainWindowViewModel(new UpdateService(), new DialogService(), settings, new SurfaceService(), game, hotkeys, new FakeEngines(machine));
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
        Assert.Equal("F8", hotkeys.Bindings![HotkeyAction.StartPause].Display);
        Assert.Equal("F9", hotkeys.Bindings[HotkeyAction.Abort].Display);
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
        Assert.Contains("F8", ready.HotkeyProblem);
        Assert.DoesNotContain("F9", ready.HotkeyProblem);
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
        Assert.True(hotkeys.IsActive);

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
