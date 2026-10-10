using RustyPainter.ViewModels.Mvvm;
using Avalonia.Controls.Templates;
using Avalonia.Controls;
using System;

namespace RustyPainter;

/// <summary>
/// Resolves a view for a <see cref="ViewModelBase"/> by replacing "ViewModels" with "Views" and
/// "ViewModel" with "View" in its full type name, e.g. "RustyPainter.ViewModels.HomeViewModel"
/// -> "RustyPainter.Views.HomeView". Used only for <c>MainWindow</c>'s swappable page content;
/// dialog windows embed their one fixed view directly instead of going through this.
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control Build(object? data)
    {
        string name = data!.GetType().FullName!
            .Replace("ViewModels", "Views")
            .Replace("ViewModel", "View");

        Type? type = Type.GetType(name);
        return type != null
            ? (Control)Activator.CreateInstance(type)!
            : new TextBlock { Text = $"View not found: {name}" };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
