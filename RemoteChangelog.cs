using System.Threading.Tasks;
using System.Threading;
using System.Net.Http;

namespace RustyPainter;

/// <summary>
/// Fetches the changelog directly from GitHub so an update check can show the user what changed in
/// the version(s) they're about to install, without waiting for a new build to embed it locally.
/// </summary>
public static class RemoteChangelog
{
    private const string ChangelogUrl = "https://raw.githubusercontent.com/tsgsOFFICIAL/RustyPainter/master/CHANGELOG.md";

    private static readonly HttpClient Client = CreateClient();

    /// <summary>
    /// Fetches the changelog and returns only the sections newer than <paramref name="installedVersion"/>.
    /// </summary>
    /// <param name="installedVersion">The currently installed version (e.g. from <see cref="Utility.GetDisplayVersion"/>).</param>
    /// <param name="ct">A token to cancel the fetch.</param>
    /// <exception cref="HttpRequestException">The fetch failed.</exception>
    /// <exception cref="TaskCanceledException">The fetch timed out or was canceled.</exception>
    public static async Task<string> GetChangesSinceAsync(string installedVersion, CancellationToken ct = default)
    {
        string markdown = await Client.GetStringAsync(ChangelogUrl, ct);
        return ChangelogParser.ExtractSince(markdown, installedVersion);
    }

    private static HttpClient CreateClient()
    {
        HttpClient client = new() { Timeout = System.TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.Add("User-Agent", "RustyPainter");
        return client;
    }
}
