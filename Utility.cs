using RustyPainter.Service.Logging;
using System.Diagnostics;
using System.Reflection;
using System.IO;
using System;

namespace RustyPainter;

/// <summary>
/// The project's external links, shared by anywhere in the UI that offers to open them
/// (the About section, the main window's footer).
/// </summary>
public static class ProjectLinks
{
    public const string GitHub = "https://github.com/tsgsOFFICIAL/RustyPainter";
    public const string Discord = "https://discord.gg/Cddu5aJ";
    public const string KoFi = "https://ko-fi.com/tsgsOFFICIAL";
}

/// <summary>
/// Helpers shared across the application.
/// </summary>
public static class Utility
{
    /// <summary>
    /// Gets the user-facing application version string, including nightly or prerelease suffixes when present.
    /// </summary>
    /// <returns>The product version (includes nightly/prerelease suffixes), falling back to the plain file version.</returns>
    public static string GetDisplayVersion()
    {
        string assemblyPath = Assembly.GetExecutingAssembly().Location;
        FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(assemblyPath);
        // ProductVersion (InformationalVersion) carries the nightly commit-hash suffix; FileVersion
        // is strictly numeric and never has it. See .github/workflows/nightly.yml.
        return versionInfo.ProductVersion ?? versionInfo.FileVersion ?? "N/A";
    }

    /// <summary>
    /// Gets the full path to the running application executable.
    /// </summary>
    public static string GetExePath()
    {
        return Environment.ProcessPath
            ?? Path.Combine(AppContext.BaseDirectory, $"{Path.GetFileNameWithoutExtension(Environment.GetCommandLineArgs()[0])}.exe");
    }

    /// <summary>
    /// Gets a marketing-style Windows name (e.g. "Windows 11"). Windows 11 reports the same major
    /// version (10) as Windows 10; only the build number (&gt;= 22000) actually distinguishes them.
    /// </summary>
    public static string GetFriendlyOsName()
    {
        Version v = Environment.OSVersion.Version;
        return v is { Major: 10, Build: >= 22000 } ? "Windows 11" : v.Major == 10 ? "Windows 10" : $"Windows NT {v}";
    }

    /// <summary>
    /// Opens the given URL or folder using the operating system's default handler. Fails silently
    /// (e.g. no default browser configured) so a broken link never crashes the app.
    /// </summary>
    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Warn("Utility", $"Failed to open '{url}'.", ex);
        }
    }
}
