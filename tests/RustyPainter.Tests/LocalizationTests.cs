using RustyPainter.Interface;
using RustyPainter.Localization;
using RustyPainter.Service;
using RustyPainter.Interface;
using RustyPainter.ViewModels;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using Xunit;

namespace RustyPainter.Tests;

/// <summary>Tests that change the language of the whole app run alone, so no other test sees a text in another language.</summary>
[CollectionDefinition("Language", DisableParallelization = true)]
public sealed class LanguageCollection;

public class CatalogTests
{
    private static readonly AppLanguage[] Languages = [AppLanguage.English, AppLanguage.Danish, AppLanguage.Russian];

    private static string Root()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RustyPainter.csproj")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("The project folder was not found.");
    }

    private static Dictionary<string, string> Catalog(AppLanguage language)
        => JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(Root(), "Lang", LocalizationService.FileName(language) + ".json")))!;

    private static readonly Regex PluralSuffix = new("_(one|few|many|other)$");

    private static string Family(string key) => PluralSuffix.Replace(key, "");

    [Fact]
    public void Every_language_has_the_same_texts()
    {
        HashSet<string> english = Catalog(AppLanguage.English).Keys.Select(Family).ToHashSet();

        foreach (AppLanguage language in Languages)
        {
            HashSet<string> keys = Catalog(language).Keys.Select(Family).ToHashSet();
            Assert.Empty(english.Except(keys));
            Assert.Empty(keys.Except(english));
        }
    }

    [Fact]
    public void Texts_with_a_number_have_the_forms_their_language_needs()
    {
        foreach (AppLanguage language in Languages)
        {
            Dictionary<string, string> catalog = Catalog(language);

            foreach (string family in catalog.Keys.Where(key => PluralSuffix.IsMatch(key)).Select(Family).Distinct())
            {
                Assert.True(catalog.ContainsKey(family + "_other"), $"{family} has no _other in {language}");
                Assert.True(catalog.ContainsKey(family + "_one"), $"{family} has no _one in {language}");

                if (language == AppLanguage.Russian)
                {
                    Assert.True(catalog.ContainsKey(family + "_few"), $"{family} has no _few in Russian");
                    Assert.True(catalog.ContainsKey(family + "_many"), $"{family} has no _many in Russian");
                }
            }
        }
    }

    [Fact]
    public void Every_language_fills_in_the_same_values()
    {
        Dictionary<string, string> english = Catalog(AppLanguage.English);

        foreach (AppLanguage language in Languages.Skip(1))
        {
            foreach ((string key, string text) in Catalog(language))
            {
                if (!english.TryGetValue(key, out string? original))
                    continue;

                Assert.True(Placeholders(original).SetEquals(Placeholders(text)), $"{key} in {language} does not use the same values as the English text");
            }
        }
    }

    private static HashSet<string> Placeholders(string text)
        => Regex.Matches(text, @"\{(\d+)").Select(match => match.Groups[1].Value).ToHashSet();

    [Fact]
    public void No_text_is_empty_or_uses_a_long_dash()
    {
        foreach (AppLanguage language in Languages)
        {
            foreach ((string key, string text) in Catalog(language))
            {
                Assert.False(string.IsNullOrWhiteSpace(text), $"{key} is empty in {language}");
                Assert.DoesNotContain((char)0x2014, text);
                Assert.DoesNotContain((char)0x2013, text);
            }
        }
    }

    [Fact]
    public void Every_text_the_code_and_the_pages_ask_for_exists()
    {
        HashSet<string> known = Catalog(AppLanguage.English).Keys.ToHashSet();
        List<string> missing = [];

        Regex code = new("""Loc\.(?:Get|Format|Count|Has)\("([A-Za-z_]+)"\)?""");
        Regex xaml = new(@"\{loc:L ([A-Za-z_]+)\}");
        Regex labels = new("""\("([A-Za-z]+)", (?:SourceKind|FitMode|ColorMode|Dithering|ImageFilter|PreviewMode|false|true|\d)""");

        foreach (string file in Directory.EnumerateFiles(Root(), "*.*", SearchOption.AllDirectories)
                     .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                                    && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                                    && !file.Contains($"{Path.DirectorySeparatorChar}externals{Path.DirectorySeparatorChar}")
                                    && !file.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}")))
        {
            string extension = Path.GetExtension(file);
            if (extension is not (".cs" or ".axaml"))
                continue;

            string text = File.ReadAllText(file);

            foreach (Match match in (extension == ".cs" ? code : xaml).Matches(text))
                Check(match.Groups[1].Value, file);

            if (Path.GetFileName(file) == "StudioViewModel.cs")
                foreach (Match match in labels.Matches(text))
                    Check(match.Groups[1].Value, file);
        }

        Assert.Empty(missing);

        void Check(string key, string file)
        {
            if (!known.Contains(key) && !known.Contains(key + "_other"))
                missing.Add($"{key} in {Path.GetFileName(file)}");
        }
    }

    [Fact]
    public void Every_surface_has_a_name_in_every_language()
    {
        SurfaceService surfaces = new();

        foreach (AppLanguage language in Languages)
        {
            Dictionary<string, string> catalog = Catalog(language);

            foreach (Surface surface in surfaces.Catalog.Surfaces)
                Assert.True(catalog.ContainsKey($"Surface_{surface.Id}"), $"{surface.Id} has no name in {language}");
        }
    }

    [Fact]
    public void No_page_has_a_text_that_is_not_in_the_catalog()
    {
        string[] allowed = ["RUSTY", "PAINTER", "RustyPainter", "tsgsOFFICIAL", "·", "Discord", "GitHub", "Ko-fi", "x", "%", "/", "RustyPainter hologram", "RustyPainter status"];
        Regex attribute = new("""\b(?:Text|Content|PlaceholderText|Header|Title|ToolTip\.Tip)="([^"{][^"]*)"(?!\w)""");
        List<string> literals = [];

        foreach (string file in Directory.EnumerateFiles(Root(), "*.axaml", SearchOption.AllDirectories)
                     .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                                    && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                                    && !file.Contains($"{Path.DirectorySeparatorChar}externals{Path.DirectorySeparatorChar}")
                                    && !file.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}")))
        {
            foreach (Match match in attribute.Matches(File.ReadAllText(file)))
            {
                string value = match.Groups[1].Value;
                if (!allowed.Contains(value) && value.Any(char.IsLetter))
                    literals.Add($"{Path.GetFileName(file)}: {value}");
            }
        }

        Assert.Empty(literals);
    }
}

[Collection("Language")]
public class LanguageTests
{
    private static LocalizationService Service() => new();

    [Theory]
    [InlineData(AppLanguage.English, 1, "one")]
    [InlineData(AppLanguage.English, 2, "other")]
    [InlineData(AppLanguage.English, 0, "other")]
    [InlineData(AppLanguage.Danish, 1, "one")]
    [InlineData(AppLanguage.Danish, 5, "other")]
    [InlineData(AppLanguage.Russian, 1, "one")]
    [InlineData(AppLanguage.Russian, 21, "one")]
    [InlineData(AppLanguage.Russian, 11, "many")]
    [InlineData(AppLanguage.Russian, 2, "few")]
    [InlineData(AppLanguage.Russian, 24, "few")]
    [InlineData(AppLanguage.Russian, 12, "many")]
    [InlineData(AppLanguage.Russian, 14, "many")]
    [InlineData(AppLanguage.Russian, 5, "many")]
    [InlineData(AppLanguage.Russian, 100, "many")]
    [InlineData(AppLanguage.Russian, 101, "one")]
    [InlineData(AppLanguage.Russian, 112, "many")]
    public void Plural_forms_follow_the_rules_of_the_language(AppLanguage language, long count, string expected)
        => Assert.Equal(expected, Loc.PluralCategory(language, count));

    [AvaloniaFact]
    public void A_key_without_a_text_comes_back_as_itself()
    {
        LocalizationService service = Service();
        Assert.Equal("NoSuchKey", service["NoSuchKey"]);
        Assert.False(service.Has("NoSuchKey"));
    }

    [AvaloniaFact]
    public void Picking_a_language_shows_its_texts()
    {
        LocalizationService service = Service();
        service.SetLanguage(AppLanguage.Russian, save: false);

        try
        {
            Assert.Equal("Отмена", service["Cancel"]);
        }
        finally
        {
            service.SetLanguage(AppLanguage.English, save: false);
        }
    }

    [AvaloniaFact]
    public void The_language_is_remembered_in_the_settings()
    {
        string path = Path.Combine(Path.GetTempPath(), $"rustypainter-test-settings-{Guid.NewGuid():N}", "settings.json");
        AppSettingsService settings = new(path);
        settings.Initialize();

        LocalizationService service = new(settings);
        service.Initialize();
        service.SetLanguage(AppLanguage.Danish);

        try
        {
            AppSettingsService again = new(path);
            again.Initialize();
            Assert.Equal(AppLanguage.Danish, again.Current.Language);

            LocalizationService restarted = new(again);
            restarted.Initialize();
            Assert.Equal(AppLanguage.Danish, restarted.Current);
            Assert.Equal("Indstillinger", restarted["NavSettings"]);
        }
        finally
        {
            new LocalizationService().SetLanguage(AppLanguage.English, save: false);
        }
    }

    [AvaloniaFact]
    public void Switching_the_language_changes_the_texts_view_models_build()
    {
        LocalizationService service = Service();
        Loc.Install(service);

        try
        {
            Assert.Equal("1 project", Loc.Count("LibraryProjects", 1));

            service.SetLanguage(AppLanguage.Russian, save: false);
            Assert.Equal("1 проект", Loc.Count("LibraryProjects", 1));
            Assert.Equal("2 проекта", Loc.Count("LibraryProjects", 2));
            Assert.Equal("5 проектов", Loc.Count("LibraryProjects", 5));
            Assert.Equal("21 проект", Loc.Count("LibraryProjects", 21));

            service.SetLanguage(AppLanguage.Danish, save: false);
            Assert.Equal("3 projekter", Loc.Count("LibraryProjects", 3));
            Assert.Equal("Mellem træskilt", Loc.Get("Surface_sign-medium"));
        }
        finally
        {
            service.SetLanguage(AppLanguage.English, save: false);
            Loc.Install(new LocalizationService());
        }
    }

    [AvaloniaFact]
    public void Times_are_described_in_the_language_in_use()
    {
        LocalizationService service = Service();
        Loc.Install(service);

        try
        {
            Assert.Equal("About 1 minute", RustyPainter.Painting.PaintTimeEstimator.Describe(TimeSpan.FromSeconds(60)));
            Assert.Equal("About 4 minutes", RustyPainter.Painting.PaintTimeEstimator.Describe(TimeSpan.FromMinutes(4)));

            service.SetLanguage(AppLanguage.Russian, save: false);
            Assert.Equal("Около 1 минуты", RustyPainter.Painting.PaintTimeEstimator.Describe(TimeSpan.FromSeconds(60)));
            Assert.Equal("Около 4 минут", RustyPainter.Painting.PaintTimeEstimator.Describe(TimeSpan.FromMinutes(4)));
            Assert.Equal("Несколько секунд", RustyPainter.Painting.PaintTimeEstimator.Describe(TimeSpan.FromSeconds(3)));
        }
        finally
        {
            service.SetLanguage(AppLanguage.English, save: false);
            Loc.Install(new LocalizationService());
        }
    }

    [AvaloniaFact]
    public void Numbers_follow_the_culture_of_the_language()
    {
        LocalizationService service = Service();
        Loc.Install(service);

        try
        {
            service.SetLanguage(AppLanguage.Danish, save: false);
            Assert.Equal("Ca. 1,5 timer", RustyPainter.Painting.PaintTimeEstimator.Describe(TimeSpan.FromHours(1.5)));
            Assert.Equal("da-DK", CultureInfo.CurrentCulture.Name);
        }
        finally
        {
            service.SetLanguage(AppLanguage.English, save: false);
            Loc.Install(new LocalizationService());
        }
    }

    [AvaloniaTheory]
    [InlineData(0u, 0x20u, "Space", "Mellemrum", "Пробел")]
    [InlineData(0u, 0x1Bu, "Esc", "Esc", "Esc")]
    [InlineData(4u, 0x41u, "Shift+A", "Skift+A", "Shift+A")]
    [InlineData(0u, 0x77u, "F8", "F8", "F8")]
    public void Keys_are_written_in_the_language_in_use_but_saved_the_same(uint modifiers, uint key, string english, string danish, string russian)
    {
        LocalizationService service = Service();
        Loc.Install(service);

        try
        {
            RustyPainter.Painting.Hotkey hotkey = new(modifiers, key);
            Assert.Equal(english, hotkey.Localized);

            service.SetLanguage(AppLanguage.Danish, save: false);
            Assert.Equal(danish, hotkey.Localized);

            service.SetLanguage(AppLanguage.Russian, save: false);
            Assert.Equal(russian, hotkey.Localized);

            Assert.Equal(english, hotkey.Display);
        }
        finally
        {
            service.SetLanguage(AppLanguage.English, save: false);
            Loc.Install(new LocalizationService());
        }
    }

    [AvaloniaFact]
    public void The_settings_page_offers_every_language_and_every_theme()
    {
        AppSettingsService settings = TestSettings.Create();
        settings.Initialize();
        LocalizationService service = Service();
        Loc.Install(service);

        try
        {
            SettingsViewModel page = new(settings, new UpdateService(), new DialogService(), "", service);

            Assert.Equal(["English", "Dansk", "Русский"], page.LanguageOptions.Select(option => option.Name));
            Assert.Equal(["Same as Windows", "Light", "Dark"], page.ThemeOptions.Select(option => option.Name));

            page.SelectedLanguage = page.LanguageOptions[1];
            Assert.Equal(AppLanguage.Danish, service.Current);
            Assert.Equal(["Samme som Windows", "Lys", "Mørk"], page.ThemeOptions.Select(option => option.Name));

            page.SelectedTheme = page.ThemeOptions[1];
            Assert.Equal(AppTheme.Light, settings.Current.Theme);
        }
        finally
        {
            service.SetLanguage(AppLanguage.English, save: false);
            Loc.Install(new LocalizationService());
        }
    }
}
