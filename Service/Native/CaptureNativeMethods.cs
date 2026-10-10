using System.Runtime.InteropServices;

namespace RustyPainter.Service.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct BitmapInfoHeader
{
    public uint Size;
    public int Width;
    public int Height;
    public ushort Planes;
    public ushort BitCount;
    public uint Compression;
    public uint SizeImage;
    public int XPelsPerMeter;
    public int YPelsPerMeter;
    public uint ColorUsed;
    public uint ColorImportant;
}

/// <summary>The GDI calls that copy a rectangle of the screen into memory.</summary>
internal static class CaptureNativeMethods
{
    internal const uint SourceCopy = 0x00CC0020;
    internal const uint CaptureBlt = 0x40000000;

    [DllImport("gdi32.dll")]
    internal static extern nint CreateCompatibleDC(nint context);

    [DllImport("gdi32.dll")]
    internal static extern nint CreateCompatibleBitmap(nint context, int width, int height);

    [DllImport("gdi32.dll")]
    internal static extern nint SelectObject(nint context, nint item);

    [DllImport("gdi32.dll")]
    internal static extern bool BitBlt(nint destination, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint operation);

    [DllImport("gdi32.dll")]
    internal static extern int GetDIBits(nint context, nint bitmap, uint start, uint lines, byte[] bits, ref BitmapInfoHeader info, uint usage);

    [DllImport("gdi32.dll")]
    internal static extern bool DeleteObject(nint item);

    [DllImport("gdi32.dll")]
    internal static extern bool DeleteDC(nint context);
}
