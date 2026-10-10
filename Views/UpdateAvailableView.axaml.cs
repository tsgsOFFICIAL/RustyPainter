using RustyPainter.ViewModels;
using Avalonia.Controls;

namespace RustyPainter.Views;

/// <summary>
/// Renders the offered version's changelog Markdown into the content host, when there is one.
/// <see cref="MarkdownRenderer.Render"/> produces a raw Avalonia control tree from a plain string,
/// so this stays a code-behind concern rather than something bindable directly.
/// </summary>
public partial class UpdateAvailableView : UserControl
{
    public UpdateAvailableView()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is UpdateAvailableViewModel { HasChangelog: true } viewModel)
                ChangelogHost.Content = MarkdownRenderer.Render(viewModel.Changelog);
        };
    }
}
