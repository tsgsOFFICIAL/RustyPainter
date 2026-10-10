using RustyPainter.Service.Logging;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System;

namespace RustyPainter.Library;

/// <summary>Keeps projects on disk, one folder each: the settings, the picture and a small picture of the result.</summary>
public interface IProjectStore
{
    /// <summary>The folder the projects are kept in.</summary>
    string Root { get; }

    /// <summary>Every project that can be read, the most recently changed first. Damaged ones are left out.</summary>
    IReadOnlyList<ProjectEntry> List();

    /// <summary>The settings and the picture of a project, or <see langword="null"/> if it cannot be read.</summary>
    (StudioProject Project, byte[] Picture)? Load(string id);

    /// <summary>
    /// Saves a project, replacing the one with the same id. A project without an id gets one.
    /// </summary>
    /// <param name="project">The settings.</param>
    /// <param name="picture">The picture as a PNG file.</param>
    /// <param name="thumbnail">A small picture of the result as a PNG file, or <see langword="null"/>.</param>
    /// <returns>The project as saved, with its id and its time of change.</returns>
    /// <param name="framePictures">The pictures of the second frame onward of an animated sign, as PNG files, in order.</param>
    StudioProject Save(StudioProject project, byte[] picture, byte[]? thumbnail, IReadOnlyList<byte[]>? framePictures = null);

    /// <summary>The pictures of the second frame onward of an animated sign. A frame whose picture is missing is <see langword="null"/>.</summary>
    IReadOnlyList<byte[]?> LoadFramePictures(string id);

    /// <summary>Changes the name of a project.</summary>
    void Rename(string id, string name);

    /// <summary>Makes a copy of a project under a new name and returns it.</summary>
    StudioProject? Duplicate(string id, string name);

    /// <summary>Removes a project and everything saved with it.</summary>
    void Delete(string id);
}

/// <inheritdoc cref="IProjectStore" />
public sealed class ProjectStore : IProjectStore
{
    private const string SettingsFile = "project.json";
    private const string PictureFile = "picture.png";
    private const string ThumbnailFile = "thumbnail.png";

    private static string FrameFile(int number) => $"frame-{number}.png";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _root;

    public string Root => _root;

    /// <summary>The folder the projects of the real app live in.</summary>
    public static string DefaultRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RustyPainter", "Library");

    /// <param name="root">The folder to keep projects in. Tests give their own, so they never touch the real Library.</param>
    public ProjectStore(string? root = null) => _root = root ?? DefaultRoot;

    public IReadOnlyList<ProjectEntry> List()
    {
        if (!Directory.Exists(_root))
            return [];

        List<ProjectEntry> entries = [];

        foreach (string folder in Directory.EnumerateDirectories(_root))
        {
            string id = Path.GetFileName(folder);

            try
            {
                StudioProject? project = ReadSettings(id);
                if (project is null || !File.Exists(Path.Combine(folder, PictureFile)))
                    continue;

                string thumbnail = Path.Combine(folder, ThumbnailFile);
                entries.Add(new ProjectEntry(project, File.Exists(thumbnail) ? thumbnail : null));
            }
            catch (Exception ex)
            {
                AppLog.Warn("ProjectStore", $"Skipping the project '{id}', which cannot be read.", ex);
            }
        }

        return [.. entries.OrderByDescending(entry => entry.Project.Modified)];
    }

    public (StudioProject Project, byte[] Picture)? Load(string id)
    {
        try
        {
            if (!IsSafeId(id) || ReadSettings(id) is not { } project)
                return null;

            string picture = Path.Combine(Folder(id), PictureFile);
            return File.Exists(picture) ? (project, File.ReadAllBytes(picture)) : null;
        }
        catch (Exception ex)
        {
            AppLog.Warn("ProjectStore", $"Could not load the project '{id}'.", ex);
            return null;
        }
    }

    public StudioProject Save(StudioProject project, byte[] picture, byte[]? thumbnail, IReadOnlyList<byte[]>? framePictures = null)
    {
        string id = IsSafeId(project.Id) ? project.Id : NewId();
        StudioProject saved = project with { Id = id, Name = CleanName(project.Name), Modified = DateTimeOffset.UtcNow, MoreFrames = new FrameList(project.MoreFrames.Take(framePictures?.Count ?? 0)) };
        string folder = Folder(id);
        Directory.CreateDirectory(folder);

        // Each file is written beside its place and then moved into it, so a crash in the middle never leaves half a file.
        WriteAtomically(Path.Combine(folder, PictureFile), picture);

        // The pictures of the other frames follow the frames listed in the settings, and any left over from before are removed.
        int frames = framePictures?.Count ?? 0;
        for (int i = 0; i < frames; i++)
            WriteAtomically(Path.Combine(folder, FrameFile(i + 2)), framePictures![i]);

        foreach (string old in Directory.EnumerateFiles(folder, "frame-*.png"))
            if (!Enumerable.Range(2, frames).Any(number => Path.GetFileName(old) == FrameFile(number)))
                File.Delete(old);

        string thumbnailPath = Path.Combine(folder, ThumbnailFile);
        if (thumbnail is not null)
            WriteAtomically(thumbnailPath, thumbnail);
        else if (File.Exists(thumbnailPath))
            File.Delete(thumbnailPath);

        WriteAtomically(Path.Combine(folder, SettingsFile), JsonSerializer.SerializeToUtf8Bytes(saved, Options));
        return saved;
    }

    public IReadOnlyList<byte[]?> LoadFramePictures(string id)
    {
        try
        {
            if (!IsSafeId(id) || ReadSettings(id) is not { } project)
                return [];

            return [.. Enumerable.Range(2, project.MoreFrames.Count).Select(number =>
            {
                string path = Path.Combine(Folder(id), FrameFile(number));
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            })];
        }
        catch (Exception ex)
        {
            AppLog.Warn("ProjectStore", $"Could not load the frames of the project '{id}'.", ex);
            return [];
        }
    }

    public void Rename(string id, string name)
    {
        if (!IsSafeId(id) || ReadSettings(id) is not { } project)
            return;

        // Renaming is not an edit of the picture, so the time of change is kept and the order of the Library stays.
        StudioProject renamed = project with { Name = CleanName(name) };
        WriteAtomically(Path.Combine(Folder(id), SettingsFile), JsonSerializer.SerializeToUtf8Bytes(renamed, Options));
    }

    public StudioProject? Duplicate(string id, string name)
    {
        if (Load(id) is not { } loaded)
            return null;

        string thumbnail = Path.Combine(Folder(id), ThumbnailFile);
        byte[]? thumbnailBytes = File.Exists(thumbnail) ? File.ReadAllBytes(thumbnail) : null;

        List<byte[]> frames = [.. LoadFramePictures(id).OfType<byte[]>()];
        return Save(loaded.Project with { Id = "", Name = name, Created = DateTimeOffset.UtcNow }, loaded.Picture, thumbnailBytes, frames);
    }

    public void Delete(string id)
    {
        if (!IsSafeId(id) || !Directory.Exists(Folder(id)))
            return;

        try
        {
            Directory.Delete(Folder(id), recursive: true);
        }
        catch (Exception ex)
        {
            AppLog.Warn("ProjectStore", $"Could not delete the project '{id}'.", ex);
        }
    }

    private string Folder(string id) => Path.Combine(_root, id);

    private StudioProject? ReadSettings(string id)
    {
        string path = Path.Combine(Folder(id), SettingsFile);
        if (!File.Exists(path))
            return null;

        StudioProject? project = JsonSerializer.Deserialize<StudioProject>(File.ReadAllBytes(path), Options);

        // The folder name is the id, whatever the file says, so a copied folder still works.
        return project is null || string.IsNullOrWhiteSpace(project.SurfaceId) ? null : project with { Id = id };
    }

    private static void WriteAtomically(string path, byte[] bytes)
    {
        string temporary = path + ".tmp";
        File.WriteAllBytes(temporary, bytes);
        File.Move(temporary, path, overwrite: true);
    }

    private static string NewId() => Guid.NewGuid().ToString("N")[..12];

    /// <summary>An id is the name of a folder, so nothing that could point somewhere else is allowed.</summary>
    private static bool IsSafeId(string id)
        => id.Length is > 0 and <= 64 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static string CleanName(string name)
    {
        string cleaned = string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return cleaned.Length == 0 ? "Untitled" : cleaned.Length > 80 ? cleaned[..80] : cleaned;
    }
}
