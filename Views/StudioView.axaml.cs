using Avalonia.Platform.Storage;
using RustyPainter.ViewModels;
using Avalonia.Interactivity;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Input;
using System.Linq;
using System;

namespace RustyPainter.Views;

/// <summary>
/// The Studio page. The file picker, drag and drop and paste are UI platform features, so they live
/// here and hand the result to <see cref="StudioViewModel"/>.
/// </summary>
public partial class StudioView : UserControl
{
    private TopLevel? _topLevel;

    public StudioView()
    {
        InitializeComponent();

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private StudioViewModel? ViewModel => DataContext as StudioViewModel;

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Ctrl+V works anywhere on the page, so listen on the window rather than on a focused control.
        _topLevel = TopLevel.GetTopLevel(this);
        _topLevel?.AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _topLevel?.RemoveHandler(KeyDownEvent, OnWindowKeyDown);
        _topLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel || ViewModel is not { } viewModel)
            return;

        FilePickerFileType pictures = new("Pictures")
        {
            Patterns = StudioViewModel.SupportedExtensions.Select(extension => "*" + extension).ToArray()
        };

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a picture",
            AllowMultiple = false,
            FileTypeFilter = [pictures]
        });

        if (files.Count == 0)
            return;

        if (files[0].TryGetLocalPath() is { } path)
            viewModel.TryLoad(path);
        else
            viewModel.ReportNothingToLoad();
    }

    private void OnCategoryClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && sender is Button { Tag: string category })
            viewModel.SelectedCategory = category;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.TryGetFiles() is { Length: > 0 } ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (ViewModel is not { } viewModel)
            return;

        string? path = e.DataTransfer.TryGetFiles()?.Select(file => file.TryGetLocalPath()).FirstOrDefault(p => p is not null);

        if (path is null)
            viewModel.ReportNothingToLoad();
        else
            viewModel.TryLoad(path);

        e.Handled = true;
    }

    private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.V || !e.KeyModifiers.HasFlag(KeyModifiers.Control) || ViewModel is not { } viewModel)
            return;

        // A text box that has focus should still be able to paste text normally.
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox)
            return;

        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            return;

        e.Handled = true;

        IAsyncDataTransfer? data = await clipboard.TryGetDataAsync();
        if (data is null)
        {
            viewModel.ReportNothingToLoad();
            return;
        }

        using (data)
        {
            var files = await data.TryGetFilesAsync();
            if (files?.Select(file => file.TryGetLocalPath()).FirstOrDefault(p => p is not null) is { } path)
            {
                viewModel.TryLoad(path);
                return;
            }

            Bitmap? bitmap = await data.TryGetBitmapAsync();
            if (bitmap is not null)
                viewModel.Load(bitmap, "Pasted picture");
            else
                viewModel.ReportNothingToLoad();
        }
    }
}
