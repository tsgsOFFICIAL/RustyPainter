using RustyPainter.ViewModels.Mvvm;
using RustyPainter.Service.Logging;
using Avalonia.Media.Imaging;
using RustyPainter.Interface;
using System.Collections.Generic;
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

/// <summary>
/// The Studio page: pick a surface, load a picture (or later some text) and see how it sits on the canvas.
/// </summary>
public sealed class StudioViewModel : ViewModelBase
{
    /// <summary>The picture formats that can be loaded. GIF is deliberately not one of them.</summary>
    public static readonly IReadOnlyList<string> SupportedExtensions = [".png", ".jpg", ".jpeg", ".webp", ".bmp"];

    /// <summary>The category filter that shows every surface.</summary>
    public const string AllCategories = "All";

    private SurfaceOption _selectedSurface;
    private string _selectedCategory = AllCategories;
    private Bitmap? _image;
    private string _imageName = "";
    private string _errorText = "";

    public StudioViewModel(ISurfaceService surfaces)
    {
        AllSurfaces = surfaces.Catalog.Surfaces.Select(surface => new SurfaceOption(surface, CategoryOf(surface))).ToList();
        _selectedSurface = AllSurfaces[0];

        Categories = new[] { AllCategories }
            .Concat(AllSurfaces.Select(option => option.Category).Distinct())
            .Select(name => new CategoryChip(name) { IsSelected = name == AllCategories })
            .ToList();

        ClearImageCommand = new RelayCommand(ClearImage, () => HasImage);
    }

    /// <summary>Every surface in the catalog.</summary>
    public IReadOnlyList<SurfaceOption> AllSurfaces { get; }

    /// <summary>The category filter buttons, starting with "All".</summary>
    public IReadOnlyList<CategoryChip> Categories { get; }

    public RelayCommand ClearImageCommand { get; }

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
            ClearImageCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasImage => Image is not null;
    public bool NoImage => Image is null;

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
    public void Load(Bitmap bitmap, string name) => Show(bitmap, name);

    /// <summary>Tells the user that a drop or paste held nothing usable.</summary>
    public void ReportNothingToLoad() => ErrorText = "There was no picture to load. Drop a PNG, JPG, WebP or BMP file.";

    private void Show(Bitmap bitmap, string name)
    {
        Image?.Dispose();
        Image = bitmap;
        ImageName = name;
        ErrorText = "";
    }

    private void ClearImage()
    {
        Image?.Dispose();
        Image = null;
        ImageName = "";
        ErrorText = "";
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
