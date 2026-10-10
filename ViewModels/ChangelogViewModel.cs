using RustyPainter.ViewModels.Mvvm;
using System;

namespace RustyPainter.ViewModels;

/// <summary>
/// Backs the changelog window. <see cref="CloseRequested"/> lets the window close itself without this view model
/// referencing it.
/// </summary>
public sealed class ChangelogViewModel : ViewModelBase
{
    public ChangelogViewModel(string markdown)
    {
        Markdown = markdown;
        CloseCommand = new RelayCommand(() => CloseRequested?.Invoke());
    }

    public event Action? CloseRequested;

    /// <summary>The changelog as Markdown. Empty when there is none.</summary>
    public string Markdown { get; }

    public bool HasMarkdown => Markdown.Trim().Length > 0;

    public RelayCommand CloseCommand { get; }
}
