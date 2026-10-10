using RustyPainter.Localization;
using System.Security.Cryptography;
using System.Net.Http.Headers;
using RustyPainter.Service.Logging;
using RustyPainter.Interface;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Threading;
using System.Net.Http;
using System.Net;
using System.IO.Compression;
using System.Text.Json;
using System.IO;
using System;

namespace RustyPainter.Service;

/// <summary>
/// Checks GitHub Releases for a newer version and applies it once downloaded. Installer-managed
/// installs (see <see cref="InstallRegistration.IsInstallerInstall"/>) are updated by silently re-running
/// the new Setup.exe, which keeps the registered Add/Remove Programs version in sync, then relaunching
/// the app once Setup exits. Portable zip installs are updated by swapping the extracted files in over
/// the running app.
/// </summary>
public sealed class UpdateService : IUpdateService
{
    private const string Owner = "tsgsOFFICIAL";
    private const string Repo = "RustyPainter";
    private const string ExecutableName = "RustyPainter.exe";

    private const int MaxAttempts = 4;
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(45);

    // Debug builds only: lets the update flow be exercised end to end against a local fake of the GitHub
    // API. Compiled out of Release so a shipped build can never be pointed somewhere else.
    private static readonly string ApiBase =
#if DEBUG
        Environment.GetEnvironmentVariable("RUSTYPAINTER_UPDATE_API") ?? "https://api.github.com";
#else
        "https://api.github.com";
#endif

    private static readonly HttpClient Client = CreateClient();

    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        using JsonDocument doc = await GetLatestReleaseAsync(ct);

        string tagName = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
        string latestVersion = tagName.TrimStart('v');

        if (!TryParseCore(latestVersion, out Version latest) ||
            !TryParseCore(Utility.GetDisplayVersion(), out Version current) ||
            latest <= current)
        {
            return null;
        }

        return FindAsset(doc.RootElement, latestVersion, tagName);
    }

    public async Task ApplyUpdateAsync(UpdateInfo update, IProgress<UpdateProgress>? progress = null, CancellationToken ct = default)
    {
        string? downloadPath = null;

        try
        {
            downloadPath = Path.Combine(Path.GetTempPath(), update.AssetName);
            AppLog.Info("UpdateService", $"Applying update {update.Version} from {update.AssetName}.");
            await DownloadToFileAsync(update, downloadPath, progress, ct);

            progress?.Report(new UpdateProgress(UpdateStage.Verifying));
            await VerifyDigestAsync(update, downloadPath, ct);

            if (update.AssetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                progress?.Report(new UpdateProgress(UpdateStage.Restarting));
                await Task.Delay(1500, CancellationToken.None); // let the user read the final status line
                ApplyInstallerUpdate(update, downloadPath);
            }
            else
            {
                string stagingPath = ExtractPortableUpdate(update, downloadPath);
                downloadPath = null; // extracted and deleted

                progress?.Report(new UpdateProgress(UpdateStage.Restarting));
                await Task.Delay(1500, CancellationToken.None);
                ApplyPortableUpdate(update, stagingPath);
            }
        }
        catch
        {
            // Never leave a half-downloaded installer/zip lying in %TEMP%; the installed copy is untouched.
            TryDelete(downloadPath);
            throw;
        }
    }

    /// <summary>
    /// Runs the new Setup.exe silently, then relaunches the app ourselves once Setup exits. Inno Setup's own
    /// restart handling only relaunches processes it saw still running, and this process exits right away, so
    /// a helper script waits on Setup's PID and relaunches explicitly.
    /// </summary>
    private static void ApplyInstallerUpdate(UpdateInfo update, string installerPath)
    {
        string exePath = Utility.GetExePath();

        Process installer = Process.Start(new ProcessStartInfo(installerPath,
            "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS")
        {
            UseShellExecute = false
        }) ?? throw new InvalidOperationException("The installer could not be started.");

        string scriptPath = Path.Combine(Path.GetTempPath(), $"RustyPainter-Update-{update.Version}.ps1");
        string script = $$"""
            Wait-Process -Id {{installer.Id}} -ErrorAction SilentlyContinue
            Start-Process -FilePath {{Quote(exePath)}} -ArgumentList '--updated'
            Remove-Item -LiteralPath {{Quote(installerPath)}} -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath {{Quote(scriptPath)}} -Force -ErrorAction SilentlyContinue
            """;

        RunHelperScript(scriptPath, script);
    }

    /// <summary>
    /// Extracts the portable zip to a staging folder and makes sure it really contains the app, so a truncated
    /// or wrong archive can never replace a working install.
    /// </summary>
    private static string ExtractPortableUpdate(UpdateInfo update, string zipPath)
    {
        string stagingPath = Path.Combine(Path.GetTempPath(), "RustyPainter-Update", update.Version);
        if (Directory.Exists(stagingPath))
            Directory.Delete(stagingPath, recursive: true);

        Directory.CreateDirectory(stagingPath);
        ZipFile.ExtractToDirectory(zipPath, stagingPath, overwriteFiles: true);
        File.Delete(zipPath);

        if (!File.Exists(Path.Combine(stagingPath, ExecutableName)))
            throw new FileNotFoundException("The downloaded update is incomplete (the application executable is missing).", ExecutableName);

        return stagingPath;
    }

    /// <summary>
    /// Hands off to a helper script that waits for this process to exit, copies the new files over the install
    /// folder and relaunches. A separate process is required because the running exe holds its own files locked.
    /// </summary>
    private static void ApplyPortableUpdate(UpdateInfo update, string stagingPath)
    {
        string exePath = Utility.GetExePath();

        string scriptPath = Path.Combine(Path.GetTempPath(), $"RustyPainter-Update-{update.Version}.ps1");
        string script = $$"""
            Wait-Process -Id {{Environment.ProcessId}} -ErrorAction SilentlyContinue
            for ($i = 0; $i -lt 10; $i++) {
                try {
                    Copy-Item -Path (Join-Path {{Quote(stagingPath)}} '*') -Destination {{Quote(InstallRegistration.InstallDirectory)}} -Recurse -Force -ErrorAction Stop
                    break
                } catch {
                    Start-Sleep -Seconds 1
                }
            }
            Remove-Item -LiteralPath {{Quote(stagingPath)}} -Recurse -Force -ErrorAction SilentlyContinue
            Start-Process -FilePath {{Quote(exePath)}} -ArgumentList '--updated'
            Remove-Item -LiteralPath {{Quote(scriptPath)}} -Force -ErrorAction SilentlyContinue
            """;

        RunHelperScript(scriptPath, script);
    }

    private static void RunHelperScript(string scriptPath, string script)
    {
        File.WriteAllText(scriptPath, script);

        Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{scriptPath}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        });

        Environment.Exit(0);
    }

    /// <summary>Fetches the latest release JSON, retrying transient network failures with a growing back-off.</summary>
    private static async Task<JsonDocument> GetLatestReleaseAsync(CancellationToken ct)
    {
        string url = $"{ApiBase}/repos/{Owner}/{Repo}/releases/latest";

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using HttpResponseMessage response = await Client.GetAsync(url, ct);
                response.EnsureSuccessStatusCode();

                await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
                return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            }
            catch (Exception ex) when (IsTransient(ex) && !ct.IsCancellationRequested)
            {
                if (attempt >= MaxAttempts)
                    throw new InvalidOperationException($"Could not reach GitHub ({ex.Message}).", ex);

                AppLog.Warn("UpdateService", $"Could not get the latest release ({ex.Message}). Retrying ({attempt}/{MaxAttempts - 1}).");
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
            }
        }
    }

    /// <summary>Picks the release asset that matches how this copy was installed.</summary>
    private static UpdateInfo? FindAsset(JsonElement release, string version, string tag)
    {
        string suffix = InstallRegistration.IsInstallerInstall()
            ? "-Setup.exe"
            : InstallRegistration.IsSelfContained() ? "-self-contained.zip" : "-framework-dependent.zip";

        foreach (JsonElement asset in release.GetProperty("assets").EnumerateArray())
        {
            string name = asset.GetProperty("name").GetString() ?? "";
            if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                continue;

            string url = asset.GetProperty("browser_download_url").GetString()
                ?? throw new InvalidOperationException($"Release asset '{name}' has no download URL.");

            long size = asset.TryGetProperty("size", out JsonElement sizeElement) ? sizeElement.GetInt64() : 0;

            // GitHub publishes "sha256:<hex>" for assets uploaded since mid-2025; older releases simply have none.
            string? sha256 = null;
            if (asset.TryGetProperty("digest", out JsonElement digestElement) &&
                digestElement.GetString() is { } digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                sha256 = digest["sha256:".Length..].ToLowerInvariant();
            }

            return new UpdateInfo(version, url, name, size, sha256);
        }

        // A newer tag without our asset (e.g. a release still being assembled) is not an error worth
        // surfacing from a background check - treat it as "nothing to install yet".
        AppLog.Warn("UpdateService", $"Release {tag} has no '*{suffix}' file to download.");
        return null;
    }

    /// <summary>
    /// Downloads to <paramref name="path"/>, restarting the stall timer on every successful read so a connection
    /// that goes quiet fails fast instead of hanging, and retrying a few times before giving up.
    /// </summary>
    private static async Task DownloadToFileAsync(UpdateInfo update, string path, IProgress<UpdateProgress>? progress, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            Exception failure;

            try
            {
                using CancellationTokenSource stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
                stall.CancelAfter(StallTimeout);

                using HttpResponseMessage response = await Client.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, stall.Token);
                response.EnsureSuccessStatusCode();

                long total = response.Content.Headers.ContentLength ?? update.Size;

                await using Stream source = await response.Content.ReadAsStreamAsync(stall.Token);
                await using FileStream target = new(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

                byte[] buffer = new byte[81920];
                long done = 0;
                long lastReport = 0;

                while (true)
                {
                    stall.CancelAfter(StallTimeout);
                    int read = await source.ReadAsync(buffer, stall.Token);
                    if (read == 0)
                        break;

                    await target.WriteAsync(buffer.AsMemory(0, read), stall.Token);
                    done += read;

                    long now = Stopwatch.GetTimestamp();
                    if (now - lastReport >= Stopwatch.Frequency / 10)
                    {
                        lastReport = now;
                        progress?.Report(new UpdateProgress(UpdateStage.Downloading, done, total));
                    }
                }

                progress?.Report(new UpdateProgress(UpdateStage.Downloading, done, total));

                if (total > 0 && done != total)
                    throw new IOException($"The download ended early ({done} of {total} bytes).");

                return;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                failure = new TimeoutException("The download stalled.");
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException && IsTransient(ex))
            {
                failure = ex;
            }

            if (attempt >= MaxAttempts)
                throw failure;

            AppLog.Warn("UpdateService", $"Download failed ({failure.Message}). Retrying ({attempt}/{MaxAttempts - 1}).");
            progress?.Report(new UpdateProgress(UpdateStage.Downloading, Retry: Loc.Format("UpdateRetrying", failure.Message, attempt, MaxAttempts - 1)));
            await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
        }
    }

    /// <summary>
    /// Compares the downloaded file against the SHA-256 GitHub published for the asset. Skipped (with a log line)
    /// when GitHub didn't provide one, e.g. for releases uploaded before digests existed.
    /// </summary>
    private static async Task VerifyDigestAsync(UpdateInfo update, string path, CancellationToken ct)
    {
        if (update.Sha256 is null)
        {
            AppLog.Warn("UpdateService", "The release has no published SHA-256, skipping integrity check.");
            return;
        }

        await using FileStream stream = File.OpenRead(path);
        string actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));

        if (!string.Equals(actual, update.Sha256, StringComparison.Ordinal))
            throw new InvalidDataException("The downloaded update failed its integrity check (checksum mismatch).");
    }

    /// <summary>
    /// Whether retrying could plausibly help: network hiccups, timeouts, truncated reads, rate limiting and
    /// server-side (5xx) errors. A 404 or 403 will not fix itself in a few seconds, so it fails immediately.
    /// </summary>
    private static bool IsTransient(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: null } => true, // DNS / connection / TLS failure
        HttpRequestException { StatusCode: { } code } => (int)code >= 500 || code is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests,
        TaskCanceledException or TimeoutException or IOException or JsonException => true,
        _ => false
    };

    private static bool TryParseCore(string version, out Version result)
    {
        int dash = version.IndexOf('-');
        string core = dash >= 0 ? version[..dash] : version;
        return Version.TryParse(core, out result!);
    }

    /// <summary>Wraps a value in a PowerShell single-quoted literal.</summary>
    private static string Quote(string value) => $"'{value.Replace("'", "''")}'";

    private static void TryDelete(string? path)
    {
        try
        {
            if (path is not null && File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        { }
        catch (UnauthorizedAccessException)
        { }
    }

    private static HttpClient CreateClient()
    {
        HttpClient client = new() { Timeout = TimeSpan.FromMinutes(2) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RustyPainter", null));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        // Use a token if one exists locally (development only) to avoid API rate limits.
        string githubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? "";
        if (!string.IsNullOrEmpty(githubToken))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", githubToken);

        return client;
    }
}
