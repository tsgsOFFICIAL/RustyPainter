using System.ComponentModel;

namespace RustyPainter.Interface;

/// <summary>The languages the app speaks.</summary>
public enum AppLanguage
{
    English,
    Danish,
    Russian
}

/// <summary>The looks the app can have. System follows Windows.</summary>
public enum AppTheme
{
    System,
    Light,
    Dark
}

/// <summary>Holds the texts of the app in every language and says which one is used now.</summary>
public interface ILocalizationService : INotifyPropertyChanged
{
    /// <summary>The language in use.</summary>
    AppLanguage Current { get; }

    /// <summary>
    /// The text for a key in the language in use. A key that has no text in that language gets its English text, and a key that
    /// is not known at all comes back as itself, so a mistake shows up on screen instead of as a crash.
    /// </summary>
    string this[string key] { get; }

    /// <summary>Whether the key has a text.</summary>
    bool Has(string key);

    /// <summary>Loads the texts and starts in the saved language, or the language of Windows when none was saved.</summary>
    void Initialize();

    /// <summary>Changes the language at once, and remembers it unless told not to.</summary>
    void SetLanguage(AppLanguage language, bool save = true);
}
