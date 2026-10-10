using Avalonia.Data;
using Avalonia.Markup.Xaml;
using RustyPainter.Interface;
using RustyPainter.Service;
using RustyPainter.ViewModels.Mvvm;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System;

namespace RustyPainter.Localization;

/// <summary>
/// The way to the texts of the app from anywhere, view models and views alike. It holds the one
/// <see cref="ILocalizationService"/> and tells every living view model when the language changed, so the texts they build
/// are built again.
/// </summary>
public static class Loc
{
    private static readonly object Lock = new();
    private static readonly List<WeakReference<ViewModelBase>> Listeners = [];
    private static ILocalizationService _service = new LocalizationService();

    /// <summary>The service that holds the texts. English until the app installs the real one.</summary>
    public static ILocalizationService Service => _service;

    /// <summary>Makes a service the one the whole app uses.</summary>
    public static void Install(ILocalizationService service)
    {
        _service.PropertyChanged -= OnServiceChanged;
        _service = service;
        _service.PropertyChanged += OnServiceChanged;
        Refresh();
    }

    /// <summary>The text for a key.</summary>
    public static string Get(string key) => _service[key];

    /// <summary>The text for a key with the values put in where it has {0}, {1} and so on, written the way the language writes them.</summary>
    public static string Format(string key, params object?[] values)
        => string.Format(CultureInfo.CurrentCulture, _service[key], values);

    /// <summary>
    /// A text that depends on a number, such as "1 stroke" or "5 strokes". The catalog holds one text per form, named after
    /// the key with _one, _few, _many or _other added, and {0} in it is the number. Languages differ in how many forms they
    /// need: English and Danish have two, Russian has three, so a key without the form that is needed falls back to _other.
    /// </summary>
    public static string Count(string key, long count)
    {
        string category = PluralCategory(_service.Current, count);

        string chosen = _service.Has($"{key}_{category}") ? $"{key}_{category}"
            : _service.Has($"{key}_other") ? $"{key}_other"
            : key;

        return string.Format(CultureInfo.CurrentCulture, _service[chosen], count);
    }

    /// <summary>Which form of a text a number needs in a language: one, few, many or other.</summary>
    public static string PluralCategory(AppLanguage language, long count)
    {
        count = Math.Abs(count);

        if (language == AppLanguage.Russian)
        {
            long lastTwo = count % 100, last = count % 10;

            if (last == 1 && lastTwo != 11)
                return "one";

            return last is >= 2 and <= 4 && lastTwo is < 12 or > 14 ? "few" : "many";
        }

        return count == 1 ? "one" : "other";
    }

    /// <summary>The name of a surface in the current language, or the name it has in the data file when no text exists for it.</summary>
    public static string SurfaceName(Surface surface)
        => _service.Has($"Surface_{surface.Id}") ? _service[$"Surface_{surface.Id}"] : surface.Name;

    /// <summary>The name of a picture for showing. The text picture is saved under one fixed name and shown in the language in use.</summary>
    public static string PictureName(string name) => name == "Text" ? Get("TextPictureName") : name;

    /// <summary>Whether there is a text for the key.</summary>
    public static bool Has(string key) => _service.Has(key);

    /// <summary>Lets a view model hear about language changes for as long as it lives.</summary>
    internal static void Register(ViewModelBase viewModel)
    {
        lock (Lock)
            Listeners.Add(new WeakReference<ViewModelBase>(viewModel));
    }

    /// <summary>Tells every living view model to build its texts again.</summary>
    public static void Refresh()
    {
        List<ViewModelBase> alive = [];

        lock (Lock)
        {
            Listeners.RemoveAll(reference =>
            {
                if (reference.TryGetTarget(out ViewModelBase? viewModel))
                {
                    alive.Add(viewModel);
                    return false;
                }

                return true;
            });
        }

        foreach (ViewModelBase viewModel in alive)
            viewModel.RaiseLanguageChanged();
    }

    private static void OnServiceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ILocalizationService.Current))
            Refresh();
    }
}

/// <summary>
/// Writes a text of the app in XAML: <c>Text="{loc:L SettingsTitle}"</c>. It keeps up with the language, so the text changes
/// the moment another language is picked.
/// </summary>
public sealed class LExtension : MarkupExtension
{
    public LExtension()
    {
    }

    public LExtension(string key) => Key = key;

    /// <summary>The key of the text.</summary>
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
        => new Binding($"[{Key}]") { Source = Loc.Service, Mode = BindingMode.OneWay };
}
