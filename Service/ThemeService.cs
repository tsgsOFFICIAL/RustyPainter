using Avalonia;
using Avalonia.Styling;
using RustyPainter.Interface;
using RustyPainter.Service.Logging;
using System;

namespace RustyPainter.Service;

/// <inheritdoc cref="IThemeService" />
public sealed class ThemeService(IAppSettingsService settings) : IThemeService
{
    public AppTheme Current { get; private set; } = AppTheme.System;

    public ThemeVariant ActualVariant => Application.Current?.ActualThemeVariant ?? ThemeVariant.Default;

    public void Initialize()
    {
        Current = settings.Current.Theme;
        Apply(Current);
    }

    public void SetTheme(AppTheme theme)
    {
        Current = theme;
        Apply(theme);

        try
        {
            settings.Current.Theme = theme;
            settings.Save();
        }
        catch (Exception ex)
        {
            AppLog.Warn("ThemeService", "The theme could not be saved.", ex);
        }
    }

    /// <summary>The variant the app asks Avalonia for. Default means Windows decides.</summary>
    public static ThemeVariant VariantOf(AppTheme theme) => theme switch
    {
        AppTheme.Light => ThemeVariant.Light,
        AppTheme.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default
    };

    private static void Apply(AppTheme theme)
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = VariantOf(theme);
    }
}
