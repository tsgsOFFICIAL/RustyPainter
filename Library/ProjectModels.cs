using System;

namespace RustyPainter.Library;

/// <summary>Where a project's picture came from, which says how it is brought back.</summary>
public enum ProjectSource
{
    /// <summary>A picture the user loaded. It is kept as a file next to the project.</summary>
    Picture,

    /// <summary>Typed text. The text and its look are kept, so it can still be edited.</summary>
    Text
}

/// <summary>
/// Everything about how a picture is set up in Studio, so reopening it gives the same result. The picture itself is
/// kept next to this as a file.
/// </summary>
public sealed record StudioProject
{
    /// <summary>The version of this format, so projects saved by this version can still be read by later ones.</summary>
    public int Version { get; init; } = 1;

    /// <summary>A name that never changes, used as the project's folder.</summary>
    public string Id { get; init; } = "";

    /// <summary>The name the user sees.</summary>
    public string Name { get; init; } = "";

    public DateTimeOffset Created { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset Modified { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>The surface it is painted on.</summary>
    public string SurfaceId { get; init; } = "";

    public ProjectSource Source { get; init; } = ProjectSource.Picture;

    /// <summary>The name of the picture file it came from, for showing.</summary>
    public string SourceName { get; init; } = "";

    // Placement
    public string Placement { get; init; } = "Fill";
    public double Zoom { get; init; } = 1;
    public double OffsetX { get; init; }
    public double OffsetY { get; init; }
    public int QuarterTurns { get; init; }
    public bool FlipX { get; init; }
    public bool FlipY { get; init; }

    // Colors
    public string ColorMode { get; init; } = "Swatches";
    public int MaxColors { get; init; } = 16;
    public string Dithering { get; init; } = "Smooth";
    public string Filter { get; init; } = "None";
    public double Brightness { get; init; }
    public double Contrast { get; init; }
    public double Saturation { get; init; }
    public bool FillBackground { get; init; }
    public string BackgroundColor { get; init; } = "#000000";

    /// <summary>The brush size in units, or which of the fixed brushes for surfaces such as neon signs.</summary>
    public int Quality { get; init; } = 4;

    // Text pictures
    public string Text { get; init; } = "";
    public string TextColor { get; init; } = "#FFFFFF";
    public bool TextBold { get; init; } = true;
    public bool TextSolidBackground { get; init; }
    public string TextBackgroundColor { get; init; } = "#000000";
}

/// <summary>A project as the Library shows it.</summary>
/// <param name="Project">Its settings.</param>
/// <param name="ThumbnailPath">The small picture of the result, or <see langword="null"/> when there is none.</param>
public sealed record ProjectEntry(StudioProject Project, string? ThumbnailPath);
