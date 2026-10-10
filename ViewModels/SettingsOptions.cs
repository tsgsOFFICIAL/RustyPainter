using Avalonia.Svg.Skia;
using Avalonia.Platform;
using Avalonia.Media;
using RustyPainter.Interface;
using RustyPainter.Localization;
using RustyPainter.Service.Logging;
using System;

namespace RustyPainter.ViewModels;

/// <summary>
/// One entry in the language list of the Settings page. Names are the language's own, "Dansk" and not "Danish", so the list
/// reads right whichever language is in use and needs no translation of its own.
/// </summary>
/// <param name="Language">The language this row selects.</param>
/// <param name="Name">The name of the language, written in that language.</param>
/// <param name="Flag">The flag shown beside the name, or <see langword="null"/> when it could not be loaded.</param>
public sealed record LanguageOption(AppLanguage Language, string Name, IImage? Flag)
{
    /// <param name="countryCode">The two-letter country code that names the flag file, such as "dk".</param>
    public static LanguageOption Create(AppLanguage language, string name, string countryCode)
        => new(language, name, LoadFlag(countryCode));

    private static IImage? LoadFlag(string countryCode)
    {
        try
        {
            Uri uri = new($"avares://RustyPainter/Assets/Flags/{countryCode}.svg");
            if (!AssetLoader.Exists(uri))
            {
                AppLog.Warn("LanguageOption", $"The flag for '{countryCode}' is missing.");
                return null;
            }

            return new SvgImage { Source = SvgSource.Load(uri.ToString(), null) };
        }
        catch (Exception ex)
        {
            AppLog.Warn("LanguageOption", $"The flag for '{countryCode}' could not be loaded.", ex);
            return null;
        }
    }
}

/// <summary>One entry in the theme list of the Settings page. The name follows the language in use.</summary>
/// <param name="Theme">The theme this row selects.</param>
public sealed record ThemeOption(AppTheme Theme)
{
    public string Name => Loc.Get($"Theme_{Theme}");
}
