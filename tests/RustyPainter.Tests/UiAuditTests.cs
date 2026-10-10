using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using RustyPainter.Controls;
using RustyPainter.Imaging;
using RustyPainter.Interface;
using RustyPainter.Library;
using RustyPainter.Localization;
using RustyPainter.Painting;
using RustyPainter.Service;
using RustyPainter.ViewModels;

namespace RustyPainter.Tests;

/// <summary>
/// Draws the real window at several sizes, in every language and both looks, with every page in its different states, and
/// looks for text that does not fit, controls that are cut off and controls that sit on top of each other. Set
/// RUSTYPAINTER_AUDIT=full for the large set of sizes, RUSTYPAINTER_AUDIT_OUT to a file for the whole list of findings, and
/// RUSTYPAINTER_SHOTS to a folder to keep a picture of each page that has one.
/// </summary>
[Collection("Language")]
public class UiAuditTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"rustypainter-audit-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static readonly (int Width, int Height)[] QuickSizes = [(1240, 640)];
    private static readonly (int Width, int Height)[] FullSizes = [(1240, 640), (1280, 800), (1366, 768), (1600, 900), (1920, 1080), (2560, 1440)];

    public static IEnumerable<object[]> Cases()
    {
        (int, int)[] sizes = Environment.GetEnvironmentVariable("RUSTYPAINTER_AUDIT") == "full" ? FullSizes : QuickSizes;
        foreach (AppLanguage language in Enum.GetValues<AppLanguage>())
            foreach (string variant in new[] { "Dark", "Light" })
                foreach ((int width, int height) in sizes)
                    yield return [language, variant, width, height];
    }

    private static void SetPage(MainWindowViewModel shell, RustyPainter.ViewModels.Mvvm.ViewModelBase page)
        => typeof(MainWindowViewModel).GetProperty(nameof(MainWindowViewModel.CurrentPage))!.SetValue(shell, page);

    private static PixelBuffer Picture(int width, int height)
    {
        PixelBuffer pixels = new(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                pixels.Data[i] = (byte)(x * 255 / Math.Max(1, width - 1));
                pixels.Data[i + 1] = (byte)(y * 255 / Math.Max(1, height - 1));
                pixels.Data[i + 2] = (byte)(128 + 100 * Math.Sin(x * 0.4));
                pixels.Data[i + 3] = 255;
            }
        }

        return pixels;
    }

    private static string Save(PixelBuffer pixels, string name)
    {
        string path = Path.Combine(Path.GetTempPath(), $"rp-audit-{Guid.NewGuid():N}-{name}");
        TestImages.SavePng(pixels, path);
        return path;
    }

    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public void Every_page_fits_its_window(AppLanguage language, string variant, int width, int height)
    {
        LocalizationService localization = new();
        Loc.Install(localization);
        localization.SetLanguage(language, save: false);

        try
        {
            Audit(language, variant, width, height);
        }
        finally
        {
            Loc.Install(new LocalizationService());
        }
    }

    private void Audit(AppLanguage language, string variant, int width, int height)
    {
        AppSettingsService settings = TestSettings.Create();
        settings.Initialize();
        settings.Current.CheckForUpdatesOnStartup = false;

        ProjectStore store = new(_root);
        FakeGame game = new(FakeGame.Rust());
        FakeHotkeys hotkeys = new();
        FakeMachine machine = new();
        MainWindowViewModel shell = new(new UpdateService(), new DialogService(), settings, new SurfaceService(), game, hotkeys,
            new FakeEngines(machine), new FakeHologram(), new FakeOverlay(), store);

        // Some saved projects with long names, so the Library has cards to draw.
        SeedLibrary(store);

        MainWindow window = new(shell) { Width = width, Height = height, RequestedThemeVariant = variant == "Light" ? ThemeVariant.Light : ThemeVariant.Dark };

        // Showing the window must not start the update check or touch the registry, as it does for a user.
        EventInfo opened = typeof(Window).GetEvent(nameof(Window.Opened))!;
        MethodInfo handler = typeof(MainWindow).GetMethod("OnMainWindowOpened", BindingFlags.NonPublic | BindingFlags.Instance)!;
        opened.RemoveEventHandler(window, Delegate.CreateDelegate(opened.EventHandlerType!, window, handler));

        window.Show();
        Dispatcher.UIThread.RunJobs();

        List<string> report = [];
        string tag = $"{language}-{variant}-{width}x{height}";

        void Look(string state)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            List<UiFinding> findings = UiAudit.Inspect(window);
            foreach (UiFinding finding in findings)
                report.Add($"{tag} | {state} | {finding.Kind} | {finding.Detail}");

            if (Environment.GetEnvironmentVariable("RUSTYPAINTER_SHOTS") is { Length: > 0 } folder
                && (findings.Count > 0 || Environment.GetEnvironmentVariable("RUSTYPAINTER_AUDIT_SHOTS") == "all"))
            {
                window.CaptureRenderedFrame()?.Save(Path.Combine(folder, $"audit-{tag}-{state}.png"));
            }
        }

        // ---- Home and Settings ----
        shell.Sidebar.NavigateTo(SidebarPage.Home);
        Look("home");

        shell.Sidebar.NavigateTo(SidebarPage.Settings);
        Look("settings");

        // ---- Library ----
        shell.Sidebar.NavigateTo(SidebarPage.Library);
        Look("library");

        LibraryViewModel library = Assert.IsType<LibraryViewModel>(shell.CurrentPage);
        if (library.Cards.Count > 0)
        {
            library.Cards[0].IsRenaming = true;
            Look("library-renaming");
            library.Cards[0].IsRenaming = false;
            library.Cards[0].IsConfirmingDelete = true;
            Look("library-delete-question");
            library.Cards[0].IsConfirmingDelete = false;
        }

        library.Search = "no project has this name";
        Look("library-nothing-found");
        library.Search = "";

        // ---- Studio ----
        shell.Sidebar.NavigateTo(SidebarPage.Studio);
        StudioViewModel studio = Assert.IsType<StudioViewModel>(shell.CurrentPage);
        studio.ReprocessDelay = TimeSpan.Zero;
        Look("studio-empty");

        string wide = Save(Picture(160, 90), "a-photo-with-a-rather-long-name-for-the-sidebar.png");
        Assert.True(studio.TryLoad(wide));
        studio.ProcessNow();
        File.Delete(wide);
        Look("studio-picture");

        studio.PreviewChoices.Choices[2].SelectCommand.Execute(null);
        Look("studio-split");
        studio.PreviewChoices.Choices[0].SelectCommand.Execute(null);

        studio.ColorModeChoices.Choices[1].SelectCommand.Execute(null);
        studio.ProcessNow();
        Look("studio-any-color");
        studio.ColorModeChoices.Choices[0].SelectCommand.Execute(null);

        studio.FillBackground = true;
        studio.MaxColors = 256;
        studio.ProcessNow();
        Look("studio-background-and-colors");
        studio.FillBackground = false;
        studio.MaxColors = 16;

        studio.SelectedSurface = studio.AllSurfaces.Single(option => option.Surface.Id == "canvas-xxl");
        studio.ProcessNow();
        Look("studio-scroll-out-hint");

        studio.SelectedSurface = studio.AllSurfaces.Single(option => option.Surface.Id == "neon-large");
        studio.ProcessNow();
        Look("studio-neon");

        studio.SelectedSurface = studio.AllSurfaces.Single(option => option.Surface.Id == "animated-neon-large");
        studio.ProcessNow();
        for (int i = 0; i < 4; i++)
            studio.AddFrame();

        studio.ProcessNow();
        Look("studio-animated-five-frames");

        studio.SelectedSurface = studio.AllSurfaces.Single(option => option.Surface.Id == "sign-small");
        studio.SourceChoices.Select(SourceKind.Text);
        studio.Text = "Hello Rust";
        studio.ProcessNow();
        Look("studio-text");

        studio.SourceChoices.Select(SourceKind.Link);
        studio.LinkText = "https://example.com/a/very/long/address/to/a/picture/that/goes/on/and/on.png";
        Look("studio-link");
        studio.SourceChoices.Select(SourceKind.Picture);

        studio.ReportNothingToLoad();
        Look("studio-error");

        // ---- Get ready and painting ----
        studio.SelectedSurface = studio.AllSurfaces.Single(option => option.Surface.Id == "animated-neon-large");
        studio.ProcessNow();
        studio.PaintCommand.Execute(null);
        for (int i = 0; i < 200 && shell.CurrentPage is not GetReadyViewModel; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }

        if (shell.CurrentPage is GetReadyViewModel ready)
        {
            Look("get-ready-animated");

            PaintSession session = new(ready.Request, new PaintOptions(ready.Request.Mode, 1), PaintTiming.FromSpeed(60));
            foreach ((string state, PaintState paintState, string detail, double done) in new[]
            {
                ("painting-running", PaintState.Running, "", 0.42),
                ("painting-paused", PaintState.Paused, "The mouse was moved and the painter waits until you are back in the game.", 0.42),
                ("painting-failed", PaintState.Failed, "The game did not switch color mode. Switch it by hand and try again.", 0.42),
                ("painting-finished", PaintState.Finished, "", 1.0)
            })
            {
                using PaintingViewModel painting = new(session, game, new SurfaceService(), new FakeEngines(machine), HotkeyDefaults.StartPause, HotkeyDefaults.Abort);
                typeof(PaintingViewModel).GetProperty("State", BindingFlags.Instance | BindingFlags.Public)!.SetValue(painting, paintState);
                typeof(PaintingViewModel).GetProperty("Detail", BindingFlags.Instance | BindingFlags.Public)!.SetValue(painting, detail);
                int total = Math.Max(1, ready.Request.StrokeCount);
                typeof(PaintingViewModel).GetMethod("OnProgress", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(painting, [new PaintProgress((int)(done * 100), 100, (int)(done * total), total)]);

                SetPage(shell, painting);
                Look(state);
            }

            ready.Dispose();
        }

        window.Close();

        string? output = Environment.GetEnvironmentVariable("RUSTYPAINTER_AUDIT_OUT");
        if (output is { Length: > 0 })
            File.AppendAllLines(output, report);

        // A long name that ends in an ellipsis is how a name that is too long is meant to look, so those are listed but do not fail.
        List<string> wrong = report.Where(line => !line.Contains("text shortened with an ellipsis")).ToList();
        Assert.True(wrong.Count == 0, $"{wrong.Count} things look wrong at {tag}:{Environment.NewLine}{string.Join(Environment.NewLine, wrong.Take(25))}");
    }

    public static IEnumerable<object[]> DialogCases()
    {
        foreach (AppLanguage language in Enum.GetValues<AppLanguage>())
            foreach (string variant in new[] { "Dark", "Light" })
                yield return [language, variant];
    }

    [AvaloniaTheory]
    [MemberData(nameof(DialogCases))]
    public void The_update_dialog_fits_its_window(AppLanguage language, string variant)
    {
        LocalizationService localization = new();
        Loc.Install(localization);
        localization.SetLanguage(language, save: false);

        try
        {
            string notes = "## 0.9.0\n\n### Painting\n\n- A very long line about what changed in this version that goes on for long enough to need to wrap in the small window of the dialog.\n- Short.\n\n### Library\n\n- Another change with `code` and **bold** words in it.\n- " + string.Join(" ", Enumerable.Repeat("More words to make the notes taller than the window.", 12));
            UpdateAvailableViewModel viewModel = new(new UpdateService(), new UpdateInfo("0.9.0", "https://example.com", "RustyPainter-0.9.0.zip"), notes);
            RustyPainter.Windows.UpdateAvailableWindow window = new(viewModel)
            {
                Width = 420,
                Height = 360,
                RequestedThemeVariant = variant == "Light" ? ThemeVariant.Light : ThemeVariant.Dark
            };

            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            List<string> wrong = UiAudit.Inspect(window).Where(finding => finding.Kind != "text shortened with an ellipsis")
                .Select(finding => $"{finding.Kind}: {finding.Detail}").ToList();

            if (Environment.GetEnvironmentVariable("RUSTYPAINTER_SHOTS") is { Length: > 0 } folder)
                window.CaptureRenderedFrame()?.Save(Path.Combine(folder, $"audit-update-{language}-{variant}.png"));

            window.Close();
            Assert.True(wrong.Count == 0, $"The update dialog has {wrong.Count} problems ({language}, {variant}):{Environment.NewLine}{string.Join(Environment.NewLine, wrong.Take(15))}");
        }
        finally
        {
            Loc.Install(new LocalizationService());
        }
    }


    private static void SeedLibrary(ProjectStore store)
    {
        foreach ((string name, string surface) in new[]
        {
            ("A picture with a really quite long name that has to fit on a card", "sign-small"),
            ("Short", "canvas-xxl"),
            ("Animated blinking neon sign with five frames", "animated-neon-large")
        })
        {
            using Avalonia.Media.Imaging.WriteableBitmap bitmap = BitmapPixels.ToBitmap(Picture(40, 20));
            using MemoryStream stream = new();
            bitmap.Save(stream);
            store.Save(new StudioProject { Name = name, SurfaceId = surface, SourceName = name + ".png" }, stream.ToArray(), stream.ToArray());
        }
    }
}
