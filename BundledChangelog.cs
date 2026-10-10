using RustyPainter.Service.Logging;
using RustyPainter.Interface;
using System.Reflection;
using System.IO;
using System;

namespace RustyPainter;

/// <summary>The changelog that ships inside the app, for reading it without a connection.</summary>
public static class BundledChangelog
{
    /// <summary>
    /// The changelog as Markdown in a language. A version that has not been translated yet is shown in English, section by
    /// section, so a language that is a little behind never hides a release. Empty when the changelog cannot be read.
    /// </summary>
    public static string Load(AppLanguage language = AppLanguage.English)
    {
        string english = Read("CHANGELOG.md");
        if (english.Length == 0 || language == AppLanguage.English)
            return english;

        string localized = Read(FileName(language));
        return localized.Length == 0 ? english : ChangelogParser.MergeWithFallback(localized, english);
    }

    /// <summary>The name of the changelog file of a language.</summary>
    public static string FileName(AppLanguage language) => language switch
    {
        AppLanguage.Danish => "CHANGELOG.da-DK.md",
        AppLanguage.Russian => "CHANGELOG.ru-RU.md",
        _ => "CHANGELOG.md"
    };

    private static string Read(string fileName)
    {
        try
        {
            Assembly assembly = typeof(BundledChangelog).Assembly;
            string? name = Array.Find(assembly.GetManifestResourceNames(), resource => resource.EndsWith(fileName, StringComparison.OrdinalIgnoreCase)
                && (resource.Length == fileName.Length || resource[^(fileName.Length + 1)] == '.'));
            if (name is null)
                return "";

            using Stream stream = assembly.GetManifestResourceStream(name)!;
            using StreamReader reader = new(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            AppLog.Warn("BundledChangelog", $"The changelog '{fileName}' could not be read.", ex);
            return "";
        }
    }
}
