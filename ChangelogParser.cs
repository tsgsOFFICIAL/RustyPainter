using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RustyPainter;

/// <summary>
/// Parses and recombines the "## {version}" section structure used by CHANGELOG.md, so a version's
/// entries can be sliced out and merged with a translated variant. Deliberately has no Avalonia
/// dependency (unlike <see cref="MarkdownRenderer"/>) so it can be shared by both the locally
/// embedded changelog and a remotely fetched one.
/// </summary>
public static partial class ChangelogParser
{
    /// <summary>
    /// Matches a heading that looks like an actual released version number (e.g. "0.5.0",
    /// "1.2.3-rc1", "0.5.0-nightly.abc1234"), as opposed to a placeholder heading like
    /// "Unreleased" or "Coming changes". Requires at least one "major.minor" pair since this
    /// project's versions are always dotted (see &lt;FileVersion&gt; in the csproj), with an
    /// optional "-prerelease" suffix.
    /// </summary>
    [GeneratedRegex(@"^\d+(\.\d+)+(-[0-9A-Za-z][0-9A-Za-z.-]*)?$")]
    private static partial Regex ReleasedVersionRegex();

    /// <summary>
    /// Splits Markdown into an ordered list of "## {version}" sections. Only "## " headings count
    /// as section boundaries (not "#" or "###"). Order is preserved as written, which in this
    /// project's CHANGELOG.md files means newest-to-oldest. A section's body is truncated early if
    /// it hits a horizontal rule line, so a trailing maintainer note doesn't bleed into the last
    /// real section.
    /// </summary>
    /// <param name="markdown">The full changelog Markdown.</param>
    public static IReadOnlyList<(string Version, string Body)> ParseSections(string markdown)
    {
        List<(string Version, string Body)> sections = new();
        string? currentVersion = null;
        List<string> currentBody = new();
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
                currentBody = new List<string>();
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
    /// Returns everything before the first "## " heading (e.g. the document title and intro line),
    /// so it can be preserved when sections are reassembled.
    /// </summary>
    /// <param name="markdown">The full changelog Markdown.</param>
    public static string ExtractPreamble(string markdown)
    {
        List<string> lines = new();

        foreach (string rawLine in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            string line = rawLine.TrimEnd();
            if (line.StartsWith("## "))
                break;

            lines.Add(line);
        }

        return string.Join('\n', lines).Trim('\n', '\r', ' ');
    }

    /// <summary>
    /// Returns the sections newer than <paramref name="installedVersion"/> (i.e. everything above
    /// its matching section), excluding any heading that doesn't look like a real released version
    /// number (e.g. "Unreleased", "Coming changes", or any other placeholder) - see
    /// <see cref="ReleasedVersionRegex"/>. Placeholders like that describe work in progress, not a
    /// tagged release, so they should never trigger or appear in an update notice. Matching against
    /// <paramref name="installedVersion"/> is an exact comparison after stripping a leading "v"/"V"
    /// and trimming, not a substring/prefix match, so "0.5.0" never accidentally matches
    /// "0.5.0-rc1". If <paramref name="installedVersion"/> isn't found at all, every real version
    /// section above is returned rather than none, since it's safer to over-show than to show
    /// nothing.
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
    /// Parses <paramref name="markdown"/> and returns the sections newer than
    /// <paramref name="installedVersion"/>, reassembled back into Markdown.
    /// </summary>
    public static string ExtractSince(string markdown, string installedVersion)
        => Reassemble(ExtractSince(ParseSections(markdown), installedVersion));

    /// <summary>
    /// Merges a localized section list over an English one: English defines which versions exist
    /// and their order, and each version's localized body is used when present, falling back to the
    /// English body for that one version otherwise. Mirrors <c>LocalizationService</c>'s per-key
    /// fallback to English.
    /// </summary>
    public static IReadOnlyList<(string Version, string Body)> MergeSectionsWithFallback(
        IReadOnlyList<(string Version, string Body)> englishSections,
        IReadOnlyList<(string Version, string Body)> localizedSections)
    {
        Dictionary<string, string> localizedByVersion = new();
        foreach ((string version, string body) in localizedSections)
            localizedByVersion[NormalizeVersion(version)] = body;

        List<(string Version, string Body)> merged = new();
        foreach ((string version, string englishBody) in englishSections)
        {
            string body = localizedByVersion.TryGetValue(NormalizeVersion(version), out string? localizedBody)
                ? localizedBody
                : englishBody;

            merged.Add((version, body));
        }

        return merged;
    }

    /// <summary>
    /// Parses and merges a localized changelog over an English one (see
    /// <see cref="MergeSectionsWithFallback"/>), reassembling the full document including the
    /// preamble. The localized preamble (document title/intro) is used when present, otherwise the
    /// English preamble is used, matching the same fallback philosophy applied to section bodies.
    /// </summary>
    public static string MergeWithFallback(string localizedMarkdown, string englishMarkdown)
    {
        IReadOnlyList<(string Version, string Body)> merged = MergeSectionsWithFallback(
            ParseSections(englishMarkdown), ParseSections(localizedMarkdown));

        return Combine(PreferredPreamble(localizedMarkdown, englishMarkdown), Reassemble(merged));
    }

    /// <summary>
    /// Merges a localized changelog over an English one, then slices to only the sections newer
    /// than <paramref name="installedVersion"/>. Merging happens before slicing (not the other way
    /// around) so English always remains the authoritative list/order of versions, even if the
    /// localized file has fallen behind or renamed a section. Returns an empty string (not just the
    /// preamble) when there's nothing newer, so callers can treat an empty result as "no update".
    /// </summary>
    public static string MergeAndExtractSince(string localizedMarkdown, string englishMarkdown, string installedVersion)
    {
        IReadOnlyList<(string Version, string Body)> merged = MergeSectionsWithFallback(
            ParseSections(englishMarkdown), ParseSections(localizedMarkdown));

        IReadOnlyList<(string Version, string Body)> since = ExtractSince(merged, installedVersion);
        if (since.Count == 0)
            return string.Empty;

        return Combine(PreferredPreamble(localizedMarkdown, englishMarkdown), Reassemble(since));
    }

    /// <summary>
    /// Returns the localized preamble when present, otherwise falls back to the English preamble.
    /// </summary>
    private static string PreferredPreamble(string localizedMarkdown, string englishMarkdown)
    {
        string localizedPreamble = ExtractPreamble(localizedMarkdown);
        return localizedPreamble.Length > 0 ? localizedPreamble : ExtractPreamble(englishMarkdown);
    }

    /// <summary>
    /// Joins a preamble and a body with a blank line between them, omitting either side if empty.
    /// </summary>
    private static string Combine(string preamble, string body)
    {
        if (preamble.Length == 0)
            return body;

        return body.Length == 0 ? preamble : $"{preamble}\n\n{body}";
    }

    /// <summary>
    /// Rebuilds a list of sections back into "## {version}" Markdown, in order.
    /// </summary>
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

    /// <summary>
    /// Normalizes a version string for comparison by stripping a leading "v"/"V" and trimming.
    /// </summary>
    private static string NormalizeVersion(string version) => version.TrimStart('v', 'V').Trim();

    /// <summary>
    /// Determines whether a section heading looks like a real released version number (see
    /// <see cref="ReleasedVersionRegex"/>), as opposed to a work-in-progress placeholder such as
    /// "Unreleased" or "Coming changes".
    /// </summary>
    private static bool IsReleasedVersion(string version) => ReleasedVersionRegex().IsMatch(NormalizeVersion(version));

    /// <summary>
    /// Determines whether the line is a horizontal rule (three or more repeated "-", "*" or "_").
    /// Intentionally duplicates <see cref="MarkdownRenderer"/>'s private rule detection so this
    /// class stays free of any Avalonia dependency.
    /// </summary>
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