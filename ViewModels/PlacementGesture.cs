using System.Numerics;
using System;

namespace RustyPainter.ViewModels;

/// <summary>
/// The movement and zoom of a picture while the mouse is still working on it. The preview does not re-render
/// for every step; it just moves the picture it already has, using <see cref="Matrix"/>, and the real settings
/// are applied once the gesture is done. Everything is in canvas units, the size of the canvas on a 1440p screen.
/// </summary>
public sealed class PlacementGesture
{
    public const double MinZoom = 0.25;
    public const double MaxZoom = 4;

    private double _width, _height;

    // What the shown picture was rendered with.
    private double _baseZoom, _baseX, _baseY;

    // What the user has moved it to. The picture on screen is turned from the first into the second.
    private double _zoom, _x, _y;

    /// <summary>Whether the shown picture differs from the last render, so it has to be moved or scaled.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Whether there are changes that have not been handed to the settings yet.</summary>
    public bool HasUncommitted { get; private set; }

    /// <summary>
    /// Starts from the settings the shown picture was rendered with. Does nothing while a gesture is already
    /// going, because then the shown picture is still the old render.
    /// </summary>
    /// <param name="canvasWidth">The canvas width.</param>
    /// <param name="canvasHeight">The canvas height.</param>
    /// <param name="zoom">The zoom of the shown render.</param>
    /// <param name="offsetXPercent">The horizontal shift of the shown render, in percent of the canvas width.</param>
    /// <param name="offsetYPercent">The vertical shift of the shown render, in percent of the canvas height.</param>
    public void Begin(double canvasWidth, double canvasHeight, double zoom, double offsetXPercent, double offsetYPercent)
    {
        if (IsActive)
            return;

        _width = canvasWidth;
        _height = canvasHeight;
        _baseZoom = _zoom = zoom;
        _baseX = _x = offsetXPercent;
        _baseY = _y = offsetYPercent;
        IsActive = true;
    }

    /// <summary>Moves the picture by canvas units. It cannot go further than the settings allow.</summary>
    public void Pan(double dx, double dy)
    {
        if (!IsActive)
            return;

        _x = Math.Clamp(_x + dx / _width * 100, -100, 100);
        _y = Math.Clamp(_y + dy / _height * 100, -100, 100);
        HasUncommitted = true;
    }

    /// <summary>Zooms about the middle of the picture. It cannot go beyond the zoom limits.</summary>
    public void ZoomBy(double factor)
    {
        if (!IsActive)
            return;

        _zoom = Math.Clamp(_zoom * factor, MinZoom, MaxZoom);
        HasUncommitted = true;
    }

    /// <summary>The zoom the settings should get.</summary>
    public double Zoom => _zoom;

    /// <summary>The horizontal shift the settings should get, in percent of the canvas width.</summary>
    public double OffsetXPercent => _x;

    /// <summary>The vertical shift the settings should get, in percent of the canvas height.</summary>
    public double OffsetYPercent => _y;

    /// <summary>Says the settings now hold everything this gesture changed.</summary>
    public void MarkCommitted() => HasUncommitted = false;

    /// <summary>
    /// A new render arrived while the gesture was still going. It holds what was committed so far, so the
    /// picture only has to be moved by what is left.
    /// </summary>
    public void Rebase(double zoom, double offsetXPercent, double offsetYPercent)
    {
        _baseZoom = zoom;
        _baseX = offsetXPercent;
        _baseY = offsetYPercent;
    }

    /// <summary>Forgets the gesture, once the shown picture has been rendered again with the new settings.</summary>
    public void Reset()
    {
        IsActive = false;
        HasUncommitted = false;
    }

    /// <summary>How the shown picture has to be moved and scaled, from the top left of the canvas.</summary>
    public Matrix3x2 Matrix
    {
        get
        {
            if (!IsActive)
                return Matrix3x2.Identity;

            // The zoom happens about the middle of the picture, which is the middle of the canvas plus the shift it was rendered with.
            Vector2 middle = new((float)(_width / 2 + _baseX / 100 * _width), (float)(_height / 2 + _baseY / 100 * _height));
            Vector2 shift = new((float)((_x - _baseX) / 100 * _width), (float)((_y - _baseY) / 100 * _height));

            return Matrix3x2.CreateTranslation(-middle)
                 * Matrix3x2.CreateScale((float)(_zoom / _baseZoom))
                 * Matrix3x2.CreateTranslation(middle)
                 * Matrix3x2.CreateTranslation(shift);
        }
    }
}
