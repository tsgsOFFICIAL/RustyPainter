using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using RustyPainter.Controls;
using RustyPainter.Imaging;
using RustyPainter.Interface;
using RustyPainter.Painting;
using RustyPainter.Service;
using RustyPainter.ViewModels;

namespace RustyPainter.Tests;

public class PaintingSettingsTests
{
    private static SettingsViewModel Page(AppSettingsService settings, string folder = "")
        => new(settings, new UpdateService(), new DialogService(), folder);

    [Fact]
    public void The_defaults_are_a_middle_speed_and_everything_over_the_game_switched_on()
    {
        AppSettings defaults = new();

        Assert.Equal(60, defaults.PaintSpeed);
        Assert.True(defaults.ShowHologram);
        Assert.Equal(55, defaults.HologramStrength);
        Assert.True(defaults.ShowStatusBox);
    }

    [Fact]
    public void Every_change_is_kept_in_the_settings_and_saved()
    {
        AppSettingsService settings = TestSettings.Create();
        SettingsViewModel page = Page(settings);

        page.PaintSpeed = 25;
        page.ShowHologram = false;
        page.HologramStrength = 80;
        page.ShowStatusBox = false;

        Assert.Equal((25, false, 80, false), (settings.Current.PaintSpeed, settings.Current.ShowHologram, settings.Current.HologramStrength, settings.Current.ShowStatusBox));
        Assert.Equal("25%", page.PaintSpeedText);
        Assert.Equal("80%", page.HologramStrengthText);

        // Another page made from the same settings sees them.
        Assert.Equal(25, Page(settings).PaintSpeed);
    }

    [Fact]
    public void The_values_stay_within_their_limits()
    {
        SettingsViewModel page = Page(TestSettings.Create());

        page.PaintSpeed = 900;
        Assert.Equal(100, page.PaintSpeed);
        page.PaintSpeed = -5;
        Assert.Equal(1, page.PaintSpeed);

        page.HologramStrength = 0;
        Assert.Equal(10, page.HologramStrength);
        page.HologramStrength = 500;
        Assert.Equal(90, page.HologramStrength);
    }

    [Fact]
    public void Resetting_puts_back_the_speed_the_picture_the_box_and_the_keys()
    {
        AppSettingsService settings = TestSettings.Create();
        SettingsViewModel page = Page(settings);
        page.PaintSpeed = 10;
        page.ShowHologram = false;
        page.HologramStrength = 20;
        page.ShowStatusBox = false;
        page.CaptureStartPauseCommand.Execute(null);
        page.CaptureKey(0x50, 0);

        page.ResetPaintingCommand.Execute(null);

        Assert.Equal((60, true, 55, true), (settings.Current.PaintSpeed, settings.Current.ShowHologram, settings.Current.HologramStrength, settings.Current.ShowStatusBox));
        Assert.Equal(60, page.PaintSpeed);
        Assert.True(page.ShowHologram);
        Assert.Equal("55%", page.HologramStrengthText);
        Assert.Equal("Space", page.StartPauseKeyText);
        Assert.Equal("Esc", page.AbortKeyText);
    }

    [Fact]
    public void The_library_folder_can_only_be_opened_when_there_is_one()
    {
        Assert.False(Page(TestSettings.Create()).OpenLibraryFolderCommand.CanExecute(null));
        Assert.True(Page(TestSettings.Create(), "C:\\somewhere").OpenLibraryFolderCommand.CanExecute(null));
    }

    private static PaintRequest Request()
    {
        SurfaceService surfaces = new();
        Surface surface = surfaces.Catalog.Surfaces.Single(s => s.Id == "sign-small");
        IReadOnlyList<Rgb> swatches = surfaces.Catalog.Layouts[surface.Layout].Swatches.ColorGrid!.Colors.Select(Rgb.Parse).ToList();
        int[] cells = new int[8 * 4];
        Array.Fill(cells, 1);
        return new PaintRequest(new PaintPlan(8, 4, swatches, cells), surface, ColorMode.Swatches, 8, "cat.png");
    }

    [AvaloniaFact]
    public void The_get_ready_page_starts_from_the_saved_picture_settings()
    {
        AppSettingsService settings = TestSettings.Create();
        settings.Current.ShowHologram = false;
        settings.Current.HologramStrength = 33;
        FakeHologram hologram = new();

        GetReadyViewModel page = new(Request(), new FakeGame(FakeGame.Rust()), new SurfaceService(), settings, hologram,
            new Hotkey(0, 0x20), new Hotkey(0, 0x1B));

        Assert.False(page.ShowHologram);
        Assert.Equal(33, page.HologramOpacity);
        Assert.False(hologram.IsShown);

        // Turning it on for this painting does not change what is saved.
        page.ShowHologram = true;
        Assert.True(hologram.IsShown);
        Assert.Equal(0.33, hologram.Opacity, 3);
        Assert.False(settings.Current.ShowHologram);
    }

    [AvaloniaFact]
    public void A_strength_in_the_settings_that_is_out_of_range_is_kept_to_the_limits()
    {
        AppSettingsService settings = TestSettings.Create();
        settings.Current.HologramStrength = 4000;

        GetReadyViewModel page = new(Request(), new FakeGame(FakeGame.Rust()), new SurfaceService(), settings, new FakeHologram(),
            new Hotkey(0, 0x20), new Hotkey(0, 0x1B));

        Assert.Equal(90, page.HologramOpacity);
    }

    [AvaloniaFact]
    public async Task The_progress_box_can_be_switched_off()
    {
        AppSettingsService settings = TestSettings.Create();
        settings.Current.ShowStatusBox = false;
        FakeOverlay overlay = new();
        FakeHotkeys hotkeys = new();
        MainWindowViewModel shell = new(new UpdateService(), new DialogService(), settings, new SurfaceService(), new FakeGame(FakeGame.Rust()),
            hotkeys, new FakeEngines(new FakeMachine()), new FakeHologram(), overlay, TestLibrary.Create());

        shell.Sidebar.NavigateTo(SidebarPage.Studio);
        StudioViewModel studio = Assert.IsType<StudioViewModel>(shell.CurrentPage);
        string path = Path.Combine(Path.GetTempPath(), $"rp-box-{Guid.NewGuid():N}.png");
        TestImages.SavePng(TestImages.Rainbow(), path);
        Assert.True(studio.TryLoad(path));
        File.Delete(path);
        studio.ProcessNow();
        studio.PaintCommand.Execute(null);

        hotkeys.Press(HotkeyAction.StartPause);
        for (int i = 0; i < 300 && shell.CurrentPage is not PaintingViewModel { IsOver: true }; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(15);
        }

        Assert.IsType<PaintingViewModel>(shell.CurrentPage);
        Assert.Equal(0, overlay.Shows);
    }

    [Fact]
    public void The_library_knows_its_folder()
    {
        string root = Path.Combine(Path.GetTempPath(), $"rp-root-{Guid.NewGuid():N}");

        Assert.Equal(root, new RustyPainter.Library.ProjectStore(root).Root);
        Assert.EndsWith(Path.Combine("RustyPainter", "Library"), RustyPainter.Library.ProjectStore.DefaultRoot);
    }
}
