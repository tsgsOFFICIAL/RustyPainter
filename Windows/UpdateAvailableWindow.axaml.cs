using RustyPainter.ViewModels;
using RustyPainter.Interface;
using RustyPainter.Service;
using Avalonia.Controls;

namespace RustyPainter.Windows;

/// <summary>
/// A window shell hosting <see cref="Views.UpdateAvailableView"/>: title bar, offered-version content,
/// and the progress / Later / Update now footer. Prompts the user that a newer version is available, with
/// the changelog rendered inline so they can see what changed before deciding to update.
/// </summary>
public partial class UpdateAvailableWindow : Window
{
    public UpdateAvailableWindow() : this(CreateDesignViewModel()) { }

    /// <summary>Creates an initialized view model for the Avalonia previewer.</summary>
    private static UpdateAvailableViewModel CreateDesignViewModel()
        => new(new UpdateService(),
            new UpdateInfo("0.0.0", "https://example.com", "RustyPainter-0.0.0-self-contained.zip"),
            "## 0.0.0\n\n### Sample\n\n- Sample entry for the previewer.");

    /// <summary>Creates the update prompt for the given available version.</summary>
    /// <param name="updates">The update service used to apply the update if the user confirms.</param>
    /// <param name="update">The available update to offer.</param>
    /// <param name="changelog">The changes between the installed and available version, or empty if unavailable.</param>
    public UpdateAvailableWindow(IUpdateService updates, UpdateInfo update, string changelog)
        : this(new UpdateAvailableViewModel(updates, update, changelog))
    {
    }

    public UpdateAvailableWindow(UpdateAvailableViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();

        viewModel.CloseRequested += Close;
    }
}
