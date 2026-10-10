namespace RustyPainter.Tests;

public class ChangelogParserTests
{
    private const string Sample = """
        # Changelog

        All notable changes are documented here.

        ## Unreleased

        - Work in progress.

        ## 0.3.0

        ### Added

        - Third.

        ## 0.2.0

        - Second.

        ## 0.1.0

        - First.
        """;

    [Fact]
    public void Returns_only_the_versions_newer_than_the_installed_one()
    {
        string result = ChangelogParser.ExtractSince(Sample, "0.2.0");

        Assert.Contains("## 0.3.0", result);
        Assert.Contains("Third.", result);
        Assert.DoesNotContain("0.2.0", result);
        Assert.DoesNotContain("First.", result);
    }

    [Fact]
    public void Ignores_placeholder_headings_such_as_unreleased()
    {
        string result = ChangelogParser.ExtractSince(Sample, "0.2.0");

        Assert.DoesNotContain("Unreleased", result);
        Assert.DoesNotContain("Work in progress.", result);
    }

    [Fact]
    public void Returns_nothing_when_already_on_the_newest_version()
    {
        Assert.Equal("", ChangelogParser.ExtractSince(Sample, "0.3.0"));
    }

    [Fact]
    public void Returns_every_released_version_when_the_installed_one_is_not_listed()
    {
        string result = ChangelogParser.ExtractSince(Sample, "9.9.9");

        Assert.Contains("## 0.3.0", result);
        Assert.Contains("## 0.2.0", result);
        Assert.Contains("## 0.1.0", result);
        Assert.DoesNotContain("Unreleased", result);
    }

    [Fact]
    public void Accepts_a_leading_v_on_the_installed_version()
    {
        Assert.Contains("## 0.3.0", ChangelogParser.ExtractSince(Sample, "v0.2.0"));
    }

    [Fact]
    public void Does_not_confuse_a_prerelease_with_its_release()
    {
        // "0.2.0-nightly.abc1234" is not the section "0.2.0", so everything released is shown rather than guessing.
        string result = ChangelogParser.ExtractSince(Sample, "0.2.0-nightly.abc1234");

        Assert.Contains("## 0.2.0", result);
    }

    [Fact]
    public void Stops_a_section_at_a_horizontal_rule()
    {
        const string markdown = "## 0.2.0\n\n- Real entry.\n\n---\n\nMaintainer note.\n\n## 0.1.0\n\n- Old.";

        string result = ChangelogParser.ExtractSince(markdown, "0.1.0");

        Assert.Contains("Real entry.", result);
        Assert.DoesNotContain("Maintainer note.", result);
    }
}
