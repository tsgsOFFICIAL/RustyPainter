using RustyPainter.Interface;
using RustyPainter.Service.Logging;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System;

namespace RustyPainter.Service;

/// <inheritdoc cref="ILocalizationService" />
public sealed class LocalizationService : ILocalizationService
{
    private const string IndexerName = "Item";
    private const string IndexerArrayName = "Item[]";

    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();

    private readonly IAppSettingsService? _settings;
    private readonly Dictionary<AppLanguage, IReadOnlyDictionary<string, string>> _catalogs = [];
    private IReadOnlyDictionary<string, string> _current = Empty;
    private IReadOnlyDictionary<string, string> _fallback = Empty;
    private bool _initialized;

    /// <param name="settings">Where the chosen language is kept. Without it the language is English and is not remembered.</param>
    public LocalizationService(IAppSettingsService? settings = null) => _settings = settings;

    public AppLanguage Current { get; private set; } = AppLanguage.English;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key]
    {
        get
        {
            Initialize();

            if (_current.TryGetValue(key, out string? text))
                return text;

            return _fallback.TryGetValue(key, out string? english) ? english : key;
        }
    }

    public bool Has(string key)
    {
        Initialize();
        return _fallback.ContainsKey(key) || _current.ContainsKey(key);
    }

    public void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;

        foreach (AppLanguage language in Enum.GetValues<AppLanguage>())
            _catalogs[language] = LoadCatalog(language);

        _fallback = Catalog(AppLanguage.English);
        Current = _settings is null ? AppLanguage.English : _settings.Current.Language ?? DetectSystemLanguage();
        Apply(Current);
        Notify();
    }

    public void SetLanguage(AppLanguage language, bool save = true)
    {
        Initialize();

        Current = language;
        Apply(language);

        if (save && _settings is not null)
        {
            _settings.Current.Language = language;
            _settings.Save();
        }

        Notify();
    }

    /// <summary>The name of the catalog file of a language, without the extension.</summary>
    public static string FileName(AppLanguage language) => language switch
    {
        AppLanguage.Danish => "da-DK",
        AppLanguage.Russian => "ru-RU",
        _ => "en"
    };

    /// <summary>The culture of a language, for the way numbers and dates are written.</summary>
    public static CultureInfo CultureOf(AppLanguage language) => language switch
    {
        AppLanguage.Danish => new CultureInfo("da-DK"),
        AppLanguage.Russian => new CultureInfo("ru-RU"),
        _ => new CultureInfo("en")
    };

    /// <summary>The language that fits the language of Windows best, or English.</summary>
    public static AppLanguage DetectSystemLanguage()
    {
        string name = CultureInfo.CurrentUICulture.Name;

        if (name.StartsWith("da", StringComparison.OrdinalIgnoreCase))
            return AppLanguage.Danish;

        return name.StartsWith("ru", StringComparison.OrdinalIgnoreCase) ? AppLanguage.Russian : AppLanguage.English;
    }

    private void Apply(AppLanguage language)
    {
        _current = Catalog(language);

        // The texts come from the catalog, but numbers and dates follow the culture, so 1.5 is written 1,5 in Danish.
        CultureInfo culture = CultureOf(language);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
    }

    private IReadOnlyDictionary<string, string> Catalog(AppLanguage language)
        => _catalogs.TryGetValue(language, out IReadOnlyDictionary<string, string>? catalog) ? catalog : Empty;

    private static IReadOnlyDictionary<string, string> LoadCatalog(AppLanguage language)
    {
        string resource = $"Lang.{FileName(language)}.json";

        try
        {
            using Stream? stream = typeof(LocalizationService).Assembly.GetManifestResourceStream(resource);
            if (stream is null)
            {
                AppLog.Warn("LocalizationService", $"The texts {resource} are missing from the app.");
                return Empty;
            }

            using StreamReader reader = new(stream);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd()) ?? [];
        }
        catch (Exception ex)
        {
            AppLog.Warn("LocalizationService", $"The texts {resource} could not be read.", ex);
            return Empty;
        }
    }

    private void Notify()
    {
        // Avalonia refreshes {Binding [Key]} paths when it hears about Item or Item[].
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(IndexerName));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(IndexerArrayName));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Current)));
    }
}
