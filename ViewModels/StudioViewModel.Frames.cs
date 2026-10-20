using Avalonia.Media.Imaging;
using Avalonia.Threading;
using RustyPainter.Imaging;
using RustyPainter.Interface;
using RustyPainter.Library;
using RustyPainter.Service.Logging;
using RustyPainter.ViewModels.Mvvm;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System;

namespace RustyPainter.ViewModels;

/// <summary>One numbered button of the frames of an animated sign.</summary>
public sealed class FrameChip(int number, bool isSelected, Action select) : ViewModelBase
{
    public int Number { get; } = number;

    public bool IsSelected { get; } = isSelected;

    public RelayCommand SelectCommand { get; } = new(select);
}

/// <summary>
/// The part of Studio that handles the frames of an animated sign. Every frame has its own picture and its own place on the
/// canvas, while the colors, the brush and the surface are the same for all of them. The open frame lives in the fields of
/// Studio as any picture does, and the other frames are kept as a picture and its settings until they are opened.
/// </summary>
public sealed partial class StudioViewModel
{
    private sealed class FrameSlot
    {
        /// <summary>The layers of the frame, bottom to top. A frame with nothing in it has one layer with no picture, and stays blank.</summary>
        public List<LayerSlot> Layers { get; set; } = [new()];

        /// <summary>Which of the layers was open when the frame was left.</summary>
        public int OpenLayer { get; set; }

        public bool HasPicture => Layers.Any(layer => layer.Picture is not null);
    }

    private readonly List<FrameSlot> _frames = [new()];
    private int _frameIndex;
    private readonly DispatcherTimer _playback = new();
    private List<WriteableBitmap> _playFrames = [];
    private int _playIndex;
    private bool _isPlaying;
    private WriteableBitmap? _playShown;

    /// <summary>How many frames the open surface can show, which is one for a surface that does not animate.</summary>
    public int MaxFrames => SelectedSurface.Surface.Frames;

    /// <summary>Whether the open surface is an animated sign.</summary>
    public bool IsAnimatedSurface => SelectedSurface.Surface.IsAnimated;

    /// <summary>How many frames there are.</summary>
    public int FrameCount => _frames.Count;

    /// <summary>The open frame, counted from 0.</summary>
    public int FrameIndex => _frameIndex;

    /// <summary>The numbered buttons, one for each frame.</summary>
    public IReadOnlyList<FrameChip> FrameChips
        => [.. _frames.Select((_, index) => new FrameChip(index + 1, index == _frameIndex, () => SelectFrame(index)))];

    public RelayCommand AddFrameCommand { get; private set; } = null!;
    public RelayCommand RemoveFrameCommand { get; private set; } = null!;
    public RelayCommand PlayCommand { get; private set; } = null!;

    /// <summary>How fast the preview of the animation plays: one frame every two seconds, every second or twice a second.</summary>
    public ChoiceGroup<double> SpeedChoices { get; private set; } = null!;

    public bool CanAddFrame => IsAnimatedSurface && FrameCount < MaxFrames;
    public bool CanRemoveFrame => IsAnimatedSurface && FrameCount > 1;
    public bool CanPlay => IsAnimatedSurface && FrameCount > 1;

    /// <summary>Whether the preview is playing the frames one after the other.</summary>
    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            if (!SetProperty(ref _isPlaying, value))
                return;

            OnPropertyChanged(nameof(ShownPreview));
            OnPropertyChanged(nameof(PlayText));
            OnPropertyChanged(nameof(ShowSplit));
            OnPropertyChanged(nameof(ShowResult));
            OnPropertyChanged(nameof(ShowRender));
        }
    }

    /// <summary>The words on the play button.</summary>
    public string PlayText => Localization.Loc.Get(IsPlaying ? "FramesStop" : "FramesPlay");

    /// <summary>What the preview shows: the frame that is playing, or the result for the open picture.</summary>
    public WriteableBitmap? ShownPreview => IsPlaying ? _playShown : Preview;

    private void InitializeFrames()
    {
        SpeedChoices = new([("FramesSlow", 2.0), ("FramesMedium", 1.0), ("FramesFast", 0.5)], 1.0);
        SpeedChoices.Changed += () => _playback.Interval = TimeSpan.FromSeconds(SpeedChoices.Selected);
        _playback.Interval = TimeSpan.FromSeconds(SpeedChoices.Selected);
        _playback.Tick += (_, _) => AdvancePlayback();

        AddFrameCommand = new RelayCommand(AddFrame, () => CanAddFrame);
        RemoveFrameCommand = new RelayCommand(RemoveFrame, () => CanRemoveFrame);
        PlayCommand = new RelayCommand(() => _ = TogglePlayAsync(), () => CanPlay);
    }

    private void RaiseFrames()
    {
        OnPropertyChanged(nameof(IsAnimatedSurface));
        OnPropertyChanged(nameof(MaxFrames));
        OnPropertyChanged(nameof(FrameCount));
        OnPropertyChanged(nameof(FrameIndex));
        OnPropertyChanged(nameof(FrameChips));
        OnPropertyChanged(nameof(CanAddFrame));
        OnPropertyChanged(nameof(CanRemoveFrame));
        OnPropertyChanged(nameof(CanPlay));
        AddFrameCommand.RaiseCanExecuteChanged();
        RemoveFrameCommand.RaiseCanExecuteChanged();
        PlayCommand.RaiseCanExecuteChanged();
    }

    // ---- moving between frames ----

    /// <summary>Opens a frame, keeping the picture of the one that was open.</summary>
    public void SelectFrame(int index)
    {
        if (index < 0 || index >= _frames.Count || index == _frameIndex)
            return;

        StopPlaying();
        StoreOpenFrame();
        _frameIndex = index;
        LoadOpenFrame();
        RaiseFrames();
    }

    /// <summary>Adds a frame after the last one, starting as a copy of the open one, and opens it.</summary>
    public void AddFrame()
    {
        if (!CanAddFrame)
            return;

        StopPlaying();
        StoreOpenFrame();

        FrameSlot open = _frames[_frameIndex];
        _frames.Add(new FrameSlot { Layers = [.. open.Layers], OpenLayer = open.OpenLayer });
        _frameIndex = _frames.Count - 1;
        RaiseFrames();
    }

    /// <summary>Removes the open frame and opens the one before it.</summary>
    public void RemoveFrame()
    {
        if (!CanRemoveFrame)
            return;

        StopPlaying();
        _frames.RemoveAt(_frameIndex);
        _frameIndex = Math.Min(_frameIndex, _frames.Count - 1);
        LoadOpenFrame();
        RaiseFrames();
    }

    /// <summary>Keeps what is shown as the layers of the open frame.</summary>
    private void StoreOpenFrame()
    {
        StoreOpenLayer();

        FrameSlot slot = _frames[_frameIndex];
        slot.Layers = [.. _layers];
        slot.OpenLayer = _layerIndex;
    }

    /// <summary>Brings the layers of the open frame into Studio, with the picture of the open layer in its fields, or empties them for a blank frame.</summary>
    private void LoadOpenFrame()
    {
        FrameSlot slot = _frames[_frameIndex];
        _layers = [.. slot.Layers];
        _layerIndex = Math.Clamp(slot.OpenLayer, 0, _layers.Count - 1);

        LoadOpenLayer();
        RaiseLayers();
        Changed();
    }

    /// <summary>The picture that is shown, as a PNG file, or <see langword="null"/> when there is none.</summary>
    private byte[]? PictureBytes()
    {
        if (Image is null)
            return null;

        using MemoryStream stream = new();
        Image.Save(stream);
        return stream.ToArray();
    }

    /// <summary>What belongs to the picture that is shown, without anything that all frames share.</summary>
    public FrameSettings CaptureFrame()
    {
        bool isText = IsTextSource && ImageName == "Text";

        return new FrameSettings
        {
            Source = isText ? ProjectSource.Text : ProjectSource.Picture,
            SourceName = ImageName,
            Placement = PlacementChoices.Selected.ToString(),
            Zoom = Zoom,
            OffsetX = OffsetX,
            OffsetY = OffsetY,
            QuarterTurns = _quarterTurns,
            FlipX = _flipX,
            FlipY = _flipY,
            Text = Text,
            TextColor = TextColorHex,
            TextBold = TextWeightChoices.Selected,
            TextSolidBackground = IsTextBackgroundSolid,
            TextBackgroundColor = TextBackgroundHex
        };
    }

    /// <summary>What is saved of a frame: the settings and picture of its bottom layer, with the settings of the layers over it, and their pictures.</summary>
    private sealed record SavedFrame(FrameSettings Settings, byte[] Picture, List<byte[]> LayerPictures);

    /// <summary>A frame as it is saved, or <see langword="null"/> for one with no picture at all. A layer with no picture is not saved.</summary>
    private static SavedFrame? Saved(FrameSlot slot)
    {
        List<LayerSlot> filled = [.. slot.Layers.Where(layer => layer.Picture is not null)];
        if (filled.Count == 0)
            return null;

        FrameSettings settings = filled[0].Settings with { MoreLayers = new FrameList(filled.Skip(1).Select(layer => layer.Settings with { MoreLayers = [] })) };
        return new SavedFrame(settings, filled[0].Picture!, [.. filled.Skip(1).Select(layer => layer.Picture!)]);
    }

    /// <summary>
    /// The frames as they are saved. The first one comes apart from the rest, since a project describes it itself. The open frame is brought
    /// up to date first.
    /// </summary>
    /// <param name="all">Whether to save every frame, or only the open one, as a surface that does not animate does.</param>
    private (List<FrameSettings> Settings, List<byte[]> Pictures, SavedFrame? First, List<List<byte[]>> LayerPictures) CaptureFrames(bool all)
    {
        StoreOpenFrame();

        List<FrameSlot> slots = all ? _frames : [_frames[_frameIndex]];
        SavedFrame? first = Saved(slots[0]);

        List<FrameSettings> settings = [];
        List<byte[]> pictures = [];
        List<List<byte[]>> layers = [first?.LayerPictures ?? []];

        // A blank frame is saved as a frame with no picture of its own, which is the one thing that brings it back blank.
        foreach (FrameSlot slot in slots.Skip(1))
        {
            if (Saved(slot) is not { } frame)
                continue;

            settings.Add(frame.Settings);
            pictures.Add(frame.Picture);
            layers.Add(frame.LayerPictures);
        }

        return (settings, pictures, first, layers);
    }

    /// <summary>Puts the frames of a saved project back. The first frame is open already, since it is what the project's own fields describe.</summary>
    private void LoadMoreFrames(StudioProject project, IReadOnlyList<byte[]?>? framePictures, IReadOnlyList<IReadOnlyList<byte[]?>>? layerPictures)
    {
        StopPlaying();
        _frames.Clear();
        _frames.Add(new FrameSlot());
        _frameIndex = 0;

        // The bottom layer of the first frame is the open one, already in the fields. The layers over it follow.
        _layers = [new(), .. LayersFrom(project.MoreLayers, LayerPicturesOf(layerPictures, 0))];
        _layerIndex = 0;

        if (SelectedSurface.Surface.IsAnimated)
        {
            for (int i = 0; i < project.MoreFrames.Count && _frames.Count < MaxFrames; i++)
            {
                byte[]? picture = framePictures is not null && i < framePictures.Count ? framePictures[i] : null;
                FrameSettings frame = project.MoreFrames[i];
                _frames.Add(new FrameSlot { Layers = [new LayerSlot(picture, frame), .. LayersFrom(frame.MoreLayers, LayerPicturesOf(layerPictures, i + 1))] });
            }
        }

        RaiseFrames();
        RaiseLayers();
    }

    private static IReadOnlyList<byte[]?> LayerPicturesOf(IReadOnlyList<IReadOnlyList<byte[]?>>? layerPictures, int frame)
        => layerPictures is not null && frame < layerPictures.Count ? layerPictures[frame] : [];

    /// <summary>The layers over a bottom one, as they were saved. One whose picture is gone is left out.</summary>
    private static IEnumerable<LayerSlot> LayersFrom(FrameList settings, IReadOnlyList<byte[]?> pictures)
        => settings.Select((layer, index) => (layer, picture: index < pictures.Count ? pictures[index] : null))
            .Where(item => item.picture is not null)
            .Select(item => new LayerSlot(item.picture, item.layer));

    // ---- the plans of all frames ----

    /// <summary>The plan of each frame, first to last. A frame with nothing in it gives a plan with nothing to paint.</summary>
    public async Task<IReadOnlyList<PaintPlan>> PreparePlansAsync()
    {
        StoreOpenFrame();

        Surface surface = SelectedSurface.Surface;
        (int gridWidth, int gridHeight) = StudioGeometry.Grid(surface, BlockSize);
        List<Rgb> swatches = _surfaces.Catalog.Layouts[surface.Layout].Swatches.ColorGrid!.Colors.Select(Rgb.Parse).ToList();
        PaintPlan blank = new(gridWidth, gridHeight, swatches, new int[gridWidth * gridHeight].Select(_ => Quantizer.Unpainted).ToArray());

        // The settings of the colors are the open ones; the pictures and their places are each frame's own.
        List<List<LayerSlot>> work = _frames.Select(slot => slot.Layers.Where(layer => layer.Picture is not null).ToList()).ToList();
        StudioSettings shared = new(
            new PlacementSettings(),
            new AdjustmentSettings(Brightness / 100, Contrast / 100, Saturation / 100, EffectiveFilter, EffectiveFilterStrength),
            EffectiveMode,
            (int)MaxColors,
            EffectiveDithering,
            FillBackground && Rgb.TryParse(BackgroundHex, out Rgb background) ? background : null,
            SelectedSurface.Surface.LeaveIndex);

        return await Task.Run(() =>
        {
            List<PaintPlan> plans = [];
            foreach (List<LayerSlot> layers in work)
            {
                if (layers.Count == 0)
                {
                    plans.Add(blank);
                    continue;
                }

                List<PlacedPicture> placed = [.. layers.Select(PlaceLayer)];
                plans.Add(StudioPipeline.Run(placed, shared, gridWidth, gridHeight, swatches));
            }

            return (IReadOnlyList<PaintPlan>)plans;
        });
    }

    // ---- the preview of the animation ----

    /// <summary>Starts playing the frames one after the other, or stops.</summary>
    public async Task TogglePlayAsync()
    {
        if (IsPlaying)
        {
            StopPlaying();
            return;
        }

        if (!CanPlay)
            return;

        IsBusy = true;
        try
        {
            IReadOnlyList<PaintPlan> plans = await PreparePlansAsync();
            _playFrames = [.. plans.Select(plan => BitmapPixels.ToBitmap(plan.Render()))];
        }
        catch (Exception ex)
        {
            AppLog.Error("StudioViewModel", "Preparing the frames for the preview failed.", ex);
            ErrorText = Localization.Loc.Get("StudioErrPrepare");
            return;
        }
        finally
        {
            IsBusy = false;
        }

        _playIndex = 0;
        _playShown = _playFrames[0];
        IsPlaying = true;
        OnPropertyChanged(nameof(ShownPreview));
        _playback.Start();
    }

    /// <summary>Shows the next frame. The timer calls this, and so can a test.</summary>
    public void AdvancePlayback()
    {
        if (!IsPlaying || _playFrames.Count == 0)
            return;

        _playIndex = (_playIndex + 1) % _playFrames.Count;
        _playShown = _playFrames[_playIndex];
        OnPropertyChanged(nameof(ShownPreview));
        OnPropertyChanged(nameof(PlayingFrame));
    }

    /// <summary>The frame that is shown while playing, counted from 1.</summary>
    public int PlayingFrame => IsPlaying ? _playIndex + 1 : FrameIndex + 1;

    private void StopPlaying()
    {
        if (!IsPlaying && _playFrames.Count == 0)
            return;

        _playback.Stop();
        IsPlaying = false;
        _playShown = null;

        foreach (WriteableBitmap frame in _playFrames)
            frame.Dispose();

        _playFrames = [];
        OnPropertyChanged(nameof(ShownPreview));
        OnPropertyChanged(nameof(PlayingFrame));
    }
}
