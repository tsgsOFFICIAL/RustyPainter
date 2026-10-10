using RustyPainter.Imaging;
using RustyPainter.Interface;

namespace RustyPainter.Painting;

/// <summary>Moves the mouse and presses keys and buttons, as if a person did. Everything is in screen pixels.</summary>
public interface IPaintInput
{
    /// <summary>Puts the cursor at a point.</summary>
    void MoveTo(ScreenPoint point);

    /// <summary>Presses the left mouse button.</summary>
    void LeftDown();

    /// <summary>Releases the left mouse button.</summary>
    void LeftUp();

    /// <summary>Turns the mouse wheel. A positive number turns it forward (up, away from the person), a negative one backward.</summary>
    void Scroll(int notches);

    /// <summary>Presses and releases the key that types a digit, a letter or a full stop.</summary>
    void PressKey(char key);
}

/// <summary>What the painter needs to know about the machine before it acts, so it never acts blindly.</summary>
public interface IPaintSensors
{
    /// <summary>Whether the game window is the one in front, so input goes to it and nothing else.</summary>
    bool IsGameInFront();

    /// <summary>Where the cursor is now.</summary>
    ScreenPoint CursorPosition { get; }

    /// <summary>The color of the screen at a point, or <see langword="null"/> if it cannot be read.</summary>
    Rgb? ReadColor(ScreenPoint point);
}
