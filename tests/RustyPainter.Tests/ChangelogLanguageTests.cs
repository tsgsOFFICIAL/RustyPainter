using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using RustyPainter.Interface;

namespace RustyPainter.Tests;

public class ChangelogLanguageTests
{
    private static string Plain(TextBlock text)
        => text.Inlines is { Count: > 0 } inlines ? string.Concat(inlines.OfType<Avalonia.Controls.Documents.Run>().Select(run => run.Text)) : text.Text ?? "";

    private static readonly string[] VersionHeadings = ["## 0.1.1", "## 0.1.0"];

    [Theory]
    [InlineData(AppLanguage.English, "# Changelog")]
    [InlineData(AppLanguage.Danish, "# Ændringslog")]
    [InlineData(AppLanguage.Russian, "# Журнал изменений")]
    public void The_changelog_ships_in_every_language(AppLanguage language, string title)
    {
        string markdown = BundledChangelog.Load(language);

        Assert.StartsWith(title, markdown);
        foreach (string heading in VersionHeadings)
            Assert.Contains(heading, markdown);
    }

    [Fact]
    public void Every_language_lists_the_same_versions_in_the_same_order()
    {
        List<string> Versions(AppLanguage language)
            => [.. ChangelogParser.ParseSections(BundledChangelog.Load(language)).Select(section => section.Version)];

        Assert.Equal(Versions(AppLanguage.English), Versions(AppLanguage.Danish));
        Assert.Equal(Versions(AppLanguage.English), Versions(AppLanguage.Russian));
    }

    [Fact]
    public void A_version_that_is_not_translated_yet_is_shown_in_english()
    {
        const string english = "# Changelog\n\nIntro.\n\n## 0.3.0\n\n- New thing.\n\n## 0.2.0\n\n- Older thing.";
        const string danish = "# Ændringslog\n\n## 0.2.0\n\n- Ældre ting.";

        string merged = ChangelogParser.MergeWithFallback(danish, english);

        Assert.Contains("# Ændringslog", merged);
        Assert.Contains("- New thing.", merged);
        Assert.Contains("- Ældre ting.", merged);
        Assert.DoesNotContain("- Older thing.", merged);
        Assert.True(merged.IndexOf("## 0.3.0", StringComparison.Ordinal) < merged.IndexOf("## 0.2.0", StringComparison.Ordinal));
    }

    [Fact]
    public void Only_what_is_newer_than_the_installed_version_is_kept_after_merging()
    {
        const string english = "# Changelog\n\n## 0.3.0\n\n- A.\n\n## 0.2.0\n\n- B.\n\n## 0.1.0\n\n- C.";
        const string russian = "# Журнал изменений\n\n## 0.3.0\n\n- А.\n\n## 0.1.0\n\n- В.";

        string changes = ChangelogParser.MergeAndExtractSince(russian, english, "0.2.0");

        Assert.Contains("- А.", changes);
        Assert.DoesNotContain("0.2.0", changes);
        Assert.DoesNotContain("0.1.0", changes);
        Assert.Equal("", ChangelogParser.MergeAndExtractSince(russian, english, "0.3.0"));
    }

    [Fact]
    public void The_file_names_follow_the_languages()
    {
        Assert.Equal("CHANGELOG.md", BundledChangelog.FileName(AppLanguage.English));
        Assert.Equal("CHANGELOG.da-DK.md", BundledChangelog.FileName(AppLanguage.Danish));
        Assert.Equal("CHANGELOG.ru-RU.md", BundledChangelog.FileName(AppLanguage.Russian));
    }

    [AvaloniaFact]
    public void A_picture_on_a_line_of_its_own_is_drawn_and_a_missing_one_shows_its_description()
    {
        string path = Path.Combine(Path.GetTempPath(), $"rp-changelog-{Guid.NewGuid():N}.png");
        TestImages.SavePng(TestImages.Rainbow(), path);

        try
        {
            Control control = AnimatedImage.BuildImageControl(File.ReadAllBytes(path));
            Assert.IsType<Avalonia.Controls.Image>(control);

            Control rendered = MarkdownRenderer.Render($"Intro\n\n![Demo]({path})\n\n- item");
            Window window = new() { Content = rendered, Width = 400, Height = 300 };
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.NotEmpty(rendered.GetVisualDescendants().OfType<ContentControl>());
            Assert.Contains(rendered.GetVisualDescendants().OfType<TextBlock>(), text => Plain(text) == "Intro");
            window.Close();
        }
        finally
        {
            File.Delete(path);
        }

        Control missing = MarkdownRenderer.Render("![Not there](C:/no/such/file.gif)");
        Window other = new() { Content = missing, Width = 400, Height = 200 };
        other.Show();
        for (int i = 0; i < 50; i++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }

        Assert.Contains(missing.GetVisualDescendants().OfType<TextBlock>(), text => Plain(text) == "Not there");
        other.Close();
    }
}
