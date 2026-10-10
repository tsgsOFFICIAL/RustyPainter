using RustyPainter.Imaging;
using RustyPainter.Interface;
using RustyPainter.Localization;
using RustyPainter.Service.Logging;
using System.Threading;
using System.Threading.Tasks;
using System;

namespace RustyPainter.Painting;

/// <summary>Where the painter is in its work.</summary>
public enum PaintState
{
    /// <summary>Nothing has been started.</summary>
    Idle,

    /// <summary>Sending input to the game.</summary>
    Running,

    /// <summary>Stopped on purpose or for safety. Nothing is sent until it is resumed.</summary>
    Paused,

    /// <summary>Every step was done.</summary>
    Finished,

    /// <summary>Stopped by the user for good.</summary>
    Aborted,

    /// <summary>Stopped because something went wrong. <see cref="PaintEngine.FailureMessage"/> says what.</summary>
    Failed
}

/// <summary>How far the painting has come.</summary>
/// <param name="StepIndex">The step being done.</param>
/// <param name="StepCount">How many steps there are.</param>
/// <param name="StrokesDone">How many strokes are painted.</param>
/// <param name="StrokeCount">How many strokes there are in all.</param>
public readonly record struct PaintProgress(int StepIndex, int StepCount, int StrokesDone, int StrokeCount)
{
    /// <summary>The share of strokes that are done, from 0 to 1.</summary>
    public double Fraction => StrokeCount == 0 ? 0 : (double)StrokesDone / StrokeCount;
}

/// <summary>
/// Plays a <see cref="PaintScript"/> into the game with mouse and keyboard input. It only acts while the game is in
/// front and the mouse is where the painter left it, and it pauses itself the moment either stops being true.
/// A step that was cut short is simply done again after resuming, which is safe because painting the same
/// color over the same cells changes nothing.
/// </summary>
public sealed class PaintEngine
{
    /// <summary>How far, in pixels, the cursor may drift from where the painter put it before that counts as the user moving it.</summary>
    public const double MouseTolerance = 6;

    private readonly IPaintInput _input;
    private readonly IPaintSensors _sensors;
    private readonly PaintTiming _timing;
    private readonly Func<int, CancellationToken, Task> _delay;
    private readonly object _lock = new();

    private TaskCompletionSource _resume = NewGate();
    private CancellationTokenSource? _abort;
    private bool _pauseRequested;
    private ScreenPoint? _expectedCursor;
    private bool _buttonDown;
    private PaintState _state = PaintState.Idle;

    /// <param name="input">Where the mouse and keyboard input goes.</param>
    /// <param name="sensors">What is known about the machine.</param>
    /// <param name="timing">The pace.</param>
    /// <param name="delay">Waits a number of milliseconds. Tests pass one that does not wait.</param>
    public PaintEngine(IPaintInput input, IPaintSensors sensors, PaintTiming? timing = null, Func<int, CancellationToken, Task>? delay = null)
    {
        _input = input;
        _sensors = sensors;
        _timing = timing ?? PaintTiming.Default;
        _delay = delay ?? ((milliseconds, token) => Task.Delay(milliseconds, token));
    }

    /// <summary>Raised when <see cref="State"/> changes. May be raised from a background thread.</summary>
    public event Action<PaintState>? StateChanged;

    /// <summary>Raised as strokes are painted. May be raised from a background thread.</summary>
    public event Action<PaintProgress>? ProgressChanged;

    public PaintState State
    {
        get { lock (_lock) return _state; }
    }

    /// <summary>Why the painter is paused, for showing to the user. Empty when it is not paused.</summary>
    public string PauseReason { get; private set; } = "";

    /// <summary>What went wrong, once the state is <see cref="PaintState.Failed"/>.</summary>
    public string FailureMessage { get; private set; } = "";

    /// <summary>The step that is being done, or the one that will be done after resuming.</summary>
    public int CurrentStep { get; private set; }

    public PaintProgress Progress { get; private set; }

    /// <summary>
    /// Does the steps of the script one after the other. Finishes when every step is done, when
    /// <see cref="Abort"/> is called or when something goes wrong; it does not throw for those.
    /// </summary>
    /// <param name="script">What to paint.</param>
    /// <param name="startStep">The step to start at, to carry on an earlier run.</param>
    /// <param name="cancellation">Cancels the run just like <see cref="Abort"/>.</param>
    public async Task RunAsync(PaintScript script, int startStep = 0, CancellationToken cancellation = default)
    {
        using CancellationTokenSource abort = CancellationTokenSource.CreateLinkedTokenSource(cancellation);

        lock (_lock)
        {
            if (_state is PaintState.Running or PaintState.Paused)
                throw new InvalidOperationException("The painter is already working.");

            _abort = abort;
            _pauseRequested = false;
            _resume = NewGate();
        }

        PauseReason = "";
        FailureMessage = "";
        _expectedCursor = null;
        CurrentStep = Math.Clamp(startStep, 0, script.Steps.Count);
        int strokesDone = CountStrokes(script, CurrentStep);
        SetState(PaintState.Running);

        try
        {
            for (; CurrentStep < script.Steps.Count; CurrentStep++)
            {
                PaintStep step = script.Steps[CurrentStep];
                Report(script, strokesDone);

                // A step that gets interrupted is done again from its start once the painter is resumed.
                while (true)
                {
                    await CheckpointAsync(abort.Token, inStep: false);

                    try
                    {
                        await ExecuteAsync(step, abort.Token);
                        break;
                    }
                    catch (InterruptedException)
                    {
                        ReleaseButton();
                    }
                }

                if (step is PaintStrokeStep)
                    strokesDone++;
            }

            Report(script, strokesDone, finished: true);
            SetState(PaintState.Finished);
        }
        catch (OperationCanceledException)
        {
            ReleaseButton();
            SetState(PaintState.Aborted);
        }
        catch (Exception ex)
        {
            ReleaseButton();
            AppLog.Error("PaintEngine", $"Painting failed at step {CurrentStep}.", ex);
            FailureMessage = ex is PaintException ? ex.Message : Loc.Get("EngineInputFailed");
            SetState(PaintState.Failed);
        }
        finally
        {
            ReleaseButton();
            lock (_lock)
                _abort = null;
        }
    }

    /// <summary>Stops sending input until <see cref="Resume"/> is called. Safe to call from any thread, such as a hotkey.</summary>
    public void Pause(string? reason = null)
    {
        lock (_lock)
        {
            if (_state != PaintState.Running)
                return;

            _pauseRequested = true;
            PauseReason = reason ?? Loc.Get("EnginePaused");
        }
    }

    /// <summary>Carries on after a pause.</summary>
    public void Resume()
    {
        TaskCompletionSource gate;

        lock (_lock)
        {
            if (_state != PaintState.Paused && !_pauseRequested)
                return;

            _pauseRequested = false;
            _expectedCursor = null;
            PauseReason = "";
            gate = _resume;
        }

        gate.TrySetResult();
    }

    /// <summary>Stops for good. The button is released at once, even in the middle of a stroke.</summary>
    public void Abort()
    {
        CancellationTokenSource? abort;

        lock (_lock)
            abort = _abort;

        abort?.Cancel();

        // A painter that waits in a pause must wake up to notice.
        _resume.TrySetResult();
    }

    // ---- safety ----

    /// <summary>Raised inside a step when the painter stopped itself and the step has to be done again later.</summary>
    private sealed class InterruptedException : Exception;

    /// <summary>
    /// Called before every action. Stops the painter when the game is not in front or the mouse was moved by the user,
    /// and waits there when it is paused.
    /// </summary>
    private async Task CheckpointAsync(CancellationToken token, bool inStep)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();

            string? problem = null;
            TaskCompletionSource? gate = null;

            lock (_lock)
            {
                if (!_pauseRequested)
                {
                    problem = FindProblem();
                    if (problem is not null)
                    {
                        _pauseRequested = true;
                        PauseReason = problem;
                    }
                }

                if (_pauseRequested)
                {
                    if (_resume.Task.IsCompleted)
                        _resume = NewGate();

                    gate = _resume;
                }
            }

            if (gate is null)
            {
                // Resumed between the stop and the wait, so the state still says paused.
                if (State == PaintState.Paused)
                    SetState(PaintState.Running);

                return;
            }

            ReleaseButton();
            SetState(PaintState.Paused);

            // Inside a step the step is abandoned and redone later; between steps there is nothing to undo.
            if (inStep)
                throw new InterruptedException();

            await gate.Task.WaitAsync(token);
            token.ThrowIfCancellationRequested();

            lock (_lock)
            {
                if (_pauseRequested)
                    continue;
            }

            SetState(PaintState.Running);
            return;
        }
    }

    private string? FindProblem()
    {
        if (!_sensors.IsGameInFront())
            return Loc.Get("EngineRustNotInFront");

        if (_expectedCursor is { } expected)
        {
            ScreenPoint now = _sensors.CursorPosition;
            if (Math.Abs(now.X - expected.X) > MouseTolerance || Math.Abs(now.Y - expected.Y) > MouseTolerance)
                return Loc.Get("EngineMouseMoved");
        }

        return null;
    }

    private void ReleaseButton()
    {
        if (!_buttonDown)
            return;

        _buttonDown = false;

        try
        {
            _input.LeftUp();
        }
        catch (Exception ex)
        {
            AppLog.Error("PaintEngine", "Could not release the mouse button.", ex);
        }
    }

    // ---- input helpers, each one checked first ----

    private async Task WaitAsync(int milliseconds, CancellationToken token)
    {
        if (milliseconds > 0)
            await _delay(milliseconds, token);
    }

    private async Task MoveAsync(ScreenPoint point, CancellationToken token)
    {
        await CheckpointAsync(token, inStep: true);
        _input.MoveTo(point);
        _expectedCursor = point;
        await WaitAsync(_timing.Settle, token);
    }

    private async Task PressAsync(CancellationToken token)
    {
        await CheckpointAsync(token, inStep: true);
        _input.LeftDown();
        _buttonDown = true;
        await WaitAsync(_timing.Hold, token);
    }

    private async Task ReleaseAsync(CancellationToken token)
    {
        await CheckpointAsync(token, inStep: true);
        _input.LeftUp();
        _buttonDown = false;
    }

    private async Task ClickAsync(ScreenPoint point, int after, CancellationToken token)
    {
        await MoveAsync(point, token);
        await PressAsync(token);
        await ReleaseAsync(token);
        await WaitAsync(after, token);
    }

    private async Task TypeAsync(string text, CancellationToken token)
    {
        foreach (char key in text)
        {
            await CheckpointAsync(token, inStep: true);
            _input.PressKey(key);
            await WaitAsync(_timing.KeyGap, token);
        }
    }

    // ---- the steps ----

    private async Task ExecuteAsync(PaintStep step, CancellationToken token)
    {
        switch (step)
        {
            case ResetCameraStep reset:
                await ClickAsync(reset.At, _timing.CameraReset, token);
                break;

            case ScrollOutStep scroll:
                for (int i = 0; i < scroll.Count; i++)
                {
                    await CheckpointAsync(token, inStep: true);
                    _input.Scroll(-1);
                    await WaitAsync(_timing.ScrollGap, token);
                }

                break;

            case EnsureColorModeStep mode:
                await EnsureColorModeAsync(mode, token);
                break;

            case ClickControlStep click:
                await ClickAsync(click.At, _timing.AfterClick, token);
                break;

            case SetBrushValueStep value:
                await ClickAsync(value.At, _timing.FieldOpen, token);
                await TypeAsync(value.Text, token);
                await WaitAsync(_timing.AfterField, token);
                break;

            case SelectFrameStep frame:
                await SelectFrameAsync(frame, token);
                break;

            case SelectSwatchStep swatch:
                await ClickAsync(swatch.At, _timing.AfterClick, token);
                break;

            case TypeColorStep color:
                await ClickAsync(color.At, _timing.FieldOpen, token);
                await TypeAsync(color.Color.Hex[1..], token);
                await WaitAsync(_timing.AfterField, token);
                break;

            case PaintStrokeStep stroke:
                await PaintAsync(stroke, token);
                break;

            default:
                throw new PaintException(Loc.Format("EngineUnknownStep", step.GetType().Name));
        }
    }

    private async Task PaintAsync(PaintStrokeStep step, CancellationToken token)
    {
        await MoveAsync(step.From, token);
        await PressAsync(token);

        if (step.From != step.To)
        {
            await MoveAsync(step.To, token);
            await WaitAsync(_timing.Hold, token);
        }

        await ReleaseAsync(token);
        await WaitAsync(_timing.AfterStroke, token);
    }

    private async Task SelectFrameAsync(SelectFrameStep step, CancellationToken token)
    {
        // The open frame is drawn in a green circle. A click that was lost is tried again before painting goes on into the wrong frame.
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            await ClickAsync(step.At, _timing.AfterClick + 120, token);
            await CheckpointAsync(token, inStep: true);

            if (_sensors.ReadColor(step.Probe) is { } color && color.G - Math.Max(color.R, color.B) >= 25)
                return;

            AppLog.Debug("PaintEngine", $"Frame {step.Index + 1} did not show as open after click {attempt}.");
        }

        throw new PaintException(Loc.Format("EngineFrameFailed", step.Index + 1));
    }

    private async Task EnsureColorModeAsync(EnsureColorModeStep step, CancellationToken token)
    {
        ColorMode? current = await ReadModeAsync(step, token);

        // Without a reading the painter carries on and trusts the user to have the right mode, which the log records.
        if (current is null)
        {
            AppLog.Warn("PaintEngine", "Could not tell which color mode the game is in.");
            return;
        }

        if (current == step.Mode)
            return;

        // The toggle sits in a different place in each mode, so it is pressed where the game shows it now.
        await ClickAsync(current == ColorMode.Swatches ? step.SwatchToggle : step.FreeToggle, _timing.AfterClick + 150, token);

        if (await ReadModeAsync(step, token) != step.Mode)
            throw new PaintException(Loc.Get("EngineColorModeFailed"));
    }

    /// <summary>
    /// Tells which mode the game is in. The toggle button is grey in swatch mode and green in free color mode, and the two modes
    /// do not put it in the same place, so each place is looked at in turn.
    /// </summary>
    private async Task<ColorMode?> ReadModeAsync(EnsureColorModeStep step, CancellationToken token)
    {
        await CheckpointAsync(token, inStep: true);

        if (_sensors.ReadColor(step.FreeFill ?? step.FreeToggle) is { } free && IsReadable(free) && free.G - Math.Max(free.R, free.B) >= 15)
            return ColorMode.Free;

        if (_sensors.ReadColor(step.SwatchToggle) is { } swatch && IsReadable(swatch)
            && Math.Abs(swatch.R - swatch.G) <= 30 && Math.Abs(swatch.G - swatch.B) <= 30)
            return ColorMode.Swatches;

        return null;
    }

    // A black reading means the screen could not really be read, not that the toggle is black.
    private static bool IsReadable(Rgb color) => Math.Max(color.R, Math.Max(color.G, color.B)) >= 25;

    // ---- bookkeeping ----

    private static TaskCompletionSource NewGate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static int CountStrokes(PaintScript script, int upTo)
    {
        int count = 0;
        for (int i = 0; i < upTo && i < script.Steps.Count; i++)
            if (script.Steps[i] is PaintStrokeStep)
                count++;

        return count;
    }

    private void Report(PaintScript script, int strokesDone, bool finished = false)
    {
        Progress = new PaintProgress(finished ? script.Steps.Count : CurrentStep, script.Steps.Count, strokesDone, script.StrokeCount);
        ProgressChanged?.Invoke(Progress);
    }

    private void SetState(PaintState state)
    {
        lock (_lock)
        {
            if (_state == state)
                return;

            _state = state;
        }

        StateChanged?.Invoke(state);
    }
}

/// <summary>Painting stopped for a reason that can be told to the user in plain words.</summary>
public sealed class PaintException(string message) : Exception(message);
