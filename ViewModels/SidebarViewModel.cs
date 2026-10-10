using RustyPainter.ViewModels.Mvvm;
using RustyPainter.Controls;
using System;

namespace RustyPainter.ViewModels;

/// <summary>
/// Drives the sidebar: which page is currently selected.
/// </summary>
public sealed class SidebarViewModel : ViewModelBase
{
    private SidebarPage _activePage = SidebarPage.Home;

    public SidebarViewModel()
    {
        NavigateCommand = new RelayCommand<string>(Navigate);
    }

    /// <summary>Raised when the user selects a different page from the nav list.</summary>
    public event EventHandler<SidebarPage>? NavigationRequested;

    /// <summary>Navigates to the page named by its parameter.</summary>
    public RelayCommand<string> NavigateCommand { get; }

    /// <summary>The page currently selected in the nav list.</summary>
    public SidebarPage ActivePage
    {
        get => _activePage;
        private set => SetProperty(ref _activePage, value);
    }

    /// <summary>Marks a page as the selected one without navigating, for when a page change was refused.</summary>
    public void Highlight(SidebarPage page) => ActivePage = page;

    /// <summary>
    /// Programmatically selects a page, the same way clicking its nav button would - used by
    /// in-page links that jump elsewhere without going through the nav rail itself.
    /// </summary>
    public void NavigateTo(SidebarPage page)
    {
        ActivePage = page;
        NavigationRequested?.Invoke(this, page);
    }

    private void Navigate(string? tag)
    {
        if (Enum.TryParse(tag, out SidebarPage page))
            NavigateTo(page);
    }
}
