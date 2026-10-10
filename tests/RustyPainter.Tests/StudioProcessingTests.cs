using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using RustyPainter.Imaging;
using RustyPainter.Service;
using RustyPainter.ViewModels;

namespace RustyPainter.Tests;

/// <summary>The whole Studio flow, with real pictures decoded by Avalonia.</summary>
public class StudioProcessingTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"rustypainter-process-{Guid.NewGuid():N}");

    public StudioProcessingTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private StudioViewModel Loaded(PixelBuffer pixels, string name = "test.png")
    {
        string path = Path.Combine(_folder, name);
        TestImages.SavePng(pixels, path);

        StudioViewModel studio = new(new SurfaceService());
        Assert.True(studio.TryLoad(path), studio.ErrorText);
        return studio;
    }

    private static Rgb CellColor(PaintPlan plan, int x, int y) => plan.Palette[plan.Cells[y * plan.Width + x]];

    [AvaloniaFact]
    public void Nothing_is_prepared_until_a_picture_is_loaded()
    {
        StudioViewModel studio = new(new SurfaceService());

        Assert.Null(studio.ProcessNow());
        Assert.Null(studio.Plan);
        Assert.Null(studio.Preview);
        Assert.False(studio.HasPlan);
    }

    [AvaloniaFact]
    public void The_grid_follows_the_surface_and_the_brush_size()
    {
        StudioViewModel studio = Loaded(TestImages.Rainbow());

        // The small sign's texture is estimated at 371 x 197 units; the default brush is 4 units.
        PaintPlan plan = studio.ProcessNow()!;
        Assert.Equal((93, 50), (plan.Width, plan.Height));
        Assert.Equal("93 x 50 cells", studio.GridText);

        studio.QualityChoices.Select(8);
        plan = studio.ProcessNow()!;
        Assert.Equal((47, 25), (plan.Width, plan.Height));

        // The medium sign's texture is known to be 512 x 256.
        studio.SelectedSurface = studio.AllSurfaces.Single(option => option.Surface.Id == "sign-medium");
        studio.QualityChoices.Select(4);
        plan = studio.ProcessNow()!;
        Assert.Equal((128, 64), (plan.Width, plan.Height));
    }

    [AvaloniaFact]
    public void Says_when_the_texture_size_is_only_an_estimate()
    {
        StudioViewModel studio = Loaded(TestImages.Rainbow());

        Assert.True(studio.IsTextureEstimated);

        studio.SelectedSurface = studio.AllSurfaces.Single(option => option.Surface.Id == "sign-medium");
        Assert.False(studio.IsTextureEstimated);
    }

    [AvaloniaFact]
    public void Swatch_mode_uses_the_surfaces_own_colors()
    {
        StudioViewModel studio = Loaded(TestImages.Rainbow());
        Assert.Equal(64, studio.ProcessNow()!.Palette.Count);

        studio.SelectedSurface = studio.AllSurfaces.Single(option => option.Surface.Id == "neon-large");
        Assert.Equal(8, studio.ProcessNow()!.Palette.Count);
    }

    [AvaloniaFact]
    public void Free_mode_reduces_to_the_chosen_number_of_colors()
    {
        StudioViewModel studio = Loaded(TestImages.Rainbow());
        studio.ColorModeChoices.Select(ColorMode.Free);
        studio.MaxColors = 3;

        PaintPlan plan = studio.ProcessNow()!;

        Assert.True(studio.IsFreeMode);
        Assert.InRange(plan.Palette.Count, 2, 3);
        Assert.InRange(plan.ColorsUsed, 2, 3);
    }

    [AvaloniaFact]
    public void The_color_limit_is_kept_between_2_and_256()
    {
        StudioViewModel studio = Loaded(TestImages.Rainbow());

        studio.MaxColors = 500;
        Assert.Equal(256, studio.MaxColors);

        studio.MaxColors = 0;
        Assert.Equal(2, studio.MaxColors);
    }

    [AvaloniaFact]
    public void Flipping_swaps_the_sides_of_the_picture()
    {
        StudioViewModel studio = Loaded(TestImages.RedAndGreen());
        studio.ColorModeChoices.Select(ColorMode.Free);

        PaintPlan before = studio.ProcessNow()!;
        Rgb leftBefore = CellColor(before, 0, before.Height / 2);
        Assert.True(leftBefore.R > leftBefore.G, $"left was {leftBefore.Hex}");

        studio.FlipHorizontalCommand.Execute(null);
        PaintPlan after = studio.ProcessNow()!;
        Rgb leftAfter = CellColor(after, 0, after.Height / 2);
        Assert.True(leftAfter.G > leftAfter.R, $"left is now {leftAfter.Hex}");
    }

    [AvaloniaFact]
    public void Rotating_four_times_comes_back_to_the_start()
    {
        StudioViewModel studio = Loaded(TestImages.RedAndGreen(4, 2));
        studio.ColorModeChoices.Select(ColorMode.Free);
        int[] start = studio.ProcessNow()!.Cells;

        for (int i = 0; i < 4; i++)
            studio.RotateCommand.Execute(null);

        Assert.Equal(start, studio.ProcessNow()!.Cells);
    }

    [AvaloniaFact]
    public void Fit_leaves_part_of_the_canvas_unpainted()
    {
        StudioViewModel studio = Loaded(TestImages.RedAndGreen(2, 2));
        PaintPlan filled = studio.ProcessNow()!;
        Assert.Equal(filled.Width * filled.Height, filled.PaintedCells);

        studio.PlacementChoices.Select(FitMode.Fit);
        PaintPlan fitted = studio.ProcessNow()!;

        Assert.True(fitted.PaintedCells < fitted.Width * fitted.Height);
    }

    [AvaloniaFact]
    public void Placement_values_are_kept_in_range()
    {
        StudioViewModel studio = Loaded(TestImages.RedAndGreen());

        studio.Zoom = 50;
        Assert.Equal(4, studio.Zoom);
        studio.Zoom = 0;
        Assert.Equal(0.25, studio.Zoom);
        studio.OffsetX = 500;
        Assert.Equal(100, studio.OffsetX);
        studio.OffsetY = -500;
        Assert.Equal(-100, studio.OffsetY);
    }

    [AvaloniaFact]
    public void Resetting_the_placement_puts_everything_back()
    {
        StudioViewModel studio = Loaded(TestImages.RedAndGreen());
        studio.Zoom = 2;
        studio.OffsetX = 30;
        studio.PlacementChoices.Select(FitMode.Stretch);
        studio.RotateCommand.Execute(null);

        studio.ResetPlacementCommand.Execute(null);

        Assert.Equal(1, studio.Zoom);
        Assert.Equal(0, studio.OffsetX);
        Assert.Equal(FitMode.Fill, studio.PlacementChoices.Selected);
    }

    [AvaloniaFact]
    public void Resetting_the_adjustments_puts_everything_back()
    {
        StudioViewModel studio = Loaded(TestImages.RedAndGreen());
        studio.Brightness = 40;
        studio.Contrast = -20;
        studio.Saturation = 70;
        studio.FilterChoices.Select(ImageFilter.Sepia);

        studio.ResetAdjustmentsCommand.Execute(null);

        Assert.Equal(0, studio.Brightness);
        Assert.Equal(0, studio.Contrast);
        Assert.Equal(0, studio.Saturation);
        Assert.Equal(ImageFilter.None, studio.FilterChoices.Selected);
    }

    [AvaloniaFact]
    public void A_filter_changes_the_result()
    {
        StudioViewModel studio = Loaded(TestImages.RedAndGreen());
        studio.ColorModeChoices.Select(ColorMode.Free);
        PaintPlan normal = studio.ProcessNow()!;
        Rgb red = CellColor(normal, 0, normal.Height / 2);

        studio.FilterChoices.Select(ImageFilter.BlackAndWhite);
        PaintPlan grey = studio.ProcessNow()!;
        Rgb afterwards = CellColor(grey, 0, grey.Height / 2);

        Assert.True(red.R > red.G + 100);
        Assert.InRange(Math.Abs(afterwards.R - afterwards.G), 0, 2);
    }

    [AvaloniaFact]
    public void The_result_and_the_original_can_be_switched()
    {
        StudioViewModel studio = Loaded(TestImages.RedAndGreen());
        studio.ProcessNow();

        Assert.True(studio.ShowResult);
        Assert.False(studio.ShowOriginal);

        studio.PreviewChoices.Select(PreviewMode.Original);

        Assert.False(studio.ShowResult);
        Assert.True(studio.ShowOriginal);
    }

    [AvaloniaFact]
    public void Moving_and_zooming_stay_within_their_limits()
    {
        StudioViewModel studio = Loaded(TestImages.RedAndGreen());

        studio.MoveBy(0.25, -0.1);
        Assert.Equal(25, studio.OffsetX);
        Assert.Equal(-10, studio.OffsetY);

        studio.MoveBy(5, -5);
        Assert.Equal(100, studio.OffsetX);
        Assert.Equal(-100, studio.OffsetY);

        studio.ZoomBy(100);
        Assert.Equal(4, studio.Zoom);

        studio.ResetView();
        Assert.Equal((1d, 0d, 0d), (studio.Zoom, studio.OffsetX, studio.OffsetY));
    }

    [AvaloniaFact]
    public void Resetting_the_view_keeps_the_rotation()
    {
        StudioViewModel studio = Loaded(TestImages.RedAndGreen(4, 2));
        studio.ColorModeChoices.Select(ColorMode.Free);
        studio.RotateCommand.Execute(null);
        PaintPlan rotated = studio.ProcessNow()!;

        studio.MoveBy(0.3, 0);
        studio.ResetView();

        Assert.Equal(rotated.Cells, studio.ProcessNow()!.Cells);
    }

    [AvaloniaFact]
    public void The_split_view_needs_the_placed_picture_and_the_result()
    {
        StudioViewModel studio = Loaded(TestImages.Rainbow());
        PaintPlan plan = studio.ProcessNow()!;

        Assert.NotNull(plan.Reference);
        Assert.Equal((plan.Width, plan.Height), (plan.Reference!.Width, plan.Reference.Height));
        Assert.False(studio.ShowSplit);

        studio.PreviewChoices.Select(PreviewMode.Split);

        Assert.True(studio.ShowSplit);
        Assert.True(studio.ShowResult);
        Assert.False(studio.ShowOriginal);
        Assert.NotNull(studio.Reference);

        studio.ClearImageCommand.Execute(null);
        Assert.False(studio.ShowSplit);
        Assert.Null(studio.Reference);
    }

    [AvaloniaFact]
    public void Lists_the_colors_the_picture_uses()
    {
        StudioViewModel studio = Loaded(TestImages.RedAndGreen());
        studio.ColorModeChoices.Select(ColorMode.Free);
        PaintPlan plan = studio.ProcessNow()!;

        Assert.Equal(plan.ColorsUsed, studio.UsedColors.Count);
        Assert.Equal($"{plan.ColorsUsed} colors", studio.ColorsText);
        Assert.Equal($"{plan.StrokeCount:N0} strokes", studio.StrokesText);
        Assert.True(plan.StrokeCount >= plan.Height);
    }

    [AvaloniaFact]
    public void Removing_the_picture_clears_the_result()
    {
        StudioViewModel studio = Loaded(TestImages.RedAndGreen());
        studio.ProcessNow();
        Assert.NotNull(studio.Preview);

        studio.ClearImageCommand.Execute(null);

        Assert.Null(studio.Plan);
        Assert.Null(studio.Preview);
        Assert.False(studio.ShowResult);
        Assert.Equal("", studio.StrokesText);
    }

    [AvaloniaFact]
    public async Task The_page_updates_on_its_own_after_a_picture_is_loaded()
    {
        StudioViewModel studio = Loaded(TestImages.Rainbow());

        for (int i = 0; i < 100 && studio.Plan is null; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(30);
        }

        Assert.NotNull(studio.Plan);
        Assert.NotNull(studio.Preview);
        Assert.False(studio.IsBusy);
    }

    [AvaloniaFact]
    public async Task The_page_shows_it_is_busy_for_every_update()
    {
        StudioViewModel studio = Loaded(TestImages.Rainbow());
        Assert.True(studio.IsBusy);

        for (int i = 0; i < 100 && studio.IsBusy; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(30);
        }

        Assert.False(studio.IsBusy);

        studio.Zoom = 2;
        Assert.True(studio.IsBusy);

        for (int i = 0; i < 100 && studio.IsBusy; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(30);
        }

        Assert.False(studio.IsBusy);
    }

    [AvaloniaFact]
    public void Free_mode_can_use_up_to_256_colors()
    {
        StudioViewModel studio = Loaded(TestImages.Rainbow(256, 64));
        studio.ColorModeChoices.Select(ColorMode.Free);
        studio.QualityChoices.Select(1);
        studio.MaxColors = 256;

        PaintPlan plan = studio.ProcessNow()!;

        Assert.InRange(plan.Palette.Count, 100, 256);
    }

    [AvaloniaFact]
    public async Task Changing_a_setting_updates_the_result_on_its_own()
    {
        StudioViewModel studio = Loaded(TestImages.Rainbow());
        PaintPlan first = studio.ProcessNow()!;

        studio.QualityChoices.Select(16);

        for (int i = 0; i < 100 && ReferenceEquals(studio.Plan, first); i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(30);
        }

        Assert.NotSame(first, studio.Plan);
        Assert.Equal(24, studio.Plan!.Width);
    }
}

public class BitmapPixelsTests
{
    [AvaloniaFact]
    public void Round_trips_opaque_and_half_transparent_pixels()
    {
        PixelBuffer source = new(2, 1);
        source.Set(0, 0, 200, 100, 50);
        source.Set(1, 0, 200, 100, 50, 128);

        using var bitmap = BitmapPixels.ToBitmap(source);
        PixelBuffer back = BitmapPixels.FromBitmap(bitmap);

        Assert.Equal((200, 100, 50, 255), (back.Data[0], back.Data[1], back.Data[2], back.Data[3]));

        // Premultiplying loses a little precision on the half transparent pixel, never more than a couple of steps.
        Assert.InRange(back.Data[4], 198, 202);
        Assert.InRange(back.Data[5], 98, 102);
        Assert.InRange(back.Data[6], 48, 52);
        Assert.Equal(128, back.Data[7]);
    }

    [AvaloniaFact]
    public void A_fully_transparent_pixel_stays_transparent()
    {
        PixelBuffer source = new(1, 1);
        source.Set(0, 0, 10, 20, 30, 0);

        using var bitmap = BitmapPixels.ToBitmap(source);
        PixelBuffer back = BitmapPixels.FromBitmap(bitmap);

        Assert.Equal(0, back.Data[3]);
    }
}
