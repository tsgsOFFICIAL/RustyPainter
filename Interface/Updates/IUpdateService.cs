using System.Threading.Tasks;
using System.Threading;
using System;

namespace RustyPainter.Interface;

/// <summary>
/// Describes an available update: the version being offered, and the release asset to download.
/// </summary>
/// <param name="Version">The offered version, without a leading "v".</param>
/// <param name="DownloadUrl">The release asset's download URL.</param>
/// <param name="AssetName">The release asset's file name (decides installer vs. portable apply).</param>
/// <param name="Size">The asset size in bytes as reported by GitHub, or 0 if unknown.</param>
/// <param name="Sha256">The asset's SHA-256 as published by GitHub (lower-case hex), or <see langword="null"/> if GitHub didn't provide one.</param>
public sealed record UpdateInfo(string Version, string DownloadUrl, string AssetName, long Size = 0, string? Sha256 = null);

/// <summary>The phase an update is currently in.</summary>
public enum UpdateStage
{
    /// <summary>The release asset is being downloaded.</summary>
    Downloading,

    /// <summary>The download finished and is being checked / unpacked.</summary>
    Verifying,

    /// <summary>The update is ready and the app is about to close and relaunch.</summary>
    Restarting
}

/// <summary>
/// A snapshot of update progress reported while <see cref="IUpdateService.ApplyUpdateAsync"/> runs.
/// </summary>
/// <param name="Stage">The current phase.</param>
/// <param name="BytesDone">Bytes downloaded so far (only meaningful while <see cref="UpdateStage.Downloading"/>).</param>
/// <param name="BytesTotal">Total bytes to download, or 0 if unknown.</param>
/// <param name="Retry">When set, a one-line explanation of why the current step is being retried.</param>
public readonly record struct UpdateProgress(UpdateStage Stage, long BytesDone = 0, long BytesTotal = 0, string? Retry = null)
{
    /// <summary>Download completion, 0-100, or 0 when the total size is unknown.</summary>
    public int Percent => BytesTotal > 0 ? (int)Math.Clamp(BytesDone * 100 / BytesTotal, 0, 100) : 0;
}

/// <summary>
/// Checks GitHub Releases for a newer version and applies it in place.
/// </summary>
public interface IUpdateService
{
    /// <summary>
    /// Queries the latest GitHub release and returns update info if it's newer than the running version,
    /// or <see langword="null"/> if already up to date.
    /// </summary>
    Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default);

    /// <summary>
    /// Downloads the release asset for <paramref name="update"/>, verifies it, and swaps it in over the
    /// current install once the app exits. Never partially applies an update: if anything fails before the
    /// hand-off the installed copy is left untouched and the exception propagates. Terminates the current
    /// process on success.
    /// </summary>
    Task ApplyUpdateAsync(UpdateInfo update, IProgress<UpdateProgress>? progress = null, CancellationToken ct = default);
}
