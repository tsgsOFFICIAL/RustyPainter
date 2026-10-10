using RustyPainter.ViewModels.Mvvm;
using RustyPainter.Service.Logging;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using Avalonia.Threading;
using RustyPainter.Interface;
using RustyPainter.Imaging;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.IO;
using System;

namespace RustyPainter.ViewModels;

/// <summary>One entry of the surface picker.</summary>
/// <param name="Surface">The surface itself.</param>
/// <param name="Category">The group the surface is listed under.</param>
public sealed record SurfaceOption(Surface Surface, string Category)
{
    public string Name => Surface.Name;

    /// <summary>The canvas size on a 1440p screen, as shown in the picker.</summary>
    public string SizeText => $"{Surface.Canvas.Width:0} x {Surface.Canvas.Height:0} px";
}

/// <summary>One category filter button of the surface picker.</summary>
public sealed class CategoryChip(string name) : ViewModelBase
{
    private bool _isSelected;

    public string Name { get; } = name;

    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}

/// <summary>A small coloured square showing one colour the picture uses.</summary>
public sealed record ColorChip(string Hex)
{
    public IBrush Brush { get; } = new SolidColorBrush(Color.Parse(Hex));
}

/// <summary>
/// The Studio page: pick a surface, load a picture, place it on the canvas, choose how its colours are matched
/// and see the result as it would be painted.
/// </summary>
public sealed class StudioViewModel : ViewModelBase
{
    /// <summary>The picture formats that can be loaded. GIF is deliberately not one of them.</summary>
    public static readonly IReadOnlyList<string> SupportedExtensions = [".png", ".jpg", ".jpeg", ".webp", ".bmp"];

    /// <summary>The category filter that shows every surface.</summary>
    public const string AllCategories = "All";

    private readonly ISurfaceService _surfaces;

    private SurfaceOption _selectedSurface;
    private string _selectedCategory = AllCategories;
    private Bitmap? _image;
    private string _imageName = "";
    private string _errorText = "";

    private PixelBuffer? _source;
    private PixelBuffer? _oriented;
    private (int Turns, bool FlipX, bool FlipY) _orientedFor;
    private int _quarterTurns;
    private bool _flipX, _flipY;
    private double _zoom = 1, _offsetX, _offsetY;
    private double _brightness, _contrast, _saturation;
    private double _maxColors = 16;

    private CancellationTokenSource? _pending;
    private PaintPlan? _plan;
    private WriteableBitmap? _preview;
    private bool _isBusy;

    public StudioViewModel(ISurfaceService surfaces)
    {
        _surfaces = surfaces;

        AllSurfaces = surfaces.Catalog.Surfaces.Select(surface => new SurfaceOption(surface, CategoryOf(surface))).ToList();
        _selectedSurface = AllSurfaces[0];

        Categories = new[] { AllCategories }
            .Concat(AllSurfaces.Select(option => option.Category).Distinct())
            .Select(name => new CategoryChip(name) { IsSelected = name == AllCategories })
            .ToList();

        PlacementChoices = new([("Fit", FitMode.Fit), ("Fill", FitMode.Fill), ("Stretch", FitMode.Stretch)], FitMode.Fill);
        ColorModeChoices = new([("Game swatches", ColorMode.Swatches), ("Any colour", ColorMode.Free)], ColorMode.Swatches);
        DitheringChoices = new([("Smooth", Dithering.Smooth), ("Pattern", Dithering.Ordered), ("None", Dithering.None)], Dithering.Smooth);
        FilterChoices = new(
        [
            ("None", ImageFilter.None), ("Black and white", ImageFilter.BlackAndWhite), ("Sepia", ImageFilter.Sepia),
            ("Invert", ImageFilter.Invert), ("Brighten", ImageFilter.Brighten), ("Posterize", ImageFilter.Posterize)
        ], ImageFilter.None);
        QualityChoices = new([("1", 1), ("2", 2), ("4", 4), ("8", 8), ("16", 16), ("32", 32)], 4);
        PreviewChoices = new([("Result", false), ("Original", true)], false);

        PlacementChoices.Changed += Changed;
        ColorModeChoices.Changed += () => { OnPropertyChanged(nameof(IsFreeMode)); Changed(); };
        DitheringChoices.Changed += Changed;
        FilterChoices.Changed += Changed;
        QualityChoices.Changed += Changed;
        PreviewChoices.Changed += () => { OnPropertyChanged(nameof(ShowOriginal)); OnPropertyChanged(nameof(ShowResult)); };

        ClearImageCommand = new RelayCommand(ClearImage, () => HasImage);
        RotateCommand = new RelayCommand(() => { _quarterTurns = (_quarterTurns + 1) % 4; Changed(); });
        FlipHorizontalCommand = new RelayCommand(() => { _flipX = !_flipX; Changed(); });
        FlipVerticalCommand = new RelayCommand(() => { _flipY = !_flipY; Changed(); });
        ResetPlacementCommand = new RelayCommand(ResetPlacement);
        ResetAdjustmentsCommand = new RelayCommand(ResetAdjustments);
    }

    // ---- surface picker ----

    /// <summary>Every surface in the catalog.</summary>
    public IReadOnlyList<SurfaceOption> AllSurfaces { get; }

    /// <summary>The category filter buttons, starting with "All".</summary>
    public IReadOnlyList<CategoryChip> Categories { get; }

    /// <summary>The surfaces matching the selected category.</summary>
    public IReadOnlyList<SurfaceOption> VisibleSurfaces
        => SelectedCategory == AllCategories ? AllSurfaces : AllSurfaces.Where(option => option.Category == SelectedCategory).ToList();

    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (!SetProperty(ref _selectedCategory, value))
                return;

            OnPropertyChanged(nameof(VisibleSurfaces));

            foreach (CategoryChip chip in Categories)
                chip.IsSelected = chip.Name == value;

            // Keep a valid selection when the filter hides the current surface.
            if (VisibleSurfaces.Count > 0 && !VisibleSurfaces.Contains(SelectedSurface))
                SelectedSurface = VisibleSurfaces[0];
        }
    }

    public SurfaceOption SelectedSurface
    {
        get => _selectedSurface;
        set
        {
            if (value is null || !SetProperty(ref _selectedSurface, value))
                return;

            OnPropertyChanged(nameof(CanvasWidth));
            OnPropertyChanged(nameof(CanvasHeight));
            OnPropertyChanged(nameof(CanvasText));
            OnPropertyChanged(nameof(ScrollHint));
            OnPropertyChanged(nameof(HasScrollHint));
            OnPropertyChanged(nameof(GridText));
            OnPropertyChanged(nameof(IsTextureEstimated));
            Changed();
        }
    }

    /// <summary>The canvas width of the selected surface on a 1440p screen, used to size the preview.</summary>
    public double CanvasWidth => SelectedSurface.Surface.Canvas.Width;

    /// <summary>The canvas height of the selected surface on a 1440p screen, used to size the preview.</summary>
    public double CanvasHeight => SelectedSurface.Surface.Canvas.Height;

    public string CanvasText => SelectedSurface.SizeText;

    /// <summary>Whether the game needs scrolling out before this canvas is fully visible.</summary>
    public bool HasScrollHint => SelectedSurface.Surface.ScrollOut > 0;

    public string ScrollHint => $"Needs {SelectedSurface.Surface.ScrollOut} scroll-outs in the game first.";

    // ---- the picture ----

    /// <summary>The loaded picture, or <see langword="null"/>.</summary>
    public Bitmap? Image
    {
        get => _image;
        private set
        {
            if (!SetProperty(ref _image, value))
                return;

            OnPropertyChanged(nameof(HasImage));
            OnPropertyChanged(nameof(NoImage));
            OnPropertyChanged(nameof(ImageInfo));
            OnPropertyChanged(nameof(ShowResult));
            OnPropertyChanged(nameof(ShowOriginal));
            ClearImageCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasImage => Image is not null;
    public bool NoImage => Image is null;

    public RelayCommand ClearImageCommand { get; }

    public string ImageName { get => _imageName; private set => SetProperty(ref _imageName, value); }

    public string ImageInfo => Image is { } image ? $"{image.PixelSize.Width} x {image.PixelSize.Height}" : "";

    /// <summary>A message about the last picture that could not be loaded, or an empty string.</summary>
    public string ErrorText
    {
        get => _errorText;
        private set
        {
            if (SetProperty(ref _errorText, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => ErrorText.Length > 0;

    // ---- placement ----

    public ChoiceGroup<FitMode> PlacementChoices { get; }

    /// <summary>Extra zoom from 0.25 to 4.</summary>
    public double Zoom
    {
        get => _zoom;
        set
        {
            if (SetProperty(ref _zoom, Math.Round(Math.Clamp(value, 0.25, 4), 2)))
            {
                OnPropertyChanged(nameof(ZoomText));
                Changed();
            }
        }
    }

    public string ZoomText => $"{Zoom * 100:0}%";

    /// <summary>Horizontal shift from -100 to 100, in percent of the canvas width.</summary>
    public double OffsetX
    {
        get => _offsetX;
        set
        {
            if (SetProperty(ref _offsetX, Math.Round(Math.Clamp(value, -100, 100))))
            {
                OnPropertyChanged(nameof(OffsetXText));
                Changed();
            }
        }
    }

    public string OffsetXText => $"{OffsetX:+0;-0;0}";

    /// <summary>Vertical shift from -100 to 100, in percent of the canvas height.</summary>
    public double OffsetY
    {
        get => _offsetY;
        set
        {
            if (SetProperty(ref _offsetY, Math.Round(Math.Clamp(value, -100, 100))))
            {
                OnPropertyChanged(nameof(OffsetYText));
                Changed();
            }
        }
    }

    public string OffsetYText => $"{OffsetY:+0;-0;0}";

    public RelayCommand RotateCommand { get; }
    public RelayCommand FlipHorizontalCommand { get; }
    public RelayCommand FlipVerticalCommand { get; }
    public RelayCommand ResetPlacementCommand { get; }

    // ---- colour ----

    public ChoiceGroup<ColorMode> ColorModeChoices { get; }
    public ChoiceGroup<Dithering> DitheringChoices { get; }
    public ChoiceGroup<ImageFilter> FilterChoices { get; }

    /// <summary>Whether any colour can be used, so the number of colours is a choice.</summary>
    public bool IsFreeMode => ColorModeChoices.Selected == ColorMode.Free;

    /// <summary>How many colours the picture is reduced to in free mode, from 2 to 64.</summary>
    public double MaxColors
    {
        get => _maxColors;
        set
        {
            if (SetProperty(ref _maxColors, Math.Round(Math.Clamp(value, 2, 64))))
            {
                OnPropertyChanged(nameof(MaxColorsText));
                Changed();
            }
        }
    }

    public string MaxColorsText => $"{MaxColors:0}";

    public double Brightness
    {
        get => _brightness;
        set
        {
            if (SetProperty(ref _brightness, Math.Round(Math.Clamp(value, -100, 100))))
            {
                OnPropertyChanged(nameof(BrightnessText));
                Changed();
            }
        }
    }

    public string BrightnessText => $"{Brightness:+0;-0;0}";

    public double Contrast
    {
        get => _contrast;
        set
        {
            if (SetProperty(ref _contrast, Math.Round(Math.Clamp(value, -100, 100))))
            {
                OnPropertyChanged(nameof(ContrastText));
                Changed();
            }
        }
    }

    public string ContrastText => $"{Contrast:+0;-0;0}";

    public double Saturation
    {
        get => _saturation;
        set
        {
            if (SetProperty(ref _saturation, Math.Round(Math.Clamp(value, -100, 100))))
            {
                OnPropertyChanged(nameof(SaturationText));
                Changed();
            }
        }
    }

    public string SaturationText => $"{Saturation:+0;-0;0}";

    public RelayCommand ResetAdjustmentsCommand { get; }

    // ---- quality ----

    /// <summary>The brush sizes offered, in game units. Smaller is finer and takes far longer.</summary>
    public ChoiceGroup<int> QualityChoices { get; }

    /// <summary>The brush size in game units.</summary>
    public int BlockSize => QualityChoices.Selected;

    // ---- result ----

    public ChoiceGroup<bool> PreviewChoices { get; }

    public bool ShowOriginal => HasImage && PreviewChoices.Selected;
    public bool ShowResult => HasImage && !PreviewChoices.Selected && Preview is not null;

    /// <summary>The plan for the current settings, or <see langword="null"/> while no picture is loaded.</summary>
    public PaintPlan? Plan
    {
        get => _plan;
        private set
        {
            _plan = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasPlan));
            OnPropertyChanged(nameof(StrokesText));
            OnPropertyChanged(nameof(ColorsText));
            OnPropertyChanged(nameof(UsedColors));
        }
    }

    public bool HasPlan => Plan is not null;

    /// <summary>The plan drawn one pixel per cell, for showing on the canvas.</summary>
    public WriteableBitmap? Preview
    {
        get => _preview;
        private set
        {
            if (!SetProperty(ref _preview, value))
                return;

            OnPropertyChanged(nameof(ShowResult));
        }
    }

    /// <summary>Whether the preview is being updated.</summary>
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }

    /// <summary>The cells across and down for the selected surface and brush size.</summary>
    public string GridText
    {
        get
        {
            (int w, int h) = StudioGeometry.Grid(SelectedSurface.Surface, BlockSize);
            return $"{w} x {h} cells";
        }
    }

    /// <summary>Whether the paint texture of the selected surface is only an estimate.</summary>
    public bool IsTextureEstimated => !StudioGeometry.IsTextureKnown(SelectedSurface.Surface);

    public string StrokesText => Plan is { } plan ? $"{plan.StrokeCount:N0} strokes" : "";

    public string ColorsText => Plan is { } plan ? $"{plan.ColorsUsed} colours" : "";

    /// <summary>The colours the picture ends up using, most used first.</summary>
    public IReadOnlyList<ColorChip> UsedColors => Plan?.UsedColors.Select(color => new ColorChip(color.Hex)).ToList() ?? [];

    // ---- loading ----

    /// <summary>Whether <paramref name="path"/> has one of the <see cref="SupportedExtensions"/>.</summary>
    public static bool IsSupported(string path)
        => SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Loads the picture at <paramref name="path"/>. On failure the current picture is kept and
    /// <see cref="ErrorText"/> explains why.
    /// </summary>
    /// <returns><see langword="true"/> if the picture was loaded.</returns>
    public bool TryLoad(string path)
    {
        string name = Path.GetFileName(path);

        if (!IsSupported(path))
        {
            ErrorText = $"{name} is not supported. Use PNG, JPG, WebP or BMP.";
            return false;
        }

        try
        {
            using FileStream stream = File.OpenRead(path);
            Show(new Bitmap(stream), name);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn("StudioViewModel", $"Could not load '{path}'.", ex);
            ErrorText = $"{name} could not be opened. It may be damaged or not really an image.";
            return false;
        }
    }

    /// <summary>Uses an already decoded picture, for example one pasted from the clipboard.</summary>
    public void Load(Bitmap bitmap, string name)
    {
        try
        {
            Show(bitmap, name);
        }
        catch (Exception ex)
        {
            AppLog.Warn("StudioViewModel", $"Could not use the picture '{name}'.", ex);
            ErrorText = $"{name} could not be used.";
        }
    }

    /// <summary>Tells the user that a drop or paste held nothing usable.</summary>
    public void ReportNothingToLoad() => ErrorText = "There was no picture to load. Drop a PNG, JPG, WebP or BMP file.";

    // ---- processing ----

    /// <summary>
    /// Works out the plan for the current settings right now, on the calling thread, and shows it. The page itself
    /// updates in the background as settings change; this is for callers that need the result immediately.
    /// </summary>
    public PaintPlan? ProcessNow()
    {
        _pending?.Cancel();

        if (_source is null)
        {
            Plan = null;
            SetPreview(null);
            return null;
        }

        PaintPlan plan = CreateJob().Run();
        Apply(plan);
        return plan;
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(BlockSize));
        OnPropertyChanged(nameof(GridText));
        ScheduleReprocess();
    }

    private void ScheduleReprocess()
    {
        _pending?.Cancel();

        if (_source is null)
            return;

        CancellationTokenSource cts = new();
        _pending = cts;
        IsBusy = true;

        Job job = CreateJob();

        _ = Task.Run(async () =>
        {
            try
            {
                // Wait a moment so dragging a slider does not start a new job for every pixel it moves.
                await Task.Delay(80, cts.Token);
                PaintPlan plan = job.Run();

                Dispatcher.UIThread.Post(() =>
                {
                    if (!cts.IsCancellationRequested)
                        Apply(plan);
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppLog.Error("StudioViewModel", "Preparing the picture failed.", ex);
                Dispatcher.UIThread.Post(() =>
                {
                    ErrorText = "The picture could not be prepared.";
                    IsBusy = false;
                });
            }
        });
    }

    private Job CreateJob()
    {
        Surface surface = SelectedSurface.Surface;
        (int gridWidth, int gridHeight) = StudioGeometry.Grid(surface, BlockSize);

        (int, bool, bool) key = (_quarterTurns, _flipX, _flipY);
        if (_oriented is null || _orientedFor != key)
        {
            _oriented = PlacementEngine.Orient(_source!, _quarterTurns, _flipX, _flipY);
            _orientedFor = key;
        }

        StudioSettings settings = new(
            new PlacementSettings(PlacementChoices.Selected, Zoom, OffsetX / 100, OffsetY / 100),
            new AdjustmentSettings(Brightness / 100, Contrast / 100, Saturation / 100, FilterChoices.Selected),
            ColorModeChoices.Selected,
            (int)MaxColors,
            DitheringChoices.Selected);

        List<Rgb> swatches = _surfaces.Catalog.Layouts[surface.Layout].Swatches.ColorGrid!.Colors.Select(Rgb.Parse).ToList();

        return new Job(_oriented, settings, gridWidth, gridHeight, swatches);
    }

    private void Apply(PaintPlan plan)
    {
        Plan = plan;
        SetPreview(BitmapPixels.ToBitmap(plan.Render()));
        IsBusy = false;
    }

    private void SetPreview(WriteableBitmap? bitmap)
    {
        WriteableBitmap? old = _preview;
        Preview = bitmap;
        old?.Dispose();
    }

    private sealed record Job(PixelBuffer Oriented, StudioSettings Settings, int GridWidth, int GridHeight, IReadOnlyList<Rgb> Swatches)
    {
        public PaintPlan Run() => StudioPipeline.Run(Oriented, Settings, GridWidth, GridHeight, Swatches);
    }

    // ---- housekeeping ----

    private void Show(Bitmap bitmap, string name)
    {
        PixelBuffer pixels = BitmapPixels.FromBitmap(bitmap);

        Image?.Dispose();
        Image = bitmap;
        ImageName = name;
        ErrorText = "";

        _source = pixels;
        _oriented = null;
        ResetPlacement();
    }

    private void ClearImage()
    {
        _pending?.Cancel();
        Image?.Dispose();
        Image = null;
        ImageName = "";
        ErrorText = "";
        _source = null;
        _oriented = null;
        Plan = null;
        SetPreview(null);
        IsBusy = false;
    }

    private void ResetPlacement()
    {
        _quarterTurns = 0;
        _flipX = false;
        _flipY = false;
        PlacementChoices.Select(FitMode.Fill);

        // Setting the sliders raises Changed for each one, which only schedules work once the picture is loaded.
        Zoom = 1;
        OffsetX = 0;
        OffsetY = 0;
        Changed();
    }

    private void ResetAdjustments()
    {
        FilterChoices.Select(ImageFilter.None);
        Brightness = 0;
        Contrast = 0;
        Saturation = 0;
        Changed();
    }

    private static string CategoryOf(Surface surface)
    {
        string prefix = surface.Id.Split('-')[0];

        return prefix switch
        {
            "sign" => "Signs",
            "canvas" => "Canvases",
            "frame" => "Frames",
            "neon" => "Neon",
            _ => "Other"
        };
    }
}
