using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RustyPainter;

/// <summary>
/// Parses the "## {version}" section structure used by CHANGELOG.md so the sections newer than the
/// installed version can be sliced out for the update prompt. Deliberately has no Avalonia dependency
/// (unlike <see cref="MarkdownRenderer"/>) so it works for both the embedded and a remotely fetched file.
/// </summary>
public static partial class ChangelogParser
{
    /// <summary>
    /// Matches a heading that looks like an actual released version number (e.g. "0.5.0",
    /// "1.2.3-rc1", "0.5.0-nightly.abc1234"), as opposed to a placeholder heading like
    /// "Unreleased" or "Coming changes".
    /// </summary>
    [GeneratedRegex(@"^\d+(\.\d+)+(-[0-9A-Za-z][0-9A-Za-z.-]*)?$")]
    private static partial Regex ReleasedVersionRegex();

    /// <summary>
    /// Splits Markdown into an ordered list of "## {version}" sections. Only "## " headings count as
    /// section boundaries. Order is preserved as written (newest-to-oldest). A section's body is truncated
    /// early at a horizontal rule, so a trailing maintainer note doesn't bleed into the last real section.
    /// </summary>
    public static IReadOnlyList<(string Version, string Body)> ParseSections(string markdown)
    {
        List<(string Version, string Body)> sections = [];
        string? currentVersion = null;
        List<string> currentBody = [];
        bool bodyTruncated = false;

        void Flush()
        {
            if (currentVersion is not null)
                sections.Add((currentVersion, string.Join('\n', currentBody).Trim('\n', '\r', ' ')));
        }

        foreach (string rawLine in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            string line = rawLine.TrimEnd();

            if (line.StartsWith("## "))
            {
                Flush();
                currentVersion = line[3..].Trim();
                currentBody = [];
                bodyTruncated = false;
                continue;
            }

            if (currentVersion is null || bodyTruncated)
                continue;

            if (IsRuleLine(line.Trim()))
            {
                bodyTruncated = true;
                continue;
            }

            currentBody.Add(line);
        }

        Flush();
        return sections;
    }

    /// <summary>
    /// Returns the sections newer than <paramref name="installedVersion"/> (everything above its matching
    /// section), excluding any heading that isn't a real released version number. If the installed version
    /// isn't found at all, every real version section is returned, since it's safer to over-show than to show nothing.
    /// </summary>
    public static IReadOnlyList<(string Version, string Body)> ExtractSince(
        IReadOnlyList<(string Version, string Body)> sections, string installedVersion)
    {
        string normalizedInstalled = NormalizeVersion(installedVersion);
        int index = -1;

        for (int i = 0; i < sections.Count; i++)
        {
            if (NormalizeVersion(sections[i].Version) == normalizedInstalled)
            {
                index = i;
                break;
            }
        }

        IEnumerable<(string Version, string Body)> newer = index < 0 ? sections : sections.Take(index);
        return newer.Where(section => IsReleasedVersion(section.Version)).ToList();
    }

    /// <summary>
    /// Parses <paramref name="markdown"/> and returns the sections newer than <paramref name="installedVersion"/>,
    /// reassembled into Markdown. Empty when there is nothing newer.
    /// </summary>
    public static string ExtractSince(string markdown, string installedVersion)
        => Reassemble(ExtractSince(ParseSections(markdown), installedVersion));

    private static string Reassemble(IReadOnlyList<(string Version, string Body)> sections)
    {
        StringBuilder builder = new();

        foreach ((string version, string body) in sections)
        {
            if (builder.Length > 0)
                builder.Append("\n\n");

            builder.Append("## ").Append(version);

            if (body.Length > 0)
                builder.Append('\n').Append(body);
        }

        return builder.ToString();
    }

    private static string NormalizeVersion(string version) => version.TrimStart('v', 'V').Trim();

    private static bool IsReleasedVersion(string version) => ReleasedVersionRegex().IsMatch(NormalizeVersion(version));

    private static bool IsRuleLine(string line)
    {
        if (line.Length < 3)
            return false;

        char first = line[0];
        if (first is not ('-' or '*' or '_'))
            return false;

        foreach (char c in line)
        {
            if (c != first)
                return false;
        }

        return true;
    }
}
