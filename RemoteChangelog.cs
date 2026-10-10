using RustyPainter.Interface;
using System.Threading.Tasks;
using System.Threading;
using System.Net.Http;
using System.Net;
using System;

namespace RustyPainter;

/// <summary>
/// Fetches the changelog directly from GitHub so an update check can show the user what changed in
/// the version(s) they're about to install, without waiting for a new build to embed it locally.
/// </summary>
public static class RemoteChangelog
{
    private const string RawBaseUrl = "https://raw.githubusercontent.com/tsgsOFFICIAL/RustyPainter/master/";

    private static readonly HttpClient Client = CreateClient();

    /// <summary>
    /// Fetches the changelog and returns only the sections newer than <paramref name="installedVersion"/>,
    /// in the language asked for, with English for any version that has not been translated yet.
    /// </summary>
    /// <param name="language">The language to prefer for the text.</param>
    /// <param name="installedVersion">The currently installed version (e.g. from <see cref="Utility.GetDisplayVersion"/>).</param>
    /// <param name="ct">A token to cancel the fetch.</param>
    /// <exception cref="HttpRequestException">The English file could not be fetched.</exception>
    /// <exception cref="TaskCanceledException">The fetch timed out or was canceled.</exception>
    public static async Task<string> GetChangesSinceAsync(AppLanguage language, string installedVersion, CancellationToken ct = default)
    {
        // English is the file that must be there. Without it there is nothing to show, and the failure goes to the caller.
        string english = await FetchAsync(BundledChangelog.FileName(AppLanguage.English), ct);

        string localized = english;
        if (language != AppLanguage.English)
        {
            try
            {
                localized = await FetchAsync(BundledChangelog.FileName(language), ct);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // The translated file has not been published yet, so English is shown.
                localized = english;
            }
        }

        return ChangelogParser.MergeAndExtractSince(localized, english, installedVersion);
    }

    private static Task<string> FetchAsync(string fileName, CancellationToken ct)
        => Client.GetStringAsync(RawBaseUrl + fileName, ct);

    private static HttpClient CreateClient()
    {
        HttpClient client = new() { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.Add("User-Agent", "RustyPainter");
        return client;
    }
}
