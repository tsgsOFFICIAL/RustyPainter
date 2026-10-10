using System;

namespace RustyPainter.Imaging;

/// <summary>The picture file kinds the app knows by their first bytes.</summary>
public enum PictureFormat
{
    Unknown,
    Png,
    Jpeg,
    WebP,
    Bmp,
    Gif
}

public static class PictureFormatDetector
{
    /// <summary>Works out what kind of picture the bytes are, whatever the file or link claims to be.</summary>
    public static PictureFormat Detect(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 8 && data[0] == 0x89 && data[1] == 'P' && data[2] == 'N' && data[3] == 'G')
            return PictureFormat.Png;

        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return PictureFormat.Jpeg;

        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8))
            return PictureFormat.WebP;

        if (data.Length >= 2 && data[0] == 'B' && data[1] == 'M')
            return PictureFormat.Bmp;

        if (data.Length >= 6 && data[..4].SequenceEqual("GIF8"u8))
            return PictureFormat.Gif;

        return PictureFormat.Unknown;
    }
}
