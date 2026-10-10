using RustyPainter.ViewModels.Mvvm;

namespace RustyPainter.ViewModels;

/// <summary>
/// Placeholder shown for pages that aren't built yet (Studio, Library). Retitled per page by the shell.
/// </summary>
public sealed class ComingSoonViewModel : ViewModelBase
{
    private string _title = "";

    public string Title { get => _title; set => SetProperty(ref _title, value); }
}
