using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.VisualTree;
using System.Globalization;

namespace RustyPainter.Tests;

/// <summary>One thing on a page that looks wrong.</summary>
/// <param name="Kind">What is wrong, in a few words.</param>
/// <param name="Detail">Which control, with its text and place.</param>
public sealed record UiFinding(string Kind, string Detail);

/// <summary>
/// Looks over a drawn window for the things that make a page look broken without anyone having to see it: text that does not
/// fit in the space it was given, controls that a container cuts off, and controls drawn on top of each other.
/// </summary>
public static class UiAudit
{
    private const double Slack = 1.5;

    public static List<UiFinding> Inspect(TopLevel root)
    {
        List<UiFinding> findings = [];
        List<Control> controls = root.GetVisualDescendants().OfType<Control>().Where(IsShown).ToList();

        foreach (Control control in controls)
        {
            if (control is TextBlock text)
                CheckText(text, findings);

            if (control is TextBlock or Button or CheckBox or ToggleSwitch or ComboBox or TextBox or Slider or ProgressBar)
                CheckCut(control, root, findings);
        }

        CheckOverlaps(controls.Where(IsLeaf).ToList(), root, findings);
        return findings
            .GroupBy(finding => (finding.Kind, finding.Detail))
            .Select(group => group.First())
            .ToList();
    }

    private static bool IsShown(Control control)
        => control.IsEffectivelyVisible && control.Bounds.Width > 0 && control.Bounds.Height > 0 && control.Opacity > 0;

    private static bool IsLeaf(Control control)
        => !IsPart(control) && (control is TextBlock { } text && TextOf(text).Trim().Length > 0
            || control is Button or CheckBox or ToggleSwitch or ComboBox or TextBox or Slider);

    private static string TextOf(TextBlock text)
        => text.Inlines is { Count: > 0 } inlines ? string.Concat(inlines.OfType<Run>().Select(run => run.Text)) : text.Text ?? "";

    private static string Describe(Control control, TopLevel root)
    {
        string label = control switch
        {
            TextBlock text => $"\"{Shorten(TextOf(text))}\"",
            ContentControl { Content: string content } => $"\"{Shorten(content)}\"",
            _ => control.Name ?? ""
        };

        Rect rect = RectIn(control, root);
        return $"{control.GetType().Name} {label} at {rect.X:0},{rect.Y:0} size {rect.Width:0}x{rect.Height:0}";
    }

    private static string Shorten(string text)
    {
        text = text.Replace('\n', ' ');
        return text.Length > 42 ? text[..42] + "..." : text;
    }

    /// <summary>Where a control is in the coordinates of one of its ancestors, scaling and all, as the smallest box around it.</summary>
    private static Rect RectIn(Control control, Visual ancestor)
        => control.TransformToVisual(ancestor) is { } matrix ? new Rect(control.Bounds.Size).TransformToAABB(matrix) : default;

    // ---- text that does not fit ----

    private static void CheckText(TextBlock text, List<UiFinding> findings)
    {
        string content = TextOf(text);
        if (content.Trim().Length == 0 || text.Bounds.Width <= 0)
            return;

        FormattedText Format(double? maxWidth)
        {
            FormattedText formatted = new(content, CultureInfo.CurrentCulture, text.FlowDirection,
                new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, null);
            if (maxWidth is { } width)
                formatted.MaxTextWidth = Math.Max(1, width);

            return formatted;
        }

        double room = text.Bounds.Width - text.Padding.Left - text.Padding.Right;

        if (text.TextWrapping == TextWrapping.NoWrap)
        {
            double natural = Format(null).WidthIncludingTrailingWhitespace;
            if (natural > room + Slack)
            {
                string kind = text.TextTrimming == TextTrimming.None ? "text wider than its space" : "text shortened with an ellipsis";
                findings.Add(new UiFinding(kind, $"{Describe(text, TopLevel.GetTopLevel(text)!)} needs {natural:0} but has {room:0}"));
            }

            return;
        }

        if (text.MaxLines > 0)
            return;

        FormattedText wrapped = Format(room);
        double height = wrapped.Height + text.Padding.Top + text.Padding.Bottom;
        if (height > text.Bounds.Height + Slack)
            findings.Add(new UiFinding("wrapped text taller than its space", $"{Describe(text, TopLevel.GetTopLevel(text)!)} needs {height:0} but has {text.Bounds.Height:0}"));
    }

    // ---- controls that a container cuts off ----

    private static bool IsPart(Control control)
        => control is RepeatButton or Thumb or AccessText
            || control.FindAncestorOfType<Slider>() is not null
            || control.FindAncestorOfType<ScrollBar>() is not null;

    private static void CheckCut(Control control, TopLevel root, List<UiFinding> findings)
    {
        if (IsPart(control))
            return;

        bool scrollsVertically = false;

        for (Visual? parent = control.GetVisualParent(); parent is not null; parent = parent.GetVisualParent())
        {
            if (parent is not Control container || container is ScrollViewer)
                continue;

            if (container is ScrollContentPresenter presenter)
            {
                // What a scroll viewer shows by scrolling is not cut off, but only in the directions it is allowed to scroll.
                ScrollViewer? scroll = presenter.FindAncestorOfType<ScrollViewer>();
                bool horizontalScroll = scroll is not null && scroll.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled;
                bool verticalScroll = scroll is not null && scroll.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled;
                CompareWith(control, container, root, findings, checkHorizontal: !horizontalScroll, checkVertical: !verticalScroll && !scrollsVertically);
                scrollsVertically |= verticalScroll;
                continue;
            }

            if (container.ClipToBounds || container is TopLevel)
                CompareWith(control, container, root, findings, checkHorizontal: true, checkVertical: !scrollsVertically);
        }
    }

    private static void CompareWith(Control control, Control container, TopLevel root, List<UiFinding> findings, bool checkHorizontal, bool checkVertical)
    {
        Rect inside = RectIn(control, root);
        Rect frame = RectIn(container, root);

        bool cutSide = checkHorizontal && (inside.Right > frame.Right + Slack || inside.X < frame.X - Slack);
        bool cutEnd = checkVertical && (inside.Bottom > frame.Bottom + Slack || inside.Y < frame.Y - Slack);
        if (!cutSide && !cutEnd)
            return;

        string where = container is TopLevel ? "the window" : $"{container.GetType().Name}{(container.Name is { } name ? " " + name : "")}";
        findings.Add(new UiFinding("cut off by its container", $"{Describe(control, root)} sticks out of {where} ({frame.Width:0}x{frame.Height:0})"));
    }

    /// <summary>The part of a control that can be seen: what is left of it after every container that clips has had its say.</summary>
    private static Rect VisibleRect(Control control, TopLevel root)
    {
        Rect visible = RectIn(control, root);

        for (Visual? parent = control.GetVisualParent(); parent is not null && visible.Width > 0; parent = parent.GetVisualParent())
        {
            if (parent is Control container && (container.ClipToBounds || container is TopLevel))
                visible = visible.Intersect(RectIn(container, root).Width > 0 ? RectIn(container, root) : new Rect(0, 0, root.Bounds.Width, root.Bounds.Height));
        }

        return visible;
    }

    // ---- controls on top of each other ----

    private static void CheckOverlaps(List<Control> leaves, TopLevel root, List<UiFinding> findings)
    {
        List<(Control Control, Rect Rect)> placed = leaves.Select(control => (control, VisibleRect(control, root)))
            .Where(item => item.Item2.Width > 2 && item.Item2.Height > 2)
            .ToList();

        for (int i = 0; i < placed.Count; i++)
        {
            for (int j = i + 1; j < placed.Count; j++)
            {
                Rect overlap = placed[i].Rect.Intersect(placed[j].Rect);
                if (overlap.Width <= 2 || overlap.Height <= 2)
                    continue;

                if (placed[i].Control.IsVisualAncestorOf(placed[j].Control) || placed[j].Control.IsVisualAncestorOf(placed[i].Control))
                    continue;

                // Controls inside a button are the button's own, and two texts that are inside one button never count.
                if (placed[i].Control.FindAncestorOfType<Button>() is { } button && button == placed[j].Control.FindAncestorOfType<Button>())
                    continue;

                findings.Add(new UiFinding("controls on top of each other",
                    $"{Describe(placed[i].Control, root)} and {Describe(placed[j].Control, root)} overlap by {overlap.Width:0}x{overlap.Height:0}"));
            }
        }
    }
}
