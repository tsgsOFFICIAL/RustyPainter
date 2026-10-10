using System.Diagnostics;
using RustyPainter.Imaging;

namespace RustyPainter.Tests;

public class StrokePlanTests
{
    private static readonly Rgb[] Palette = [new Rgb(255, 0, 0), new Rgb(0, 255, 0), new Rgb(0, 0, 255)];

    private static PaintPlan Grid(int width, params int[] cells) => new(width, cells.Length / width, Palette, cells);

    /// <summary>Paints the strokes onto a blank grid and returns what ended up where, so a plan can be checked against the picture.</summary>
    private static int[] Replay(StrokePlan plan, out int overlaps)
    {
        int[] canvas = new int[plan.Width * plan.Height];
        Array.Fill(canvas, Quantizer.Unpainted);
        overlaps = 0;

        foreach (ColorPass pass in plan.Passes)
        {
            foreach (Stroke stroke in pass.Strokes)
            {
                for (int k = 0; k < stroke.Length; k++)
                {
                    int x = stroke.Vertical ? stroke.X : stroke.X + k;
                    int y = stroke.Vertical ? stroke.Y + k : stroke.Y;
                    int i = y * plan.Width + x;

                    if (canvas[i] != Quantizer.Unpainted)
                        overlaps++;

                    canvas[i] = pass.PaletteIndex;
                }
            }
        }

        return canvas;
    }

    [Fact]
    public void A_row_of_one_color_is_one_drag()
    {
        StrokePlan strokes = Grid(5, 0, 0, 0, 0, 0).Strokes;

        Stroke stroke = Assert.Single(strokes.Passes[0].Strokes);
        Assert.Equal(new Stroke(0, 0, 5, Vertical: false), stroke);
        Assert.Equal(1, strokes.StrokeCount);
        Assert.Equal(0, strokes.ClickCount);
    }

    [Fact]
    public void A_column_of_one_color_is_one_vertical_drag()
    {
        StrokePlan strokes = Grid(1, 2, 2, 2, 2).Strokes;

        Stroke stroke = Assert.Single(strokes.Passes[0].Strokes);
        Assert.Equal(new Stroke(0, 0, 4, Vertical: true), stroke);
    }

    [Fact]
    public void A_single_cell_is_a_click()
    {
        StrokePlan strokes = Grid(3, 0, 1, 0).Strokes;

        Assert.Equal(3, strokes.StrokeCount);
        Assert.Equal(3, strokes.ClickCount);
    }

    [Fact]
    public void The_longer_direction_wins()
    {
        // A tall bar: going down is one stroke, going across would be five.
        StrokePlan strokes = Grid(2,
            0, 1,
            0, 1,
            0, 1,
            0, 1,
            0, 1).Strokes;

        Assert.Equal(2, strokes.StrokeCount);
        Assert.All(strokes.Passes.SelectMany(pass => pass.Strokes), stroke => Assert.True(stroke.Vertical));
    }

    [Fact]
    public void A_block_prefers_rows_when_it_is_a_tie()
    {
        StrokePlan strokes = Grid(2, 1, 1, 1, 1).Strokes;

        Assert.Equal(2, strokes.StrokeCount);
        Assert.All(strokes.Passes[0].Strokes, stroke => Assert.False(stroke.Vertical));
    }

    [Fact]
    public void A_vertical_stroke_does_not_leave_a_wrongly_long_row_behind()
    {
        // A plus shape: the vertical bar is taken first from the top, so the cross bar must not cover its middle again.
        PaintPlan plan = Grid(3,
            -1, 0, -1,
            0, 0, 0,
            -1, 0, -1);

        StrokePlan strokes = plan.Strokes;
        Replay(strokes, out int overlaps);

        Assert.Equal(0, overlaps);
        Assert.Equal(5, strokes.CellCount);
    }

    [Fact]
    public void Unpainted_cells_get_no_stroke_and_split_runs()
    {
        PaintPlan plan = Grid(5, 0, -1, 0, 0, -1);

        Assert.Equal(2, plan.Strokes.StrokeCount);
        Assert.Equal(3, plan.Strokes.CellCount);
    }

    [Fact]
    public void Every_color_is_painted_in_one_pass_the_most_used_first()
    {
        PaintPlan plan = Grid(4,
            0, 1, 1, 1,
            0, 2, 1, 1);

        Assert.Equal([1, 0, 2], plan.Strokes.Passes.Select(pass => pass.PaletteIndex));
        Assert.Equal(Palette[1], plan.Strokes.Passes[0].Color);
    }

    [Fact]
    public void An_empty_plan_has_no_strokes()
    {
        PaintPlan plan = Grid(3, -1, -1, -1);

        Assert.Empty(plan.Strokes.Passes);
        Assert.Equal(0, plan.StrokeCount);
    }

    [Fact]
    public void Odd_rows_run_backwards_so_the_mouse_does_not_jump()
    {
        // Two separate dots in each of three rows.
        PaintPlan plan = Grid(5,
            0, -1, 0, -1, -1,
            -1, 0, -1, 0, -1,
            0, -1, 0, -1, -1);

        IReadOnlyList<Stroke> order = plan.Strokes.Passes[0].Strokes;

        Assert.Equal([(0, 0), (2, 0), (3, 1), (1, 1), (0, 2), (2, 2)], order.Select(stroke => (stroke.X, stroke.Y)));
    }

    [Fact]
    public void Painting_the_strokes_gives_back_the_picture_exactly()
    {
        Random random = new(7);
        int w = 40, h = 30;
        int[] cells = new int[w * h];

        // Blobs and noise, so there are long runs, short runs and lone cells.
        for (int i = 0; i < cells.Length; i++)
        {
            int x = i % w, y = i / w;
            cells[i] = random.Next(10) == 0 ? Quantizer.Unpainted : (x / 7 + y / 5) % 3;
            if (random.Next(6) == 0)
                cells[i] = random.Next(3);
        }

        PaintPlan plan = new(w, h, Palette, cells);
        int[] painted = Replay(plan.Strokes, out int overlaps);

        Assert.Equal(cells, painted);
        Assert.Equal(0, overlaps);
        Assert.Equal(plan.PaintedCells, plan.Strokes.CellCount);
    }

    [Fact]
    public void A_flat_picture_needs_far_fewer_strokes_than_one_per_cell()
    {
        int[] cells = new int[100 * 100];
        PaintPlan plan = new(100, 100, Palette, cells);

        Assert.Equal(100, plan.Strokes.StrokeCount);
    }

    [Fact]
    public void Stroke_ends_are_worked_out()
    {
        Assert.Equal((6, 2), (new Stroke(2, 2, 5, Vertical: false).EndX, new Stroke(2, 2, 5, Vertical: false).EndY));
        Assert.Equal((2, 6), (new Stroke(2, 2, 5, Vertical: true).EndX, new Stroke(2, 2, 5, Vertical: true).EndY));
    }

    [Fact]
    public void A_big_noisy_canvas_is_planned_quickly()
    {
        // The biggest grid there is: the XXL canvas at the finest brush, with 64 colors dithered across it.
        int w = 1713, h = 857;
        Random random = new(3);
        Rgb[] palette = Enumerable.Range(0, 64).Select(i => new Rgb((byte)i, (byte)(i * 3), (byte)(255 - i))).ToArray();
        int[] cells = new int[w * h];

        for (int i = 0; i < cells.Length; i++)
        {
            int x = i % w, y = i / w;
            int baseColor = (x / 40 + y / 30) % 64;
            cells[i] = random.Next(4) == 0 ? random.Next(64) : baseColor;
        }

        PaintPlan plan = new(w, h, palette, cells);

        Stopwatch clock = Stopwatch.StartNew();
        StrokePlan strokes = plan.Strokes;
        clock.Stop();

        Assert.Equal(plan.PaintedCells, strokes.CellCount);
        Assert.True(clock.ElapsedMilliseconds < 4000, $"Took {clock.ElapsedMilliseconds} ms");
    }
}
