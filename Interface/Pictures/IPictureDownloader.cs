using System.Threading;
using System.Threading.Tasks;
using System;

namespace RustyPainter.Interface;

/// <summary>Thrown when a picture could not be fetched. The message is written for the user.</summary>
public sealed class PictureDownloadException(string message) : Exception(message);

/// <summary>Fetches the bytes of a picture from a web link.</summary>
public interface IPictureDownloader
{
    /// <summary>The largest picture that is accepted, in bytes.</summary>
    const int MaxBytes = 25 * 1024 * 1024;

    /// <exception cref="PictureDownloadException">The link could not be reached, answered with an error or was too big.</exception>
    Task<byte[]> DownloadAsync(Uri link, CancellationToken cancellation = default);
}
