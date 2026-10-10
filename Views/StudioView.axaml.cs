using Avalonia.Platform.Storage;
using RustyPainter.ViewModels;
using Avalonia.Interactivity;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using Avalonia;
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

        if (ViewModel is { } viewModel && this.TryFindResource("Montserrat", out object? font) && font is FontFamily family)
            viewModel.TextFont = family;
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

    // ---- moving and zooming the picture on the canvas ----

    private Point? _dragFrom;

    private void OnStagePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control stage || ViewModel is not { ShowResult: true } || !e.GetCurrentPoint(stage).Properties.IsLeftButtonPressed)
            return;

        _dragFrom = e.GetPosition(stage);
        e.Pointer.Capture(stage);
        e.Handled = true;
    }

    private void OnStageMoved(object? sender, PointerEventArgs e)
    {
        if (_dragFrom is not { } from || sender is not Control stage || ViewModel is not { } viewModel)
            return;

        // The stage is drawn inside a Viewbox, so positions are already in canvas units whatever size it is shown at.
        Point now = e.GetPosition(stage);
        if (stage.Bounds.Width > 0 && stage.Bounds.Height > 0)
            viewModel.MoveBy((now.X - from.X) / stage.Bounds.Width, (now.Y - from.Y) / stage.Bounds.Height);

        _dragFrom = now;
    }

    private void OnStageReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragFrom = null;
        e.Pointer.Capture(null);
    }

    private void OnStageCaptureLost(object? sender, PointerCaptureLostEventArgs e) => _dragFrom = null;

    private void OnStageWheel(object? sender, PointerWheelEventArgs e)
    {
        if (ViewModel is not { ShowResult: true } viewModel)
            return;

        viewModel.ZoomBy(e.Delta.Y > 0 ? 1.1 : 1 / 1.1);
        e.Handled = true;
    }

    private void OnStageDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel is { ShowResult: true } viewModel)
            viewModel.ResetView();
    }

    private void OnLinkKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ViewModel is { } viewModel && viewModel.LoadLinkCommand.CanExecute(null))
        {
            viewModel.LoadLinkCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnCategoryClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && sender is Button { Tag: string category })
            viewModel.SelectedCategory = category;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        bool usable = e.DataTransfer.TryGetFiles() is { Length: > 0 } || e.DataTransfer.TryGetText() is { Length: > 0 };
        e.DragEffects = usable ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (ViewModel is not { } viewModel)
            return;

        string? path = e.DataTransfer.TryGetFiles()?.Select(file => file.TryGetLocalPath()).FirstOrDefault(p => p is not null);

        if (path is not null)
            viewModel.TryLoad(path);
        else if (e.DataTransfer.TryGetText() is { Length: > 0 } text)
            _ = viewModel.TryLoadLinkAsync(text);
        else
            viewModel.ReportNothingToLoad();

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
            {
                viewModel.Load(bitmap, "Pasted picture");
                return;
            }

            if (await data.TryGetTextAsync() is { Length: > 0 } text)
                await viewModel.TryLoadLinkAsync(text);
            else
                viewModel.ReportNothingToLoad();
        }
    }
}
