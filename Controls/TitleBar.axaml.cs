using IconPacks.Avalonia.PhosphorIcons;
using Avalonia.Interactivity;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia;
using System;

namespace RustyPainter.Controls;

/// <summary>
/// A reusable custom title bar that replaces the native window chrome with a themed header
/// containing an icon, title, and window control buttons. Only takes effect on Windows; on other
/// platforms it stays hidden and the native OS title bar/decorations are used instead.
/// </summary>
public partial class TitleBar : UserControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<TitleBar, string?>(nameof(Title));

    public static readonly StyledProperty<object?> IconProperty =
        AvaloniaProperty.Register<TitleBar, object?>(nameof(Icon));

    public static readonly StyledProperty<bool> ShowMaximizeButtonProperty =
        AvaloniaProperty.Register<TitleBar, bool>(nameof(ShowMaximizeButton), defaultValue: true);

    /// <summary>
    /// Whether the icon/title/author/version block on the left is shown. Defaults to true;
    /// windows that already display branding elsewhere (e.g. MainWindow's sidebar logo) should
    /// set this false instead of just leaving Title/Icon unset, so the title bar doesn't reserve
    /// an empty gap for a branding block nobody bound.
    /// </summary>
    public static readonly StyledProperty<bool> ShowBrandingProperty =
        AvaloniaProperty.Register<TitleBar, bool>(nameof(ShowBranding), defaultValue: true);

    public static readonly StyledProperty<string?> AuthorProperty =
        AvaloniaProperty.Register<TitleBar, string?>(nameof(Author));

    public static readonly StyledProperty<string?> VersionTextProperty =
        AvaloniaProperty.Register<TitleBar, string?>(nameof(VersionText), defaultValue: GetDefaultVersionText());

    private Window? _window;

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public object? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public bool ShowMaximizeButton
    {
        get => GetValue(ShowMaximizeButtonProperty);
        set => SetValue(ShowMaximizeButtonProperty, value);
    }

    public bool ShowBranding
    {
        get => GetValue(ShowBrandingProperty);
        set => SetValue(ShowBrandingProperty, value);
    }

    /// <summary>
    /// The attribution text shown under the title as "by {Author}". Left unset (null), nothing is shown.
    /// </summary>
    public string? Author
    {
        get => GetValue(AuthorProperty);
        set => SetValue(AuthorProperty, value);
    }

    /// <summary>
    /// The version number shown next to the title (e.g. "0.4.0"), without a "Version" prefix so
    /// the caller can localize that word separately. Defaults to the entry assembly's version.
    /// </summary>
    public string? VersionText
    {
        get => GetValue(VersionTextProperty);
        set => SetValue(VersionTextProperty, value);
    }

    private static string? GetDefaultVersionText()
    {
        string version = Utility.GetDisplayVersion();
        return version is "N/A" ? null : version;
    }

    public TitleBar()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is null)
            return;

        if (!OperatingSystem.IsWindows())
        {
            IsVisible = false;
            return;
        }

        // BorderOnly (not None) keeps the native resize border/hit-testing at the window edges
        // while still dropping the native title bar and caption buttons. Extending the client
        // area into the decorations (with a height hint of 0, not -1/"default") tells Windows
        // there is no title-bar strip to reserve, which removes the leftover 1px accent line
        // it otherwise draws along the top of a BorderOnly window.
        _window.WindowDecorations = WindowDecorations.BorderOnly;
        _window.ExtendClientAreaToDecorationsHint = true;
        _window.ExtendClientAreaTitleBarHeightHint = 0;

        UpdateMaximizeRestoreIcon(_window.WindowState);
        _window.PropertyChanged += OnWindowPropertyChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_window is not null)
            _window.PropertyChanged -= OnWindowPropertyChanged;

        _window = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty)
            UpdateMaximizeRestoreIcon((WindowState)e.NewValue!);
    }

    private void UpdateMaximizeRestoreIcon(WindowState state)
        => MaximizeRestoreIcon.Kind = state == WindowState.Maximized
            ? PackIconPhosphorIconsKind.CopySimple
            : PackIconPhosphorIconsKind.Square;

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_window is null)
            return;

        if (e.ClickCount == 2)
        {
            ToggleMaximizeRestore();
            return;
        }

        _window.BeginMoveDrag(e);
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        if (_window is not null)
            _window.WindowState = WindowState.Minimized;
    }

    private void OnMaximizeRestoreClick(object? sender, RoutedEventArgs e)
        => ToggleMaximizeRestore();

    private void OnCloseClick(object? sender, RoutedEventArgs e)
        => _window?.Close();

    private void ToggleMaximizeRestore()
    {
        if (_window is null)
            return;

        _window.WindowState = _window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }
}