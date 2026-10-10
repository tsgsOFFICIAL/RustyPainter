using RustyPainter.Imaging;
using RustyPainter.Interface;
using System.Collections.Generic;
using System.Linq;

namespace RustyPainter.Painting;

/// <summary>One thing to do in the game. The painter turns each step into mouse and keyboard input.</summary>
public abstract record PaintStep;

/// <summary>Clicks the camera reset button and waits for the view to settle, so the canvas is where it was measured.</summary>
public sealed record ResetCameraStep(ScreenPoint At) : PaintStep;

/// <summary>Scrolls the mouse wheel out, for canvases that open zoomed in.</summary>
public sealed record ScrollOutStep(int Count) : PaintStep;

/// <summary>Makes sure the game is in the wanted color mode, switching it with the toggle if it is not.</summary>
/// <param name="FreeFill">A point on the toggle where its fill shows in free color mode, since the middle of it is the icon. Defaults to the middle.</param>
public sealed record EnsureColorModeStep(ColorMode Mode, ScreenPoint SwatchToggle, ScreenPoint FreeToggle, ScreenPoint? FreeFill = null) : PaintStep;

/// <summary>Clicks a button, such as a tool or the clear button.</summary>
public sealed record ClickControlStep(string Control, ScreenPoint At) : PaintStep;

/// <summary>Types a value into a brush setting: clicks the number, types the text, and leaves the field.</summary>
public sealed record SetBrushValueStep(string Field, ScreenPoint At, string Text) : PaintStep;

/// <summary>Opens a frame of an animated sign by clicking its number.</summary>
/// <param name="Index">The frame, counted from 0.</param>
/// <param name="At">The number to click.</param>
/// <param name="Probe">A point that is green while the frame is the open one, to check the click did its work.</param>
public sealed record SelectFrameStep(int Index, ScreenPoint At, ScreenPoint Probe) : PaintStep;

/// <summary>Picks a color from the swatch grid.</summary>
public sealed record SelectSwatchStep(int Index, Rgb Color, ScreenPoint At) : PaintStep;

/// <summary>Types a color into the hex field of the free color picker.</summary>
public sealed record TypeColorStep(Rgb Color, ScreenPoint At) : PaintStep;

/// <summary>Paints one stroke: a click when <see cref="From"/> and <see cref="To"/> are the same cell, a drag otherwise.</summary>
public sealed record PaintStrokeStep(Stroke Stroke, ScreenPoint From, ScreenPoint To) : PaintStep;

/// <summary>Everything that has to be done in the game to paint a picture, in order.</summary>
public sealed class PaintScript(IReadOnlyList<PaintStep> steps)
{
    public IReadOnlyList<PaintStep> Steps { get; } = steps;

    /// <summary>How many strokes are painted.</summary>
    public int StrokeCount { get; } = steps.Count(step => step is PaintStrokeStep);

    /// <summary>How many times a color is picked.</summary>
    public int ColorChanges { get; } = steps.Count(step => step is SelectSwatchStep or TypeColorStep);
}

/// <summary>How the picture is to be painted, apart from the picture itself.</summary>
/// <param name="Mode">Whether the game is used in swatch mode or free color mode.</param>
/// <param name="BlockSize">The brush size in game units, which is also the size of one cell.</param>
/// <param name="ClearFirst">Whether to clear the canvas before painting.</param>
/// <param name="Spacing">The brush spacing to type in, from 0.01 to 1.</param>
/// <param name="Opacity">The brush opacity to type in, from 0.01 to 1.</param>
/// <param name="ShapeIndex">Which brush shape or preset to use, counted from the left.</param>
/// <param name="NudgeX">Moves everything this many screen pixels to the right, for fine tuning the position.</param>
/// <param name="NudgeY">Moves everything this many screen pixels down.</param>
public sealed record PaintOptions(
    ColorMode Mode,
    int BlockSize,
    bool ClearFirst = true,
    double Spacing = 0.1,
    double Opacity = 1,
    int? ShapeIndex = null,
    double NudgeX = 0,
    double NudgeY = 0)
{
    /// <summary>The hard square brush, which paints exactly the square it is sized to. It is the fourth shape on the standard paint screens.</summary>
    public const int SquareShape = 3;
}

public static class PaintScriptBuilder
{
    /// <summary>
    /// Turns a plan into the steps that paint it on a surface. The grid of the plan is laid over the canvas, so the
    /// same plan fits the canvas at any game resolution.
    /// </summary>
    /// <param name="plan">The picture as cells.</param>
    /// <param name="layout">The surface and its controls for the game's resolution.</param>
    /// <param name="options">How to paint.</param>
    public static PaintScript Build(PaintPlan plan, ResolvedLayout layout, PaintOptions options)
        => Build([plan], layout, options);

    /// <summary>
    /// Turns the pictures of an animated sign into the steps that paint them, frame by frame. A surface that does not
    /// animate takes one plan.
    /// </summary>
    /// <param name="plans">The picture of each frame, first to last.</param>
    /// <param name="layout">The surface and its controls for the game's resolution.</param>
    /// <param name="options">How to paint.</param>
    public static PaintScript Build(IReadOnlyList<PaintPlan> plans, ResolvedLayout layout, PaintOptions options)
    {
        if (plans.Count == 0)
            throw new System.ArgumentException("There is nothing to paint.", nameof(plans));

        Surface surface = layout.Surface;
        if (plans.Count > surface.Frames)
            throw new System.ArgumentException($"The surface '{surface.Id}' has {surface.Frames} frame(s), but {plans.Count} pictures were given.", nameof(plans));

        PaintLayout controls = options.Mode == ColorMode.Swatches ? layout.Swatches : layout.Free;
        ScreenRect canvas = layout.Canvas.Offset(options.NudgeX, options.NudgeY);
        FrameControls? frames = surface.IsAnimated ? controls.Frames ?? throw new System.InvalidOperationException("The paint screen has no frame buttons.") : null;
        List<PaintStep> steps = [];

        steps.Add(new ResetCameraStep(controls.ResetCamera.Center));

        if (surface.ScrollOut > 0)
            steps.Add(new ScrollOutStep(surface.ScrollOut));

        ScreenRect freeToggle = layout.Free.ColorModeToggle;
        steps.Add(new EnsureColorModeStep(options.Mode, layout.Swatches.ColorModeToggle.Center, freeToggle.Center,
            new ScreenPoint(freeToggle.X1 + 0.12 * freeToggle.Width, freeToggle.Y1 + 0.5 * freeToggle.Height)));
        steps.Add(new ClickControlStep("Brush", controls.ToolBrush.Center));

        // Whether clearing wipes one frame or all of them is not something to rely on, so every frame is cleared before any is painted.
        if (options.ClearFirst)
        {
            if (frames is null)
            {
                steps.Add(new ClickControlStep("Clear", controls.Clear.Center));
            }
            else
            {
                for (int frame = 0; frame < plans.Count; frame++)
                {
                    steps.Add(SelectFrame(frames, frame));
                    steps.Add(new ClickControlStep("Clear", controls.Clear.Center));
                }
            }
        }

        for (int frame = 0; frame < plans.Count; frame++)
        {
            PaintPlan plan = plans[frame];

            if (frames is not null)
            {
                steps.Add(SelectFrame(frames, frame));
                steps.Add(new ClickControlStep("Brush", controls.ToolBrush.Center));
            }

            int shape = options.ShapeIndex ?? (controls.Size is null ? 0 : PaintOptions.SquareShape);
            steps.Add(new ClickControlStep("Brush shape", controls.BrushShapes[System.Math.Clamp(shape, 0, controls.BrushShapes.Length - 1)].Center));

            // Neon signs have fixed brush presets and no numbers to type.
            if (controls.Size is { } size && controls.Spacing is { } spacing && controls.Opacity is { } opacity)
            {
                steps.Add(new SetBrushValueStep("Size", size.Center, options.BlockSize.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                steps.Add(new SetBrushValueStep("Spacing", spacing.Center, options.Spacing.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));
                steps.Add(new SetBrushValueStep("Opacity", opacity.Center, options.Opacity.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));
            }

            double cellWidth = canvas.Width / plan.Width;
            double cellHeight = canvas.Height / plan.Height;

            ScreenPoint CenterOf(int x, int y) => new(canvas.X1 + (x + 0.5) * cellWidth, canvas.Y1 + (y + 0.5) * cellHeight);

            foreach (ColorPass pass in plan.Strokes.Passes)
            {
                if (options.Mode == ColorMode.Swatches)
                {
                    ColorGrid grid = controls.ColorGrid ?? throw new System.InvalidOperationException("The swatch layout has no color grid.");
                    steps.Add(new SelectSwatchStep(pass.PaletteIndex, pass.Color, grid.CellCenter(pass.PaletteIndex)));
                }
                else
                {
                    FreeColorControls free = controls.FreeColor ?? throw new System.InvalidOperationException("The free color layout has no color controls.");
                    steps.Add(new TypeColorStep(pass.Color, free.ColorHex.Center));
                }

                foreach (Stroke stroke in pass.Strokes)
                    steps.Add(new PaintStrokeStep(stroke, CenterOf(stroke.X, stroke.Y), CenterOf(stroke.EndX, stroke.EndY)));
            }
        }

        return new PaintScript(steps);
    }

    private static SelectFrameStep SelectFrame(FrameControls frames, int index)
    {
        ScreenRect button = frames.Buttons[index];

        // The open frame is the one in a green circle, so a point inside the top of that circle is green when the frame is open.
        return new SelectFrameStep(index, button.Center, new ScreenPoint(button.Center.X, button.Y1 + 0.18 * button.Height));
    }
}
