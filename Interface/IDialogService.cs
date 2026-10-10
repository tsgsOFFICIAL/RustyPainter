using System.Threading.Tasks;

namespace RustyPainter.Interface;

/// <summary>
/// Shows modal windows over the main window, so view models can ask for a dialog without referencing
/// any Avalonia types.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Shows the "update available" prompt for <paramref name="update"/>, with <paramref name="changelog"/>
    /// rendered inline (pass an empty string if it couldn't be fetched). Completes when the user closes it.
    /// </summary>
    Task ShowUpdateAvailableAsync(IUpdateService updates, UpdateInfo update, string changelog);

    /// <summary>Shows the changelog that ships with the app. Completes when the user closes it.</summary>
    Task ShowChangelogAsync(string markdown);
}
