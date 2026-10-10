using Avalonia.Controls;
using Avalonia.Input;
using RustyPainter.ViewModels;

namespace RustyPainter.Views;

public partial class LibraryView : UserControl
{
    public LibraryView()
    {
        InitializeComponent();
    }

    /// <summary>Enter keeps the new name and Escape drops it.</summary>
    private void OnRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Control { DataContext: ProjectCard card })
            return;

        if (e.Key == Key.Enter)
        {
            card.CommitRenameCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            card.CancelRenameCommand.Execute(null);
            e.Handled = true;
        }
    }
}
