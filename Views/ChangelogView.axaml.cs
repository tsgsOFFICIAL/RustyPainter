using RustyPainter.ViewModels;
using Avalonia.Controls;

namespace RustyPainter.Views;

/// <summary>Draws the changelog Markdown into the content host.</summary>
public partial class ChangelogView : UserControl
{
    public ChangelogView()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is ChangelogViewModel { HasMarkdown: true } viewModel)
                ChangelogHost.Content = MarkdownRenderer.Render(viewModel.Markdown);
        };
    }
}
