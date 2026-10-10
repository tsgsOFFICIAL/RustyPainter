using RustyPainter.Localization;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using RustyPainter.Imaging;
using RustyPainter.Library;
using RustyPainter.Service.Logging;
using RustyPainter.ViewModels.Mvvm;
using System.IO;
using System.Linq;
using System;

namespace RustyPainter.ViewModels;

/// <summary>The part of Studio that saves its setup to the Library and brings a saved setup back.</summary>
public sealed partial class StudioViewModel
{
    /// <summary>The biggest side of the small picture of the result kept for the Library, in pixels.</summary>
    public const int ThumbnailSize = 256;

    private readonly IProjectStore? _projects;
    private string? _projectId;
    private string _projectName = "";
    private DateTimeOffset? _projectCreated;
    private string _saveStatus = "";

    /// <summary>The project that is open, or <see langword="null"/> for a picture that has not been saved yet.</summary>
    public string? CurrentProjectId
    {
        get => _projectId;
        private set
        {
            if (SetProperty(ref _projectId, value))
                OnPropertyChanged(nameof(IsSavedProject));
        }
    }

    /// <summary>Whether what is shown is a project that was saved before, so saving updates it.</summary>
    public bool IsSavedProject => CurrentProjectId is not null;

    /// <summary>The name the project is saved under.</summary>
    public string ProjectName { get => _projectName; private set => SetProperty(ref _projectName, value); }

    public RelayCommand SaveCommand { get; }

    /// <summary>Whether there is a picture to save and somewhere to save it.</summary>
    public bool CanSave => _projects is not null && HasImage;

    /// <summary>What to tell the user after saving, for a few seconds.</summary>
    public string SaveStatus
    {
        get => _saveStatus;
        private set
        {
            if (SetProperty(ref _saveStatus, value))
                OnPropertyChanged(nameof(HasSaveStatus));
        }
    }

    public bool HasSaveStatus => SaveStatus.Length > 0;

    /// <summary>Raised after a project was saved, so the Library can show it.</summary>
    public event Action? ProjectSaved;

    /// <summary>Says the project that is open is no longer what is shown, because another picture was put in.</summary>
    private void DetachProject()
    {
        CurrentProjectId = null;
        ProjectName = "";
        _projectCreated = null;
        SaveCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanSave));
    }

    /// <summary>Everything about the current setup, for saving.</summary>
    public StudioProject CaptureProject()
    {
        bool isText = IsTextSource && ImageName == "Text";

        return new StudioProject
        {
            Id = CurrentProjectId ?? "",
            Name = ProjectName.Length > 0 ? ProjectName : SuggestedName(isText),
            Created = _projectCreated ?? DateTimeOffset.UtcNow,
            SurfaceId = SelectedSurface.Surface.Id,
            Source = isText ? ProjectSource.Text : ProjectSource.Picture,
            SourceName = ImageName,
            Placement = PlacementChoices.Selected.ToString(),
            Zoom = Zoom,
            OffsetX = OffsetX,
            OffsetY = OffsetY,
            QuarterTurns = _quarterTurns,
            FlipX = _flipX,
            FlipY = _flipY,
            ColorMode = ColorModeChoices.Selected.ToString(),
            MaxColors = (int)MaxColors,
            Dithering = DitheringChoices.Selected.ToString(),
            Filter = FilterChoices.Selected.ToString(),
            Brightness = Brightness,
            Contrast = Contrast,
            Saturation = Saturation,
            FillBackground = FillBackground,
            BackgroundColor = BackgroundHex,
            Quality = BlockSize,
            Text = Text,
            TextColor = TextColorHex,
            TextBold = TextWeightChoices.Selected,
            TextSolidBackground = IsTextBackgroundSolid,
            TextBackgroundColor = TextBackgroundHex
        };
    }

    private string SuggestedName(bool isText)
    {
        if (isText)
        {
            string firstLine = Text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
            return firstLine.Length > 0 ? firstLine : Loc.Get("TextPictureName");
        }

        string name = Path.GetFileNameWithoutExtension(ImageName);
        return name.Length > 0 ? name : Loc.Get("Untitled");
    }

    private void SaveProject()
    {
        if (!CanSave || _projects is null || Image is null)
            return;

        try
        {
            using MemoryStream picture = new();
            Image.Save(picture);

            StudioProject saved = _projects.Save(CaptureProject(), picture.ToArray(), MakeThumbnail());
            CurrentProjectId = saved.Id;
            ProjectName = saved.Name;
            _projectCreated = saved.Created;
            SaveStatus = IsSavedProject ? Loc.Format("StudioSavedAs", saved.Name) : Loc.Get("Saved");
            ProjectSaved?.Invoke();

            DispatcherTimer.RunOnce(() => SaveStatus = "", TimeSpan.FromSeconds(4));
        }
        catch (Exception ex)
        {
            AppLog.Warn("StudioViewModel", "Could not save the project.", ex);
            ErrorText = Loc.Get("StudioErrSave");
        }
    }

    /// <summary>A small picture of the result, or of the original while there is no result yet.</summary>
    private byte[]? MakeThumbnail()
    {
        PixelBuffer? full = Plan?.Render() ?? _source;
        if (full is null)
            return null;

        double scale = Math.Min(1, (double)ThumbnailSize / Math.Max(full.Width, full.Height));
        int width = Math.Max(1, (int)Math.Round(full.Width * scale)), height = Math.Max(1, (int)Math.Round(full.Height * scale));

        // The result is a few blocks, which stay sharp edged. A real picture is shrunk smoothly.
        PixelBuffer small = Plan is not null && scale >= 1
            ? full
            : PlacementEngine.Resample(full, new PlacementSettings(FitMode.Stretch), width, height);

        using WriteableBitmap bitmap = BitmapPixels.ToBitmap(small);
        using MemoryStream stream = new();
        bitmap.Save(stream);
        return stream.ToArray();
    }

    /// <summary>Opens a saved project in Studio.</summary>
    /// <returns><see langword="false"/> if the project cannot be read.</returns>
    public bool OpenProject(string id)
    {
        if (_projects?.Load(id) is not { } loaded)
        {
            ErrorText = Loc.Get("StudioErrProjectGone");
            return false;
        }

        try
        {
            ApplyProject(loaded.Project, loaded.Picture);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn("StudioViewModel", $"Could not open the project '{id}'.", ex);
            ErrorText = Loc.Get("StudioErrProjectOpen");
            return false;
        }
    }

    /// <summary>Puts a saved setup back, with its picture.</summary>
    /// <param name="project">The settings.</param>
    /// <param name="picture">The picture as a PNG file.</param>
    public void ApplyProject(StudioProject project, byte[] picture)
    {
        // A surface that is no longer in the catalog leaves the current one, and says so.
        SurfaceOption? surface = AllSurfaces.FirstOrDefault(option => option.Surface.Id == project.SurfaceId);

        if (SelectedCategory != AllCategories)
            SelectedCategory = AllCategories;

        if (surface is not null)
            SelectedSurface = surface;

        // The picture first, because bringing it in puts the placement back to its start.
        if (project.Source == ProjectSource.Text)
        {
            SourceChoices.Select(SourceKind.Text);
            TextColorHex = project.TextColor;
            TextBackgroundChoices.Select(project.TextSolidBackground);
            TextBackgroundHex = project.TextBackgroundColor;
            TextWeightChoices.Select(project.TextBold);
            Text = project.Text;

            // An unchanged text has not drawn anything yet, so bring the saved picture in as it was.
            if (ImageName != "Text")
            {
                using MemoryStream stream = new(picture);
                Show(new Bitmap(stream), "Text");
            }
        }
        else
        {
            SourceChoices.Select(SourceKind.Picture);
            using MemoryStream stream = new(picture);
            Show(new Bitmap(stream), project.SourceName);
        }

        _quarterTurns = Math.Clamp(project.QuarterTurns, 0, 3);
        _flipX = project.FlipX;
        _flipY = project.FlipY;

        if (Enum.TryParse(project.Placement, out FitMode fit)) PlacementChoices.Select(fit);
        Zoom = project.Zoom;
        OffsetX = project.OffsetX;
        OffsetY = project.OffsetY;

        if (Enum.TryParse(project.ColorMode, out ColorMode mode)) ColorModeChoices.Select(mode);
        MaxColors = project.MaxColors;
        if (Enum.TryParse(project.Dithering, out Dithering dithering)) DitheringChoices.Select(dithering);
        if (Enum.TryParse(project.Filter, out ImageFilter filter)) FilterChoices.Select(filter);
        Brightness = project.Brightness;
        Contrast = project.Contrast;
        Saturation = project.Saturation;
        FillBackground = project.FillBackground;
        BackgroundHex = project.BackgroundColor;

        if (IsPresetSurface)
            PresetChoices.Select(project.Quality);
        else
            QualityChoices.Select(project.Quality);

        CurrentProjectId = project.Id.Length > 0 ? project.Id : null;
        ProjectName = project.Name;
        _projectCreated = project.Created;
        ErrorText = surface is null ? Loc.Get("StudioErrSurfaceUnknown") : "";

        SaveCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanSave));
        Changed();
    }
}
