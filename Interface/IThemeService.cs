using Avalonia.Styling;

namespace RustyPainter.Interface;

/// <summary>Chooses whether the app is light or dark.</summary>
public interface IThemeService
{
    /// <summary>The choice that was made, which may be to follow Windows.</summary>
    AppTheme Current { get; }

    /// <summary>What the app looks like right now.</summary>
    ThemeVariant ActualVariant { get; }

    /// <summary>Applies the saved choice.</summary>
    void Initialize();

    /// <summary>Applies a choice at once and remembers it.</summary>
    void SetTheme(AppTheme theme);
}
