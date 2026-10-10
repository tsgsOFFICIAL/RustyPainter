using RustyPainter.Interface;
using RustyPainter.Painting;

namespace RustyPainter.Service;

/// <inheritdoc cref="IPaintEngineFactory" />
public sealed class PaintEngineFactory(IGameWindowService game) : IPaintEngineFactory
{
    public PaintEngine Create(PaintTiming timing) => new(new Win32PaintInput(), new Win32PaintSensors(game), timing);
}
