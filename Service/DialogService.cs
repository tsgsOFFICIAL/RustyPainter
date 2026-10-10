using Avalonia.Controls.ApplicationLifetimes;
using RustyPainter.Interface;
using System.Threading.Tasks;
using RustyPainter.Windows;
using Avalonia.Controls;
using Avalonia;

namespace RustyPainter.Service;

/// <inheritdoc cref="IDialogService" />
public sealed class DialogService : IDialogService
{
    public async Task ShowUpdateAvailableAsync(IUpdateService updates, UpdateInfo update, string changelog)
    {
        if (GetOwner() is not { } owner)
            return;

        UpdateAvailableWindow window = new(updates, update, changelog);
        await window.ShowDialog(owner);
    }

    private static Window? GetOwner()
        => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
}
