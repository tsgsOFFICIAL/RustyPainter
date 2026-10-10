using RustyPainter.Service.Logging;
using System.Reflection;
using System.IO;
using System;

namespace RustyPainter;

/// <summary>The changelog that ships inside the app, for reading it without a connection.</summary>
public static class BundledChangelog
{
    /// <summary>The whole changelog as Markdown, or a short note when it cannot be read.</summary>
    public static string Load()
    {
        try
        {
            Assembly assembly = typeof(BundledChangelog).Assembly;
            string? name = Array.Find(assembly.GetManifestResourceNames(), resource => resource.EndsWith("CHANGELOG.md", StringComparison.OrdinalIgnoreCase));
            if (name is null)
                return "";

            using Stream stream = assembly.GetManifestResourceStream(name)!;
            using StreamReader reader = new(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            AppLog.Warn("BundledChangelog", "The changelog could not be read.", ex);
            return "";
        }
    }
}
