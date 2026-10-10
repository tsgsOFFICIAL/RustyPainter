using RustyPainter.ViewModels;
using Avalonia.Controls;

namespace RustyPainter.Windows;

/// <summary>Shows the changelog that ships with the app.</summary>
public partial class ChangelogWindow : Window
{
    public ChangelogWindow() : this(new ChangelogViewModel("## 0.0.0\n\n- Sample entry for the previewer."))
    {
    }

    public ChangelogWindow(ChangelogViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();

        viewModel.CloseRequested += Close;
    }
}
