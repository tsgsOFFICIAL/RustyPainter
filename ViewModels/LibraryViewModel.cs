using Avalonia.Media.Imaging;
using RustyPainter.Interface;
using RustyPainter.Library;
using RustyPainter.Service.Logging;
using RustyPainter.ViewModels.Mvvm;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System;

namespace RustyPainter.ViewModels;

/// <summary>One saved project as a card in the Library.</summary>
public sealed class ProjectCard : ViewModelBase
{
    private readonly LibraryViewModel _library;
    private string _name;
    private string _editName = "";
    private bool _isRenaming;
    private bool _isConfirmingDelete;

    internal ProjectCard(LibraryViewModel library, ProjectEntry entry, string surfaceName, Bitmap? thumbnail, string modifiedText)
    {
        _library = library;
        Id = entry.Project.Id;
        _name = entry.Project.Name;
        SurfaceName = surfaceName;
        Thumbnail = thumbnail;
        ModifiedText = modifiedText;
        Modified = entry.Project.Modified;

        OpenCommand = new RelayCommand(() => _library.Open(this));
        BeginRenameCommand = new RelayCommand(() =>
        {
            EditName = Name;
            IsRenaming = true;
        });
        CommitRenameCommand = new RelayCommand(() => _library.Rename(this, EditName));
        CancelRenameCommand = new RelayCommand(() => IsRenaming = false);
        DuplicateCommand = new RelayCommand(() => _library.Duplicate(this));
        AskDeleteCommand = new RelayCommand(() => IsConfirmingDelete = true);
        ConfirmDeleteCommand = new RelayCommand(() => _library.Delete(this));
        CancelDeleteCommand = new RelayCommand(() => IsConfirmingDelete = false);
    }

    public string Id { get; }
    public string SurfaceName { get; }
    public Bitmap? Thumbnail { get; }
    public bool HasThumbnail => Thumbnail is not null;
    public string ModifiedText { get; }
    public DateTimeOffset Modified { get; }

    public string Name { get => _name; internal set => SetProperty(ref _name, value); }

    /// <summary>What is being typed while the name is changed.</summary>
    public string EditName { get => _editName; set => SetProperty(ref _editName, value); }

    public bool IsRenaming
    {
        get => _isRenaming;
        internal set
        {
            if (SetProperty(ref _isRenaming, value))
                OnPropertyChanged(nameof(IsNotRenaming));
        }
    }

    public bool IsNotRenaming => !IsRenaming;

    /// <summary>Whether the card is asking "really delete?" first, so one stray click never removes a project.</summary>
    public bool IsConfirmingDelete
    {
        get => _isConfirmingDelete;
        internal set
        {
            if (SetProperty(ref _isConfirmingDelete, value))
                OnPropertyChanged(nameof(IsNotConfirmingDelete));
        }
    }

    public bool IsNotConfirmingDelete => !IsConfirmingDelete;

    public RelayCommand OpenCommand { get; }
    public RelayCommand BeginRenameCommand { get; }
    public RelayCommand CommitRenameCommand { get; }
    public RelayCommand CancelRenameCommand { get; }
    public RelayCommand DuplicateCommand { get; }
    public RelayCommand AskDeleteCommand { get; }
    public RelayCommand ConfirmDeleteCommand { get; }
    public RelayCommand CancelDeleteCommand { get; }
}

/// <summary>The Library page: every saved project, to open, rename, copy or delete.</summary>
public sealed class LibraryViewModel : ViewModelBase
{
    private readonly IProjectStore _store;
    private readonly ISurfaceService _surfaces;
    private readonly Func<DateTimeOffset> _now;

    private List<ProjectCard> _all = [];
    private string _search = "";
    private string _message = "";

    /// <param name="store">Where the projects are kept.</param>
    /// <param name="surfaces">Turns the surface of a project into its name.</param>
    /// <param name="now">The current time, for "Today" and "Yesterday". Tests give their own.</param>
    public LibraryViewModel(IProjectStore store, ISurfaceService surfaces, Func<DateTimeOffset>? now = null)
    {
        _store = store;
        _surfaces = surfaces;
        _now = now ?? (() => DateTimeOffset.Now);

        Refresh();
    }

    /// <summary>Raised with the id of a project the user wants to open.</summary>
    public event Action<string>? OpenRequested;

    /// <summary>The cards that match the search, most recently changed first.</summary>
    public IReadOnlyList<ProjectCard> Cards
        => string.IsNullOrWhiteSpace(Search)
            ? _all
            : [.. _all.Where(card => card.Name.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase)
                || card.SurfaceName.Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase))];

    public string Search
    {
        get => _search;
        set
        {
            if (!SetProperty(ref _search, value ?? ""))
                return;

            RaiseLists();
        }
    }

    /// <summary>Nothing has been saved yet.</summary>
    public bool IsEmpty => _all.Count == 0;

    /// <summary>Something is saved, but none of it matches the search.</summary>
    public bool NothingFound => _all.Count > 0 && Cards.Count == 0;

    public bool HasProjects => _all.Count > 0;

    public string CountText => _all.Count switch
    {
        0 => "",
        1 => "1 project",
        var count => $"{count} projects"
    };

    /// <summary>Something to tell the user, such as a project that could not be opened.</summary>
    public string Message
    {
        get => _message;
        set
        {
            if (SetProperty(ref _message, value ?? ""))
                OnPropertyChanged(nameof(HasMessage));
        }
    }

    public bool HasMessage => Message.Length > 0;

    /// <summary>Reads the Library from disk again.</summary>
    public void Refresh()
    {
        foreach (ProjectCard old in _all)
            old.Thumbnail?.Dispose();

        List<ProjectCard> cards = [];
        foreach (ProjectEntry entry in _store.List())
        {
            string surface = _surfaces.Catalog.Surfaces.FirstOrDefault(s => s.Id == entry.Project.SurfaceId)?.Name ?? "An unknown surface";
            cards.Add(new ProjectCard(this, entry, surface, LoadThumbnail(entry.ThumbnailPath), DescribeTime(entry.Project.Modified)));
        }

        _all = cards;
        RaiseLists();
    }

    internal void Open(ProjectCard card)
    {
        Message = "";
        OpenRequested?.Invoke(card.Id);
    }

    internal void Rename(ProjectCard card, string name)
    {
        card.IsRenaming = false;

        if (string.IsNullOrWhiteSpace(name) || name.Trim() == card.Name)
            return;

        _store.Rename(card.Id, name);
        Refresh();
    }

    internal void Duplicate(ProjectCard card)
    {
        _store.Duplicate(card.Id, $"{card.Name} copy");
        Refresh();
    }

    internal void Delete(ProjectCard card)
    {
        _store.Delete(card.Id);
        Refresh();
    }

    private void RaiseLists()
    {
        OnPropertyChanged(nameof(Cards));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(NothingFound));
        OnPropertyChanged(nameof(HasProjects));
        OnPropertyChanged(nameof(CountText));
    }

    private static Bitmap? LoadThumbnail(string? path)
    {
        if (path is null)
            return null;

        try
        {
            return new Bitmap(path);
        }
        catch (Exception ex)
        {
            AppLog.Warn("LibraryViewModel", $"Could not read the thumbnail '{path}'.", ex);
            return null;
        }
    }

    private string DescribeTime(DateTimeOffset time)
    {
        DateTimeOffset local = time.ToLocalTime();
        DateTimeOffset now = _now().ToLocalTime();

        if (local.Date == now.Date)
            return $"Today {local.ToString("t", CultureInfo.CurrentCulture)}";

        if (local.Date == now.Date.AddDays(-1))
            return $"Yesterday {local.ToString("t", CultureInfo.CurrentCulture)}";

        return local.ToString("d MMM yyyy", CultureInfo.CurrentCulture);
    }
}
