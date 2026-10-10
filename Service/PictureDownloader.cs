using RustyPainter.Interface;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System;

namespace RustyPainter.Service;

/// <inheritdoc cref="IPictureDownloader" />
public sealed class PictureDownloader : IPictureDownloader
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        HttpClient client = new() { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RustyPainter");
        return client;
    }

    public async Task<byte[]> DownloadAsync(Uri link, CancellationToken cancellation = default)
    {
        if (link.Scheme != Uri.UriSchemeHttp && link.Scheme != Uri.UriSchemeHttps)
            throw new PictureDownloadException("Only http and https links can be used.");

        try
        {
            using HttpResponseMessage response = await Http.GetAsync(link, HttpCompletionOption.ResponseHeadersRead, cancellation);

            if (!response.IsSuccessStatusCode)
                throw new PictureDownloadException($"The website answered with error {(int)response.StatusCode}.");

            if (response.Content.Headers.ContentLength > IPictureDownloader.MaxBytes)
                throw TooBig();

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellation);
            using MemoryStream buffer = new();
            byte[] chunk = new byte[81920];
            int read;

            while ((read = await stream.ReadAsync(chunk, cancellation)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > IPictureDownloader.MaxBytes)
                    throw TooBig();
            }

            return buffer.ToArray();
        }
        catch (HttpRequestException)
        {
            throw new PictureDownloadException("The link could not be reached. Check the address and your connection.");
        }
        catch (TaskCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw new PictureDownloadException("The website took too long to answer.");
        }
    }

    private static PictureDownloadException TooBig() => new($"The picture is bigger than {IPictureDownloader.MaxBytes / (1024 * 1024)} MB.");
}
