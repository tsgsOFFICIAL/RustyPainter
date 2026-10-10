using Avalonia.Platform.Storage;
using RustyPainter.ViewModels;
using Avalonia.Interactivity;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using Avalonia.Threading;
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
        WireGesture();
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

    // The picture is moved and zoomed by transforming the render that is already shown, so dragging stays smooth.
    // The real settings change once, when the gesture ends, and the next render replaces the moved one.

    private readonly PlacementGesture _gesture = new();
    private readonly MatrixTransform _gestureTransform = new();
    private readonly DispatcherTimer _wheelTimer = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private Point? _dragFrom;
    private StudioViewModel? _watched;

    private void WireGesture()
    {
        foreach (Image image in new[] { PreviewImage, ReferenceImage })
        {
            image.RenderTransform = _gestureTransform;
            image.RenderTransformOrigin = RelativePoint.TopLeft;
        }

        _wheelTimer.Tick += (_, _) =>
        {
            _wheelTimer.Stop();
            CommitGesture();
        };

        DataContextChanged += (_, _) =>
        {
            if (_watched is not null)
                _watched.PropertyChanged -= OnViewModelChanged;

            _watched = ViewModel;
            if (_watched is not null)
                _watched.PropertyChanged += OnViewModelChanged;

            EndGesture();
        };

        Stage.LayoutUpdated += (_, _) => UpdateSplitOverlayScale();
    }

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // A new render arrived. If it already holds everything the gesture changed, the moved copy is not needed any more.
        // If the mouse is still busy, the render holds what was committed so far and the picture only moves by the rest.
        if (e.PropertyName is not (nameof(StudioViewModel.Preview) or nameof(StudioViewModel.Reference))
            || sender is not StudioViewModel viewModel || !_gesture.IsActive)
            return;

        if (_dragFrom is null && !_gesture.HasUncommitted)
        {
            EndGesture();
            return;
        }

        _gesture.Rebase(viewModel.Zoom, viewModel.OffsetX, viewModel.OffsetY);
        ShowGesture();
    }

    private void EndGesture()
    {
        _gesture.Reset();
        _gestureTransform.Matrix = Matrix.Identity;
    }

    private void ShowGesture()
    {
        System.Numerics.Matrix3x2 m = _gesture.Matrix;
        _gestureTransform.Matrix = new Matrix(m.M11, m.M12, m.M21, m.M22, m.M31, m.M32);
    }

    private void BeginGesture(StudioViewModel viewModel)
        => _gesture.Begin(viewModel.CanvasWidth, viewModel.CanvasHeight, viewModel.Zoom, viewModel.OffsetX, viewModel.OffsetY);

    /// <summary>Hands what the gesture changed to the settings. They render once and the moved picture is replaced.</summary>
    private void CommitGesture()
    {
        if (ViewModel is not { } viewModel || !_gesture.HasUncommitted)
            return;

        viewModel.Zoom = _gesture.Zoom;
        viewModel.OffsetX = _gesture.OffsetXPercent;
        viewModel.OffsetY = _gesture.OffsetYPercent;
        _gesture.MarkCommitted();

        // Nothing to render means nothing will replace the moved picture, so drop it now.
        if (!viewModel.IsBusy)
            EndGesture();
    }

    private void OnStagePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control stage || ViewModel is not { ShowResult: true } viewModel || !e.GetCurrentPoint(stage).Properties.IsLeftButtonPressed)
            return;

        BeginGesture(viewModel);
        _dragFrom = e.GetPosition(stage);
        e.Pointer.Capture(stage);
        e.Handled = true;
    }

    private void OnStageMoved(object? sender, PointerEventArgs e)
    {
        if (_dragFrom is not { } from || sender is not Control stage)
            return;

        // The stage is drawn inside a Viewbox, so positions are already in canvas units whatever size it is shown at.
        Point now = e.GetPosition(stage);
        _gesture.Pan(now.X - from.X, now.Y - from.Y);
        ShowGesture();
        _dragFrom = now;
    }

    private void OnStageReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragFrom = null;
        e.Pointer.Capture(null);
        CommitGesture();
    }

    private void OnStageCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_dragFrom is not null)
        {
            _dragFrom = null;
            CommitGesture();
        }
    }

    private void OnStageWheel(object? sender, PointerWheelEventArgs e)
    {
        if (ViewModel is not { ShowResult: true } viewModel)
            return;

        BeginGesture(viewModel);
        _gesture.ZoomBy(e.Delta.Y > 0 ? 1.1 : 1 / 1.1);
        ShowGesture();

        // Commit once the wheel has stopped, not for every notch.
        _wheelTimer.Stop();
        _wheelTimer.Start();
        e.Handled = true;
    }

    private void OnStageDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel is { ShowResult: true } viewModel)
            viewModel.ResetView();
    }

    // ---- the divider of the split view ----

    private bool _splitDragging;

    private void OnSplitPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control strip || !e.GetCurrentPoint(strip).Properties.IsLeftButtonPressed)
            return;

        _splitDragging = true;
        e.Pointer.Capture(strip);
        e.Handled = true;
        MoveSplit(e);
    }

    private void OnSplitMoved(object? sender, PointerEventArgs e)
    {
        if (_splitDragging)
            MoveSplit(e);
    }

    private void OnSplitReleased(object? sender, PointerReleasedEventArgs e)
    {
        _splitDragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnSplitCaptureLost(object? sender, PointerCaptureLostEventArgs e) => _splitDragging = false;

    private void MoveSplit(PointerEventArgs e)
    {
        if (ViewModel is { } viewModel && Stage.Bounds.Width > 0)
            viewModel.SplitPosition = e.GetPosition(Stage).X / Stage.Bounds.Width;
    }

    /// <summary>Keeps the handle and labels the same size on screen however much the canvas is scaled to fit.</summary>
    private void UpdateSplitOverlayScale()
    {
        if (Stage.TransformToVisual(this) is not { } matrix || matrix.M11 <= 0)
            return;

        double inverse = 1 / matrix.M11;
        if (Math.Abs(inverse - _overlayInverse) < 0.001)
            return;

        _overlayInverse = inverse;
        SplitStrip.Width = 34 * inverse;
        SplitStrip.Margin = new Thickness(0, 0, -17 * inverse, 0);
        SplitLine.Width = 2 * inverse;

        SplitHandle.RenderTransform = new ScaleTransform(inverse, inverse);
        PictureLabel.RenderTransformOrigin = new RelativePoint(0, 1, RelativeUnit.Relative);
        PictureLabel.RenderTransform = new ScaleTransform(inverse, inverse);
        ResultLabel.RenderTransformOrigin = new RelativePoint(1, 1, RelativeUnit.Relative);
        ResultLabel.RenderTransform = new ScaleTransform(inverse, inverse);
    }

    private double _overlayInverse;

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
