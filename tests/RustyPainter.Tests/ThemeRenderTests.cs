using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using RustyPainter.Controls;
using RustyPainter.Interface;
using RustyPainter.Localization;
using RustyPainter.Service;
using RustyPainter.ViewModels;
using RustyPainter.Views;

namespace RustyPainter.Tests;

/// <summary>Draws the pages in each look and language, to see that nothing is unreadable and nothing is cut off.</summary>
[Collection("Language")]
public class ThemeRenderTests
{
    public static IEnumerable<object[]> Looks()
    {
        foreach (AppLanguage language in Enum.GetValues<AppLanguage>())
        {
            yield return [language, "Light"];
            yield return [language, "Dark"];
        }
    }

    private static (byte R, byte G, byte B) PixelAt(WriteableBitmap bitmap, int x, int y)
    {
        IntPtr buffer = Marshal.AllocHGlobal(4);
        try
        {
            bitmap.CopyPixels(new PixelRect(x, y, 1, 1), buffer, 4, 4);
            byte[] bytes = new byte[4];
            Marshal.Copy(buffer, bytes, 0, 4);
            return bitmap.Format == PixelFormat.Bgra8888 ? (bytes[2], bytes[1], bytes[0]) : (bytes[0], bytes[1], bytes[2]);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static WriteableBitmap Draw(Control page, Control? sidebar, string variant, string name)
    {
        Grid layout = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        if (sidebar is not null)
            layout.Children.Add(sidebar);

        Grid.SetColumn(page, 1);
        layout.Children.Add(page);

        Window window = new()
        {
            Width = 1296,
            Height = 760,
            RequestedThemeVariant = variant == "Light" ? ThemeVariant.Light : ThemeVariant.Dark,
            [!Window.BackgroundProperty] = new DynamicResourceExtension("BgColor"),
            Content = layout
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        WriteableBitmap frame = window.CaptureRenderedFrame()!;
        if (Environment.GetEnvironmentVariable("RUSTYPAINTER_SHOTS") is { Length: > 0 } folder)
            frame.Save(Path.Combine(folder, name + ".png"));

        return frame;
    }

    private static void ExpectLook(WriteableBitmap frame, string variant)
    {
        // The strip next to the sidebar shows the page background itself.
        (byte r, byte g, byte b) = PixelAt(frame, 1290, 4);

        if (variant == "Light")
            Assert.True(r > 200 && g > 200 && b > 200, $"The light page is not light: {r},{g},{b}");
        else
            Assert.True(r < 70 && g < 70 && b < 70, $"The dark page is not dark: {r},{g},{b}");
    }

    private static MainWindowViewModel Shell(AppSettingsService settings)
        => new(new UpdateService(), new DialogService(), settings, new SurfaceService(), new FakeGame(null), new FakeHotkeys(), new FakeEngines(new FakeMachine()), new FakeHologram(), new FakeOverlay(), TestLibrary.Create());

    private static void Use(AppLanguage language)
    {
        LocalizationService service = new();
        Loc.Install(service);
        service.SetLanguage(language, save: false);
    }

    private static void Reset()
    {
        LocalizationService service = new();
        Loc.Install(service);
        service.SetLanguage(AppLanguage.English, save: false);
    }

    [AvaloniaTheory]
    [MemberData(nameof(Looks))]
    public void Pages_are_drawn_in_every_look_and_language(AppLanguage language, string variant)
    {
        try
        {
            Use(language);
            AppSettingsService settings = TestSettings.Create();
            settings.Initialize();
            MainWindowViewModel shell = Shell(settings);

            string tag = $"{language}-{variant}";

            ExpectLook(Draw(new HomeView { DataContext = new HomeViewModel(shell.Sidebar) }, new Sidebar { ViewModel = shell.Sidebar }, variant, $"home-{tag}"), variant);
            ExpectLook(Draw(new SettingsView { DataContext = new SettingsViewModel(settings, new UpdateService(), new DialogService(), "C:\\Library") }, new Sidebar { ViewModel = shell.Sidebar }, variant, $"settings-{tag}"), variant);
            ExpectLook(Draw(new StudioView { DataContext = new StudioViewModel(new SurfaceService()) }, new Sidebar { ViewModel = shell.Sidebar }, variant, $"studio-{tag}"), variant);
            StudioViewModel animated = new(new SurfaceService()) { ReprocessDelay = TimeSpan.Zero };
            animated.SelectedSurface = animated.AllSurfaces.Single(option => option.Surface.Id == "animated-neon-large");
            string picture = Path.Combine(Path.GetTempPath(), $"rp-anim-{Guid.NewGuid():N}.png");
            TestImages.SavePng(TestImages.Rainbow(), picture);
            animated.TryLoad(picture);
            animated.ProcessNow();
            File.Delete(picture);
            animated.AddFrame();
            animated.AddFrame();
            ExpectLook(Draw(new StudioView { DataContext = animated }, new Sidebar { ViewModel = shell.Sidebar }, variant, $"studio-animated-{tag}"), variant);
            ExpectLook(Draw(new LibraryView { DataContext = new LibraryViewModel(TestLibrary.Create(), new SurfaceService()) }, new Sidebar { ViewModel = shell.Sidebar }, variant, $"library-{tag}"), variant);
        }
        finally
        {
            Reset();
        }
    }
}
