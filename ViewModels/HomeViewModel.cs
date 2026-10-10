using RustyPainter.ViewModels.Mvvm;
using RustyPainter.Controls;

namespace RustyPainter.ViewModels;

/// <summary>
/// The landing page. For now just the hero and shortcuts into Studio; the image / URL / text source
/// tiles from the design mockup land here once Studio exists.
/// </summary>
public sealed class HomeViewModel : ViewModelBase
{
    public HomeViewModel(SidebarViewModel sidebar)
    {
        OpenStudioCommand = new RelayCommand(() => sidebar.NavigateTo(SidebarPage.Studio));
    }

    public RelayCommand OpenStudioCommand { get; }
}
