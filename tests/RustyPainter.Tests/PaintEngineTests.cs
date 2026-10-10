using RustyPainter.Imaging;
using RustyPainter.Interface;
using RustyPainter.Painting;
using RustyPainter.Service;

namespace RustyPainter.Tests;

public class PaintEngineTests
{
    private static readonly Rgb[] Palette = [new Rgb(255, 0, 0), new Rgb(0, 0, 255)];

    private static PaintEngine Engine(FakeMachine machine) => new(machine, machine, new PaintTiming(), (_, token) =>
    {
        token.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    });

    private static PaintScript Script(params PaintStep[] steps) => new(steps);

    private static PaintStrokeStep Drag(double x1, double y1, double x2, double y2)
        => new(new Stroke(0, 0, 2, Vertical: false), new ScreenPoint(x1, y1), new ScreenPoint(x2, y2));

    private static PaintStrokeStep Dot(double x, double y)
        => new(new Stroke(0, 0, 1, Vertical: false), new ScreenPoint(x, y), new ScreenPoint(x, y));

    [Fact]
    public async Task A_drag_is_move_press_move_release()
    {
        FakeMachine machine = new();
        PaintEngine engine = Engine(machine);

        await engine.RunAsync(Script(Drag(10, 20, 110, 20)));

        Assert.Equal(["move 10,20", "down", "move 110,20", "up"], machine.Log);
        Assert.Equal(PaintState.Finished, engine.State);
    }

    [Fact]
    public async Task A_dot_is_a_press_and_release_in_one_place()
    {
        FakeMachine machine = new();

        await Engine(machine).RunAsync(Script(Dot(50, 60)));

        Assert.Equal(["move 50,60", "down", "up"], machine.Log);
    }

    [Fact]
    public async Task Clicking_a_control_moves_there_and_clicks()
    {
        FakeMachine machine = new();

        await Engine(machine).RunAsync(Script(new ClickControlStep("Clear", new ScreenPoint(5, 6)), new SelectSwatchStep(3, Palette[0], new ScreenPoint(7, 8))));

        Assert.Equal(["move 5,6", "down", "up", "move 7,8", "down", "up"], machine.Log);
    }

    [Fact]
    public async Task Brush_values_are_clicked_and_typed()
    {
        FakeMachine machine = new();

        await Engine(machine).RunAsync(Script(new SetBrushValueStep("Spacing", new ScreenPoint(9, 9), "0.05")));

        Assert.Equal(["move 9,9", "down", "up", "key 0", "key .", "key 0", "key 5"], machine.Log);
    }

    [Fact]
    public async Task A_free_color_is_typed_as_six_hex_digits_without_the_hash()
    {
        FakeMachine machine = new();

        await Engine(machine).RunAsync(Script(new TypeColorStep(new Rgb(255, 128, 10), new ScreenPoint(1, 1))));

        Assert.Equal("FF800A", string.Concat(machine.Log.Where(entry => entry.StartsWith("key ")).Select(entry => entry[4])));
    }

    [Fact]
    public async Task Scrolling_out_turns_the_wheel_backwards_once_per_count()
    {
        FakeMachine machine = new();

        await Engine(machine).RunAsync(Script(new ScrollOutStep(3)));

        Assert.Equal(["scroll -1", "scroll -1", "scroll -1"], machine.Log);
    }

    [Fact]
    public async Task Resetting_the_camera_is_a_click()
    {
        FakeMachine machine = new();

        await Engine(machine).RunAsync(Script(new ResetCameraStep(new ScreenPoint(4, 4))));

        Assert.Equal(["move 4,4", "down", "up"], machine.Log);
    }

    // ---- color mode ----

    [Fact]
    public async Task A_matching_color_mode_is_left_alone()
    {
        FakeMachine machine = new() { ToggleColor = new Rgb(110, 112, 115) };

        await Engine(machine).RunAsync(Script(new EnsureColorModeStep(ColorMode.Swatches, new ScreenPoint(1, 1), new ScreenPoint(2, 2))));

        Assert.Empty(machine.Log);
    }

    [Fact]
    public async Task The_wrong_color_mode_is_switched_with_the_toggle()
    {
        FakeMachine machine = new() { ToggleColor = new Rgb(110, 112, 115) };
        machine.Before = what =>
        {
            // Pressing the toggle turns it green.
            if (what == "up")
                machine.ToggleColor = new Rgb(60, 180, 70);
        };

        PaintEngine engine = Engine(machine);
        await engine.RunAsync(Script(new EnsureColorModeStep(ColorMode.Free, new ScreenPoint(1, 1), new ScreenPoint(2, 2))));

        Assert.Equal(["move 1,1", "down", "up"], machine.Log);
        Assert.Equal(PaintState.Finished, engine.State);
    }

    [Fact]
    public async Task A_toggle_that_does_not_switch_stops_with_an_explanation()
    {
        FakeMachine machine = new() { ToggleColor = new Rgb(110, 112, 115) };
        PaintEngine engine = Engine(machine);

        await engine.RunAsync(Script(new EnsureColorModeStep(ColorMode.Free, new ScreenPoint(1, 1), new ScreenPoint(2, 2)), Dot(5, 5)));

        Assert.Equal(PaintState.Failed, engine.State);
        Assert.Contains("color mode", engine.FailureMessage);
        Assert.DoesNotContain("move 5,5", machine.Log);
    }

    [Fact]
    public async Task An_unreadable_toggle_does_not_stop_the_painting()
    {
        FakeMachine machine = new() { ToggleColor = null };
        PaintEngine engine = Engine(machine);

        await engine.RunAsync(Script(new EnsureColorModeStep(ColorMode.Free, new ScreenPoint(1, 1), new ScreenPoint(2, 2)), Dot(5, 5)));

        Assert.Equal(PaintState.Finished, engine.State);
        Assert.Contains("move 5,5", machine.Log);
    }

    // ---- safety ----

    [Fact]
    public async Task Nothing_is_sent_while_the_game_is_not_in_front()
    {
        FakeMachine machine = new() { GameInFront = false };
        PaintEngine engine = Engine(machine);
        TaskCompletionSource paused = new();
        engine.StateChanged += state =>
        {
            if (state == PaintState.Paused)
                paused.TrySetResult();
        };

        Task run = engine.RunAsync(Script(Dot(1, 1), Dot(2, 2)));
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(machine.Log);
        Assert.Equal("Rust is not in front.", engine.PauseReason);

        engine.Abort();
        await run;
    }

    [Fact]
    public async Task Losing_the_game_in_the_middle_of_a_stroke_releases_the_button_and_redoes_the_stroke_after_resuming()
    {
        FakeMachine machine = new();
        PaintEngine engine = Engine(machine);
        TaskCompletionSource paused = new();
        engine.StateChanged += state =>
        {
            if (state == PaintState.Paused)
                paused.TrySetResult();
        };

        // The user switches to another window right after the button went down.
        machine.Before = what =>
        {
            if (what == "down")
                machine.GameInFront = false;
        };

        Task run = engine.RunAsync(Script(Drag(10, 10, 90, 10), Dot(50, 50)));
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(machine.ButtonDown);
        Assert.Contains("up", machine.Log);
        int countAtPause = machine.Log.Count;

        // Still nothing while it is paused.
        await Task.Delay(50);
        Assert.Equal(countAtPause, machine.Log.Count);

        machine.Before = null;
        machine.GameInFront = true;
        engine.Resume();
        await run;

        // The stroke was started again from its beginning, then the dot was painted.
        Assert.Equal(PaintState.Finished, engine.State);
        Assert.Equal(["move 10,10", "down", "up"], machine.Log.Take(3));
        Assert.Equal(["move 10,10", "down", "move 90,10", "up", "move 50,50", "down", "up"], machine.Log.Skip(3));
        Assert.False(machine.ButtonDown);
    }

    [Fact]
    public async Task Moving_the_mouse_pauses_the_painter()
    {
        FakeMachine machine = new();
        PaintEngine engine = Engine(machine);
        TaskCompletionSource paused = new();
        engine.StateChanged += state =>
        {
            if (state == PaintState.Paused)
                paused.TrySetResult();
        };

        machine.Before = what =>
        {
            // After the first click the user pushes the mouse away.
            if (what == "up")
                machine.Cursor = new ScreenPoint(900, 900);
        };

        Task run = engine.RunAsync(Script(new ClickControlStep("Clear", new ScreenPoint(5, 5)), Dot(60, 60)));
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("The mouse was moved.", engine.PauseReason);
        Assert.DoesNotContain("move 60,60", machine.Log);

        machine.Before = null;
        engine.Resume();
        await run;

        Assert.Contains("move 60,60", machine.Log);
        Assert.Equal(PaintState.Finished, engine.State);
    }

    [Fact]
    public async Task A_small_drift_of_the_cursor_is_not_a_mouse_move()
    {
        FakeMachine machine = new();
        machine.Before = what =>
        {
            if (what == "down")
                machine.Cursor = new ScreenPoint(machine.Cursor.X + 2, machine.Cursor.Y - 2);
        };

        PaintEngine engine = Engine(machine);
        await engine.RunAsync(Script(Drag(10, 10, 50, 10)));

        Assert.Equal(PaintState.Finished, engine.State);
    }

    [Fact]
    public async Task Pausing_by_request_stops_before_the_next_step_and_resuming_carries_on()
    {
        FakeMachine machine = new();
        PaintEngine engine = Engine(machine);
        TaskCompletionSource paused = new();
        engine.StateChanged += state =>
        {
            if (state == PaintState.Paused)
                paused.TrySetResult();
        };

        // The hotkey is pressed while the first dot is being painted.
        machine.Before = what =>
        {
            if (what == "down")
                engine.Pause("Paused with the hotkey.");
        };

        Task run = engine.RunAsync(Script(Dot(1, 1), Dot(2, 2), Dot(3, 3)));
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("Paused with the hotkey.", engine.PauseReason);
        Assert.False(machine.ButtonDown);
        Assert.DoesNotContain("move 3,3", machine.Log);

        machine.Before = null;
        engine.Resume();
        await run;

        Assert.Equal(PaintState.Finished, engine.State);
        Assert.Contains("move 3,3", machine.Log);
    }

    [Fact]
    public async Task Aborting_in_the_middle_of_a_stroke_lets_go_of_the_button_at_once()
    {
        FakeMachine machine = new();
        PaintEngine engine = Engine(machine);

        machine.Before = what =>
        {
            if (what == "move 90,10")
                engine.Abort();
        };

        await engine.RunAsync(Script(Drag(10, 10, 90, 10), Dot(50, 50)));

        Assert.Equal(PaintState.Aborted, engine.State);
        Assert.False(machine.ButtonDown);
        Assert.DoesNotContain("move 50,50", machine.Log);
        Assert.Equal("up", machine.Log[^1]);
    }

    [Fact]
    public async Task Aborting_while_paused_ends_the_run()
    {
        FakeMachine machine = new() { GameInFront = false };
        PaintEngine engine = Engine(machine);
        TaskCompletionSource paused = new();
        engine.StateChanged += state =>
        {
            if (state == PaintState.Paused)
                paused.TrySetResult();
        };

        Task run = engine.RunAsync(Script(Dot(1, 1)));
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(5));

        engine.Abort();
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(PaintState.Aborted, engine.State);
    }

    [Fact]
    public async Task A_failure_in_the_input_is_reported_and_the_button_is_released()
    {
        FakeMachine machine = new();
        PaintEngine engine = Engine(machine);

        machine.Before = what =>
        {
            if (what == "move 90,10")
                throw new InvalidOperationException("the system refused the input");
        };

        await engine.RunAsync(Script(Drag(10, 10, 90, 10)));

        Assert.Equal(PaintState.Failed, engine.State);
        Assert.NotEqual("", engine.FailureMessage);
        Assert.False(machine.ButtonDown);
    }

    // ---- progress ----

    [Fact]
    public async Task Reports_how_many_strokes_are_done()
    {
        FakeMachine machine = new();
        PaintEngine engine = Engine(machine);
        List<PaintProgress> reports = [];
        engine.ProgressChanged += reports.Add;

        PaintScript script = Script(new ClickControlStep("Brush", new ScreenPoint(1, 1)), Dot(2, 2), Dot(3, 3));
        await engine.RunAsync(script);

        Assert.Equal(0, reports[0].StrokesDone);
        Assert.Equal(2, reports[^1].StrokesDone);
        Assert.Equal(2, reports[^1].StrokeCount);
        Assert.Equal(1.0, reports[^1].Fraction);
        Assert.Equal(3, reports[^1].StepCount);
    }

    [Fact]
    public async Task A_run_can_start_part_way_through()
    {
        FakeMachine machine = new();
        PaintEngine engine = Engine(machine);

        await engine.RunAsync(Script(Dot(1, 1), Dot(2, 2), Dot(3, 3)), startStep: 2);

        Assert.Equal(["move 3,3", "down", "up"], machine.Log);
        Assert.Equal(3, engine.Progress.StrokesDone);
    }

    [Fact]
    public async Task Cannot_start_twice_at_once()
    {
        FakeMachine machine = new() { GameInFront = false };
        PaintEngine engine = Engine(machine);
        TaskCompletionSource paused = new();
        engine.StateChanged += state =>
        {
            if (state == PaintState.Paused)
                paused.TrySetResult();
        };

        Task run = engine.RunAsync(Script(Dot(1, 1)));
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.RunAsync(Script(Dot(2, 2))));

        engine.Abort();
        await run;
    }

    // ---- a whole picture ----

    [Fact]
    public async Task A_whole_script_for_a_real_surface_runs_to_the_end_with_the_button_always_released()
    {
        SurfaceService surfaces = new();
        Surface sign = surfaces.Catalog.Surfaces.Single(surface => surface.Id == "sign-small");
        ResolvedLayout layout = surfaces.Resolve(sign, 2560, 1440);
        IReadOnlyList<Rgb> swatches = layout.Swatches.ColorGrid!.Colors.Select(Rgb.Parse).ToList();

        int[] cells = new int[30 * 16];
        for (int i = 0; i < cells.Length; i++)
            cells[i] = (i / 30 / 4 + i % 30 / 5) % 4;

        PaintPlan plan = new(30, 16, swatches, cells);
        PaintScript script = PaintScriptBuilder.Build(plan, layout, new PaintOptions(ColorMode.Swatches, 4));

        FakeMachine machine = new();
        PaintEngine engine = Engine(machine);
        await engine.RunAsync(script);

        Assert.Equal(PaintState.Finished, engine.State);
        Assert.Equal(plan.StrokeCount, engine.Progress.StrokesDone);
        Assert.False(machine.ButtonDown);

        // Every press has its release.
        Assert.Equal(machine.Log.Count(entry => entry == "down"), machine.Log.Count(entry => entry == "up"));
    }
}
