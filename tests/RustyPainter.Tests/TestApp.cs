using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using RustyPainter.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace RustyPainter.Tests;

/// <summary>
/// A bare Avalonia application with the same theme and styles as the real one, so pages can be drawn and
/// pictures decoded without opening a window. The real <c>App</c> needs the full service container.
/// </summary>
public class TestApp : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;

        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://RustyPainter/")) { Source = new Uri("avares://Avalonia.Controls.ColorPicker/Themes/Fluent/Fluent.xaml") });
        Styles.Add(new StyleInclude(new Uri("avares://RustyPainter/")) { Source = new Uri("avares://RustyPainter/Styles/AppStyles.axaml") });
        Styles.Add(new StyleInclude(new Uri("avares://RustyPainter/")) { Source = new Uri("avares://IconPacks.Avalonia.PhosphorIcons/PhosphorIcons.axaml") });

        DataTemplates.Add(new RustyPainter.ViewLocator());

        Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://RustyPainter/")) { Source = new Uri("avares://RustyPainter/Theme/Colors.axaml") });
    }
}

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
