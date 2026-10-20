using Avalonia.Media.Imaging;
using RustyPainter.Imaging;
using RustyPainter.Library;
using RustyPainter.Localization;
using RustyPainter.ViewModels.Mvvm;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;

namespace RustyPainter.ViewModels;

/// <summary>One row of the layers of a painting.</summary>
public sealed class LayerChip(int number, string label, bool isSelected, Action select) : ViewModelBase
{
    public int Number { get; } = number;

    /// <summary>What is on the layer: the name of the picture, or the word for a layer with nothing on it yet.</summary>
    public string Label { get; } = label;

    public bool IsSelected { get; } = isSelected;

    public RelayCommand SelectCommand { get; } = new(select);
}

/// <summary>
/// The part of Studio that handles the layers of a painting. A painting is a stack of pictures, each with its own place on the canvas,
/// laid over each other before the colors are matched, so a photo and some text end up in the same painting. Like the frames, the open
/// layer lives in the fields of Studio and the others are kept as a picture and its settings until they are opened. Colors, brush and
/// surface are shared by all layers.
/// </summary>
public sealed partial class StudioViewModel
{
    /// <summary>How many layers a painting can have.</summary>
    public const int MaxLayers = 8;

    /// <summary>One layer that is not open: its picture as a PNG file, how it sits on the canvas, and the decoded picture once that is needed.</summary>
    private sealed record LayerSlot(byte[]? Picture, FrameSettings Settings, PixelBuffer? Source = null)
    {
        public LayerSlot() : this(null, new FrameSettings())
        {
        }
    }

    /// <summary>A layer ready for the pipeline, which turns it and places it on a worker thread.</summary>
    private sealed record LayerInput(PixelBuffer Picture, int QuarterTurns, bool FlipX, bool FlipY, PlacementSettings Placement)
    {
        public PlacedPicture Place() => new(PlacementEngine.Orient(Picture, QuarterTurns, FlipX, FlipY), Placement);
    }

    // The layers of the open frame, bottom to top. The entry of the open layer is out of date: its picture is in the fields.
    private List<LayerSlot> _layers = [new()];
    private int _layerIndex;

    public int LayerCount => _layers.Count;

    /// <summary>Whether the painting is made of more than one layer.</summary>
    public bool HasMultipleLayers => _layers.Count > 1;

    /// <summary>The open layer, counted from 0 at the bottom.</summary>
    public int LayerIndex => _layerIndex;

    /// <summary>The rows of the layers, the top one first, as the layers sit over each other.</summary>
    public IReadOnlyList<LayerChip> LayerChips
        => [.. _layers.Select((layer, index) => new LayerChip(index + 1, LabelOf(layer, index), index == _layerIndex, () => SelectLayer(index))).Reverse()];

    public RelayCommand AddLayerCommand { get; private set; } = null!;
    public RelayCommand RemoveLayerCommand { get; private set; } = null!;
    public RelayCommand MoveLayerUpCommand { get; private set; } = null!;
    public RelayCommand MoveLayerDownCommand { get; private set; } = null!;

    /// <summary>A new layer goes over the open one, which needs a picture first, since an empty layer adds nothing.</summary>
    public bool CanAddLayer => HasImage && _layers.Count < MaxLayers;
    public bool CanRemoveLayer => _layers.Count > 1;
    public bool CanMoveLayerUp => _layerIndex < _layers.Count - 1;
    public bool CanMoveLayerDown => _layerIndex > 0;

    /// <summary>Whether any layer has a picture, which is what there has to be for the canvas to show something.</summary>
    public bool HasAnyPicture => HasImage || OtherLayersHavePictures;

    private bool OtherLayersHavePictures => _layers.Where((layer, index) => index != _layerIndex && layer.Picture is not null).Any();

    private void InitializeLayers()
    {
        AddLayerCommand = new RelayCommand(AddLayer, () => CanAddLayer);
        RemoveLayerCommand = new RelayCommand(RemoveLayer, () => CanRemoveLayer);
        MoveLayerUpCommand = new RelayCommand(() => MoveLayer(1), () => CanMoveLayerUp);
        MoveLayerDownCommand = new RelayCommand(() => MoveLayer(-1), () => CanMoveLayerDown);
    }

    private void RaiseLayers()
    {
        OnPropertyChanged(nameof(LayerCount));
        OnPropertyChanged(nameof(HasMultipleLayers));
        OnPropertyChanged(nameof(LayerIndex));
        OnPropertyChanged(nameof(LayerChips));
        OnPropertyChanged(nameof(CanAddLayer));
        OnPropertyChanged(nameof(CanRemoveLayer));
        OnPropertyChanged(nameof(CanMoveLayerUp));
        OnPropertyChanged(nameof(CanMoveLayerDown));
        RaiseAnyPicture();
        AddLayerCommand.RaiseCanExecuteChanged();
        RemoveLayerCommand.RaiseCanExecuteChanged();
        MoveLayerUpCommand.RaiseCanExecuteChanged();
        MoveLayerDownCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Says that what decides whether the canvas shows something may have changed.</summary>
    private void RaiseAnyPicture()
    {
        OnPropertyChanged(nameof(HasAnyPicture));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(ShowResult));
        OnPropertyChanged(nameof(ShowRender));
        OnPropertyChanged(nameof(ShowSplit));
        SaveCommand.RaiseCanExecuteChanged();
    }

    private string LabelOf(LayerSlot layer, int index)
    {
        bool open = index == _layerIndex;
        bool empty = open ? Image is null : layer.Picture is null;

        return empty ? Loc.Get("LayerEmpty") : Loc.PictureName(open ? ImageName : layer.Settings.SourceName);
    }

    // ---- working with the layers ----

    /// <summary>Opens a layer, keeping the picture of the one that was open.</summary>
    public void SelectLayer(int index)
    {
        if (index < 0 || index >= _layers.Count || index == _layerIndex)
            return;

        StopPlaying();
        StoreOpenLayer();
        _layerIndex = index;
        LoadOpenLayer();
        RaiseLayers();
        Changed();
    }

    /// <summary>Adds an empty layer over the open one and opens it, ready for a picture or some text.</summary>
    public void AddLayer()
    {
        if (!CanAddLayer)
            return;

        StopPlaying();
        StoreOpenLayer();
        _layers.Insert(_layerIndex + 1, new LayerSlot());
        _layerIndex++;
        LoadOpenLayer();
        RaiseLayers();
        Changed();
    }

    /// <summary>Takes the open layer out of the painting and opens the one under it.</summary>
    public void RemoveLayer()
    {
        if (!CanRemoveLayer)
            return;

        StopPlaying();
        _layers.RemoveAt(_layerIndex);
        _layerIndex = Math.Max(0, _layerIndex - 1);
        LoadOpenLayer();
        RaiseLayers();
        Changed();
    }

    /// <summary>Moves the open layer up (1) or down (-1) in the stack.</summary>
    public void MoveLayer(int direction)
    {
        int target = _layerIndex + direction;
        if (target < 0 || target >= _layers.Count)
            return;

        StopPlaying();
        StoreOpenLayer();
        (_layers[_layerIndex], _layers[target]) = (_layers[target], _layers[_layerIndex]);
        _layerIndex = target;
        RaiseLayers();
        Changed();
    }

    /// <summary>Keeps what is shown as the open layer.</summary>
    private void StoreOpenLayer()
        => _layers[_layerIndex] = new LayerSlot(PictureBytes(), CaptureFrame(), _source);

    /// <summary>Brings the open layer into the fields of Studio, or empties them for a layer with nothing on it.</summary>
    private void LoadOpenLayer()
    {
        LayerSlot slot = _layers[_layerIndex];

        if (slot.Picture is not null)
        {
            ApplyFrame(slot.Settings, slot.Picture);
            return;
        }

        ClearPicture();
        SourceChoices.Select(SourceKind.Picture);
        Text = "";
        ResetPlacement();
    }

    // ---- dragging a layer ----

    // The render shown holds every layer, so moving it would move them all. While a layer is dragged, the canvas shows the layers
    // under it, the layer itself and the layers over it as three pictures instead, and only the middle one follows the mouse.
    private WriteableBitmap? _dragUnder, _dragLayer, _dragOver;

    /// <summary>The layers under the open one while it is dragged, or <see langword="null"/>.</summary>
    public WriteableBitmap? DragUnder => _dragUnder;

    /// <summary>The open layer on its own while it is dragged, or <see langword="null"/>.</summary>
    public WriteableBitmap? DragLayer => _dragLayer;

    /// <summary>The layers over the open one while it is dragged, or <see langword="null"/>.</summary>
    public WriteableBitmap? DragOver => _dragOver;

    /// <summary>Whether the canvas shows the three pictures of a layer being dragged instead of the render.</summary>
    public bool IsLayerDrag => _dragLayer is not null;

    /// <summary>The render, unless a layer is being dragged.</summary>
    public bool ShowRender => ShowResult && !IsLayerDrag;

    /// <summary>
    /// Prepares to drag the open layer: the layers under it, itself and the layers over it, placed on the canvas but not yet matched to
    /// colors, so this is quick. Does nothing with a single layer, which the render itself can be moved for.
    /// </summary>
    public void BeginLayerDrag()
    {
        if (!HasMultipleLayers || IsLayerDrag || _source is null)
            return;

        (int gridWidth, int gridHeight) = StudioGeometry.Grid(SelectedSurface.Surface, BlockSize);
        double scale = Math.Max(1, DragDetail / (double)Math.Max(gridWidth, gridHeight));
        int width = Math.Max(1, (int)Math.Round(gridWidth * scale)), height = Math.Max(1, (int)Math.Round(gridHeight * scale));

        List<LayerInput> inputs = LayerInputs(out int open);

        WriteableBitmap? Stack(IEnumerable<LayerInput> part)
        {
            List<PlacedPicture> placed = [.. part.Select(layer => layer.Place())];
            return placed.Count == 0 ? null : BitmapPixels.ToBitmap(StudioPipeline.Compose(placed, width, height));
        }

        _dragUnder = Stack(inputs.Take(open));
        _dragLayer = Stack(inputs.Skip(open).Take(1));
        _dragOver = Stack(inputs.Skip(open + 1));
        RaiseDrag();
    }

    /// <summary>Goes back to showing the render.</summary>
    public void EndLayerDrag()
    {
        if (!IsLayerDrag)
            return;

        WriteableBitmap?[] old = [_dragUnder, _dragLayer, _dragOver];
        _dragUnder = _dragLayer = _dragOver = null;
        RaiseDrag();

        foreach (WriteableBitmap? bitmap in old)
            bitmap?.Dispose();
    }

    /// <summary>How many pixels along its longer side the pictures of a dragged layer have at least.</summary>
    private const int DragDetail = 512;

    private void RaiseDrag()
    {
        OnPropertyChanged(nameof(DragUnder));
        OnPropertyChanged(nameof(DragLayer));
        OnPropertyChanged(nameof(DragOver));
        OnPropertyChanged(nameof(IsLayerDrag));
        OnPropertyChanged(nameof(ShowRender));
    }

    // ---- the pictures of all layers ----

    /// <summary>Whether there is anything to make a plan of, in the open layer or another one.</summary>
    private bool HasSource => _source is not null || OtherLayersHavePictures;

    /// <summary>The layers that have a picture, bottom to top, as the pipeline needs them.</summary>
    private List<LayerInput> LayerInputs() => LayerInputs(out _);

    /// <param name="openPosition">Where the open layer is in the list returned, or -1 when it has no picture.</param>
    private List<LayerInput> LayerInputs(out int openPosition)
    {
        List<LayerInput> inputs = [];
        openPosition = -1;

        for (int i = 0; i < _layers.Count; i++)
        {
            if (i == _layerIndex)
            {
                if (_source is null)
                    continue;

                (int, bool, bool) key = (_quarterTurns, _flipX, _flipY);
                if (_oriented is null || _orientedFor != key)
                {
                    _oriented = PlacementEngine.Orient(_source, _quarterTurns, _flipX, _flipY);
                    _orientedFor = key;
                }

                openPosition = inputs.Count;

                // The turning is done already, and the picture is cached with it.
                inputs.Add(new LayerInput(_oriented, 0, false, false, new PlacementSettings(PlacementChoices.Selected, Zoom, OffsetX / 100, OffsetY / 100)));
                continue;
            }

            LayerSlot slot = _layers[i];
            if (slot.Picture is null)
                continue;

            if (slot.Source is null)
            {
                slot = slot with { Source = Decode(slot.Picture) };
                _layers[i] = slot;
            }

            inputs.Add(new LayerInput(slot.Source!, Math.Clamp(slot.Settings.QuarterTurns, 0, 3), slot.Settings.FlipX, slot.Settings.FlipY, PlacementOf(slot.Settings)));
        }

        return inputs;
    }

    private static PlacementSettings PlacementOf(FrameSettings frame)
        => new(Enum.TryParse(frame.Placement, out FitMode fit) ? fit : FitMode.Fill, frame.Zoom, frame.OffsetX / 100, frame.OffsetY / 100);

    private static PixelBuffer Decode(byte[] picture)
    {
        using MemoryStream stream = new(picture);
        using Bitmap bitmap = new(stream);
        return BitmapPixels.FromBitmap(bitmap);
    }

    /// <summary>A stored layer turned and placed on the canvas, ready to lay over the others.</summary>
    private static PlacedPicture PlaceLayer(LayerSlot layer)
    {
        FrameSettings frame = layer.Settings;
        PixelBuffer oriented = PlacementEngine.Orient(layer.Source ?? Decode(layer.Picture!), Math.Clamp(frame.QuarterTurns, 0, 3), frame.FlipX, frame.FlipY);
        return new PlacedPicture(oriented, PlacementOf(frame));
    }
}
