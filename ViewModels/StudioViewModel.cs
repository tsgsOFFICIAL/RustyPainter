using RustyPainter.ViewModels.Mvvm;
using RustyPainter.Service.Logging;
using RustyPainter.Service;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using Avalonia.Threading;
using RustyPainter.Interface;
using RustyPainter.Imaging;
using RustyPainter.Painting;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.IO;
using System;

namespace RustyPainter.ViewModels;

/// <summary>What the canvas in the middle shows.</summary>
public enum PreviewMode
{
    /// <summary>The picture as it would be painted.</summary>
    Result,

    /// <summary>The picture as loaded.</summary>
    Original,

    /// <summary>The placed picture on the left, the result on the right.</summary>
    Split
}

/// <summary>Where the picture comes from.</summary>
public enum SourceKind
{
    Picture,
    Link,
    Text
}

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

/// <summary>A small colored square showing one color the picture uses.</summary>
public sealed record ColorChip(string Hex)
{
    public IBrush Brush { get; } = new SolidColorBrush(Color.Parse(Hex));
}

/// <summary>
/// The Studio page: pick a surface, load a picture, place it on the canvas, choose how its colors are matched
/// and see the result as it would be painted.
/// </summary>
public sealed class StudioViewModel : ViewModelBase
{
    /// <summary>The picture formats that can be loaded. GIF is deliberately not one of them.</summary>
    public static readonly IReadOnlyList<string> SupportedExtensions = [".png", ".jpg", ".jpeg", ".webp", ".bmp"];

    /// <summary>The category filter that shows every surface.</summary>
    public const string AllCategories = "All";

    private readonly ISurfaceService _surfaces;
    private readonly IPictureDownloader _downloader;

    private string _linkText = "";
    private bool _isLoadingLink;
    private CancellationTokenSource? _download;
    private string _text = "";
    private string _textColorHex = "#FFFFFF";
    private string _textBackgroundHex = "#000000";
    private FontFamily? _textFont;

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
    private bool _fillBackground;
    private string _backgroundHex = "#000000";

    private CancellationTokenSource? _pending;
    private PaintPlan? _plan;
    private WriteableBitmap? _preview;
    private WriteableBitmap? _reference;
    private double _splitPosition = 0.5;
    private bool _isBusy;

    public StudioViewModel(ISurfaceService surfaces, IPictureDownloader? downloader = null)
    {
        _surfaces = surfaces;
        _downloader = downloader ?? new PictureDownloader();

        SourceChoices = new([("Picture", SourceKind.Picture), ("Link", SourceKind.Link), ("Text", SourceKind.Text)], SourceKind.Picture);
        TextBackgroundChoices = new([("Clear", false), ("Solid", true)], false);
        TextWeightChoices = new([("Regular", false), ("Bold", true)], true);

        SourceChoices.Changed += () =>
        {
            OnPropertyChanged(nameof(IsPictureSource));
            OnPropertyChanged(nameof(IsLinkSource));
            OnPropertyChanged(nameof(IsTextSource));
            if (IsTextSource)
                RenderText();
        };
        TextBackgroundChoices.Changed += () => { OnPropertyChanged(nameof(IsTextBackgroundSolid)); RenderText(); };
        TextWeightChoices.Changed += RenderText;

        LoadLinkCommand = new RelayCommand(() => _ = TryLoadLinkAsync(LinkText), () => !IsLoadingLink && LinkText.Trim().Length > 0);

        AllSurfaces = surfaces.Catalog.Surfaces.Select(surface => new SurfaceOption(surface, CategoryOf(surface))).ToList();
        _selectedSurface = AllSurfaces[0];

        Categories = new[] { AllCategories }
            .Concat(AllSurfaces.Select(option => option.Category).Distinct())
            .Select(name => new CategoryChip(name) { IsSelected = name == AllCategories })
            .ToList();

        PlacementChoices = new([("Fit", FitMode.Fit), ("Fill", FitMode.Fill), ("Stretch", FitMode.Stretch)], FitMode.Fill);
        ColorModeChoices = new([("Game swatches", ColorMode.Swatches), ("Any color", ColorMode.Free)], ColorMode.Swatches);
        DitheringChoices = new([("Smooth", Dithering.Smooth), ("Pattern", Dithering.Ordered), ("None", Dithering.None)], Dithering.Smooth);
        FilterChoices = new(
        [
            ("None", ImageFilter.None), ("Black and white", ImageFilter.BlackAndWhite), ("Sepia", ImageFilter.Sepia),
            ("Invert", ImageFilter.Invert), ("Brighten", ImageFilter.Brighten), ("Posterize", ImageFilter.Posterize)
        ], ImageFilter.None);
        QualityChoices = new([("1", 1), ("2", 2), ("4", 4), ("8", 8), ("16", 16), ("32", 32)], 4);
        PreviewChoices = new([("Result", PreviewMode.Result), ("Original", PreviewMode.Original), ("Split", PreviewMode.Split)], PreviewMode.Result);

        PlacementChoices.Changed += Changed;
        ColorModeChoices.Changed += () => { OnPropertyChanged(nameof(IsFreeMode)); Changed(); };
        DitheringChoices.Changed += Changed;
        FilterChoices.Changed += Changed;
        QualityChoices.Changed += Changed;
        PreviewChoices.Changed += () => { OnPropertyChanged(nameof(ShowOriginal)); OnPropertyChanged(nameof(ShowResult)); OnPropertyChanged(nameof(ShowSplit)); };

        ClearImageCommand = new RelayCommand(ClearImage, () => HasImage);
        PaintCommand = new RelayCommand(RequestPaint, () => CanPaint);
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
            OnPropertyChanged(nameof(SplitWidth));
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
            if (SetProperty(ref _offsetX, Math.Round(Math.Clamp(value, -100, 100), 1)))
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
            if (SetProperty(ref _offsetY, Math.Round(Math.Clamp(value, -100, 100), 1)))
            {
                OnPropertyChanged(nameof(OffsetYText));
                Changed();
            }
        }
    }

    public string OffsetYText => $"{OffsetY:+0;-0;0}";

    /// <summary>Moves the picture by a share of the canvas, for dragging it with the mouse. 1 is the full canvas width or height.</summary>
    public void MoveBy(double shareX, double shareY)
    {
        OffsetX += shareX * 100;
        OffsetY += shareY * 100;
    }

    /// <summary>Puts the zoom and the shift back, keeping the rotation, flips and fit mode.</summary>
    public void ResetView()
    {
        Zoom = 1;
        OffsetX = 0;
        OffsetY = 0;
    }

    /// <summary>Zooms by a factor, for the mouse wheel.</summary>
    public void ZoomBy(double factor) => Zoom *= factor;

    public RelayCommand RotateCommand { get; }
    public RelayCommand FlipHorizontalCommand { get; }
    public RelayCommand FlipVerticalCommand { get; }
    public RelayCommand ResetPlacementCommand { get; }

    // ---- color ----

    public ChoiceGroup<ColorMode> ColorModeChoices { get; }
    public ChoiceGroup<Dithering> DitheringChoices { get; }
    public ChoiceGroup<ImageFilter> FilterChoices { get; }

    /// <summary>Whether any color can be used, so the number of colors is a choice.</summary>
    public bool IsFreeMode => ColorModeChoices.Selected == ColorMode.Free;

    /// <summary>How many colors the picture is reduced to in free mode, from 2 to 256.</summary>
    public double MaxColors
    {
        get => _maxColors;
        set
        {
            if (SetProperty(ref _maxColors, Math.Round(Math.Clamp(value, 2, 256))))
            {
                OnPropertyChanged(nameof(MaxColorsText));
                Changed();
            }
        }
    }

    public string MaxColorsText => $"{MaxColors:0}";

    /// <summary>Whether empty areas get a background color, so the whole canvas is painted.</summary>
    public bool FillBackground
    {
        get => _fillBackground;
        set
        {
            if (SetProperty(ref _fillBackground, value))
                Changed();
        }
    }

    /// <summary>The background color as "#RRGGBB".</summary>
    public string BackgroundHex
    {
        get => _backgroundHex;
        set
        {
            if (SetProperty(ref _backgroundHex, value ?? ""))
            {
                OnPropertyChanged(nameof(BackgroundValue));
                if (FillBackground)
                    Changed();
            }
        }
    }

    /// <summary>The background color for a color picker. Setting it updates <see cref="BackgroundHex"/>.</summary>
    public Avalonia.Media.Color BackgroundValue
    {
        get => ColorOf(BackgroundHex);
        set => BackgroundHex = new Rgb(value.R, value.G, value.B).Hex;
    }

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

    public ChoiceGroup<PreviewMode> PreviewChoices { get; }

    /// <summary>The picture as loaded, without placement or colors.</summary>
    public bool ShowOriginal => HasImage && PreviewChoices.Selected == PreviewMode.Original;

    /// <summary>The result as it would be painted. In split view it fills the whole canvas, with the placed picture over its left half.</summary>
    public bool ShowResult => HasImage && PreviewChoices.Selected != PreviewMode.Original && Preview is not null;

    /// <summary>Whether the left of the divider shows the original picture, placed like the result.</summary>
    public bool ShowSplit => HasImage && PreviewChoices.Selected == PreviewMode.Split && Reference is not null;

    /// <summary>Where the divider of the split view is, from 0 (left edge) to 1 (right edge).</summary>
    public double SplitPosition
    {
        get => _splitPosition;
        set
        {
            if (SetProperty(ref _splitPosition, Math.Clamp(value, 0, 1)))
                OnPropertyChanged(nameof(SplitWidth));
        }
    }

    /// <summary>The width of the part of the canvas left of the divider, in canvas units.</summary>
    public double SplitWidth => CanvasWidth * SplitPosition;

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
            OnPropertyChanged(nameof(TimeText));
            OnPropertyChanged(nameof(CanPaint));
            PaintCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasPlan => Plan is not null;

    /// <summary>The original picture placed on the canvas, with its full detail whatever the brush size. Shown beside the result in split view.</summary>
    public WriteableBitmap? Reference
    {
        get => _reference;
        private set
        {
            if (SetProperty(ref _reference, value))
                OnPropertyChanged(nameof(ShowSplit));
        }
    }

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

    /// <summary>How long to wait after a change before preparing the picture, so a dragged slider does not start a job per pixel.</summary>
    public TimeSpan ReprocessDelay { get; set; } = TimeSpan.FromMilliseconds(80);

    /// <summary>Whether the preview is being updated.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
                return;

            OnPropertyChanged(nameof(CanPaint));
            PaintCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Raised when the user chooses to paint what is shown.</summary>
    public event Action<PaintRequest>? PaintRequested;

    public RelayCommand PaintCommand { get; }

    /// <summary>Whether there is a finished plan to paint, which is not the case while it is still being worked out.</summary>
    public bool CanPaint => Plan is { PaintedCells: > 0 } && !IsBusy;

    private void RequestPaint()
    {
        if (Plan is not { } plan || !CanPaint)
            return;

        PaintRequested?.Invoke(new PaintRequest(plan, SelectedSurface.Surface, ColorModeChoices.Selected, BlockSize, ImageName));
    }

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

    public string ColorsText => Plan is { } plan ? $"{plan.ColorsUsed} colors" : "";

    /// <summary>A rough time for painting at the pace chosen in the get-ready page, or the default pace before that.</summary>
    public string TimeText => Plan is { } plan
        ? PaintTimeEstimator.Describe(PaintTimeEstimator.Estimate(plan.Strokes, PaintTiming.FromSpeed(PaintSpeed), ColorModeChoices.Selected, SelectedSurface.Surface.ScrollOut))
        : "";

    /// <summary>The pace the estimate is made for, from 1 to 100.</summary>
    public int PaintSpeed { get; set; } = 60;

    /// <summary>The colors the picture ends up using, most used first.</summary>
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

    // ---- picture from a link ----

    /// <summary>The ways to bring in a picture: a file, a web link or typed text.</summary>
    public ChoiceGroup<SourceKind> SourceChoices { get; }

    public bool IsPictureSource => SourceChoices.Selected == SourceKind.Picture;
    public bool IsLinkSource => SourceChoices.Selected == SourceKind.Link;
    public bool IsTextSource => SourceChoices.Selected == SourceKind.Text;

    /// <summary>The web link as typed.</summary>
    public string LinkText
    {
        get => _linkText;
        set
        {
            if (SetProperty(ref _linkText, value ?? ""))
                LoadLinkCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>Whether a picture is being fetched from a link.</summary>
    public bool IsLoadingLink
    {
        get => _isLoadingLink;
        private set
        {
            if (SetProperty(ref _isLoadingLink, value))
                LoadLinkCommand.RaiseCanExecuteChanged();
        }
    }

    public RelayCommand LoadLinkCommand { get; }

    /// <summary>
    /// Fetches the picture a web link points to. On failure the current picture is kept and
    /// <see cref="ErrorText"/> explains why.
    /// </summary>
    /// <returns><see langword="true"/> if the picture was loaded.</returns>
    public async Task<bool> TryLoadLinkAsync(string link)
    {
        if (!TryReadLink(link, out Uri? address))
        {
            ErrorText = "That does not look like a web link. It should start with http:// or https://.";
            return false;
        }

        _download?.Cancel();
        CancellationTokenSource cts = new();
        _download = cts;
        IsLoadingLink = true;
        ErrorText = "";

        try
        {
            byte[] data = await _downloader.DownloadAsync(address, cts.Token);
            if (cts.IsCancellationRequested)
                return false;

            switch (PictureFormatDetector.Detect(data))
            {
                case PictureFormat.Gif:
                    ErrorText = "GIF pictures are not supported. Use a PNG, JPG, WebP or BMP link.";
                    return false;
                case PictureFormat.Unknown:
                    ErrorText = "That link does not lead to a PNG, JPG, WebP or BMP picture.";
                    return false;
            }

            using MemoryStream stream = new(data);
            Show(new Bitmap(stream), NameFromLink(address));
            return true;
        }
        catch (PictureDownloadException ex)
        {
            ErrorText = ex.Message;
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            AppLog.Warn("StudioViewModel", $"Could not use the picture at '{address}'.", ex);
            ErrorText = "The picture at that link could not be opened.";
            return false;
        }
        finally
        {
            if (ReferenceEquals(_download, cts))
                IsLoadingLink = false;
        }
    }

    /// <summary>Accepts "https://...", and also a bare "example.com/picture.png".</summary>
    private static bool TryReadLink(string text, out Uri address)
    {
        text = text.Trim();
        address = null!;

        if (text.Length == 0 || text.Contains(' '))
            return false;

        if (!text.Contains("://") && text.Contains('.'))
            text = "https://" + text;

        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? parsed) || parsed.Scheme is not ("http" or "https"))
            return false;

        address = parsed;
        return true;
    }

    private static string NameFromLink(Uri address)
    {
        string name = Path.GetFileName(address.AbsolutePath);
        return name.Length > 0 ? Uri.UnescapeDataString(name) : address.Host;
    }

    // ---- picture from text ----

    public ChoiceGroup<bool> TextBackgroundChoices { get; }
    public ChoiceGroup<bool> TextWeightChoices { get; }

    public bool IsTextBackgroundSolid => TextBackgroundChoices.Selected;

    /// <summary>The text to turn into a picture.</summary>
    public string Text
    {
        get => _text;
        set
        {
            if (SetProperty(ref _text, value ?? ""))
                RenderText();
        }
    }

    /// <summary>The color of the letters as "#RRGGBB". An unfinished value keeps the last good one in use.</summary>
    public string TextColorHex
    {
        get => _textColorHex;
        set
        {
            if (SetProperty(ref _textColorHex, value ?? ""))
            {
                OnPropertyChanged(nameof(TextColorValue));
                RenderText();
            }
        }
    }

    /// <summary>The color behind the letters as "#RRGGBB", used when the background is solid.</summary>
    public string TextBackgroundHex
    {
        get => _textBackgroundHex;
        set
        {
            if (SetProperty(ref _textBackgroundHex, value ?? ""))
            {
                OnPropertyChanged(nameof(TextBackgroundValue));
                RenderText();
            }
        }
    }

    /// <summary>The letter color for a color picker. Setting it updates <see cref="TextColorHex"/>.</summary>
    public Avalonia.Media.Color TextColorValue
    {
        get => ColorOf(TextColorHex);
        set => TextColorHex = new Rgb(value.R, value.G, value.B).Hex;
    }

    /// <summary>The background color for a color picker. Setting it updates <see cref="TextBackgroundHex"/>.</summary>
    public Avalonia.Media.Color TextBackgroundValue
    {
        get => ColorOf(TextBackgroundHex);
        set => TextBackgroundHex = new Rgb(value.R, value.G, value.B).Hex;
    }

    private static Avalonia.Media.Color ColorOf(string hex)
        => Rgb.TryParse(hex, out Rgb color) ? Avalonia.Media.Color.FromRgb(color.R, color.G, color.B) : Colors.Black;


    /// <summary>The font used for text pictures. The view hands in the app font.</summary>
    public FontFamily? TextFont
    {
        get => _textFont;
        set
        {
            if (SetProperty(ref _textFont, value))
                RenderText();
        }
    }

    private static IBrush BrushOf(string hex)
        => Rgb.TryParse(hex, out Rgb color) ? new SolidColorBrush(Avalonia.Media.Color.FromRgb(color.R, color.G, color.B)) : Brushes.Transparent;

    /// <summary>Turns <see cref="Text"/> into the picture, keeping the placement the user has set up.</summary>
    private void RenderText()
    {
        if (!IsTextSource || Text.Trim().Length == 0)
            return;

        if (!Rgb.TryParse(TextColorHex, out Rgb color))
            return;

        Rgb? background = null;
        if (IsTextBackgroundSolid)
        {
            if (!Rgb.TryParse(TextBackgroundHex, out Rgb solid))
                return;

            background = solid;
        }

        try
        {
            // Keep the placement while the text is being edited. A fresh text picture starts fitted, so no letters are cropped.
            bool editing = ImageName == "Text";
            Show(TextPicture.Render(Text, new TextPictureSettings(color, background, TextWeightChoices.Selected), TextFont), "Text", keepPlacement: editing);

            if (!editing)
                PlacementChoices.Select(FitMode.Fit);
        }
        catch (Exception ex)
        {
            AppLog.Warn("StudioViewModel", "Could not draw the text.", ex);
            ErrorText = "The text could not be turned into a picture.";
        }
    }

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
        TimeSpan delay = ReprocessDelay;

        _ = Task.Run(async () =>
        {
            try
            {
                // Wait a moment so dragging a slider does not start a new job for every pixel it moves.
                await Task.Delay(delay, cts.Token);
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
            DitheringChoices.Selected,
            FillBackground && Rgb.TryParse(BackgroundHex, out Rgb background) ? background : null);

        List<Rgb> swatches = _surfaces.Catalog.Layouts[surface.Layout].Swatches.ColorGrid!.Colors.Select(Rgb.Parse).ToList();

        return new Job(_oriented, settings, gridWidth, gridHeight, swatches);
    }

    private void Apply(PaintPlan plan)
    {
        Plan = plan;
        SetPreview(BitmapPixels.ToBitmap(plan.Render()), plan.Reference is { } reference ? BitmapPixels.ToBitmap(reference) : null);
        IsBusy = false;
    }

    private void SetPreview(WriteableBitmap? bitmap, WriteableBitmap? reference = null)
    {
        WriteableBitmap? old = _preview, oldReference = _reference;
        Preview = bitmap;
        Reference = reference;
        old?.Dispose();
        oldReference?.Dispose();
    }

    private sealed record Job(PixelBuffer Oriented, StudioSettings Settings, int GridWidth, int GridHeight, IReadOnlyList<Rgb> Swatches)
    {
        public PaintPlan Run() => StudioPipeline.Run(Oriented, Settings, GridWidth, GridHeight, Swatches);
    }

    // ---- housekeeping ----

    private void Show(Bitmap bitmap, string name, bool keepPlacement = false)
    {
        PixelBuffer pixels = BitmapPixels.FromBitmap(bitmap);

        Image?.Dispose();
        Image = bitmap;
        ImageName = name;
        ErrorText = "";

        _source = pixels;
        _oriented = null;

        if (keepPlacement)
            Changed();
        else
            ResetPlacement();
    }

    private void ClearImage()
    {
        _download?.Cancel();
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
