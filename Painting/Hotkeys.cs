using RustyPainter.Localization;
using System.Collections.Generic;
using System.Linq;
using System;

namespace RustyPainter.Painting;

/// <summary>What a hotkey does while painting.</summary>
public enum HotkeyAction
{
    /// <summary>Starts painting from the get-ready page, pauses it while running, and resumes it while paused.</summary>
    StartPause,

    /// <summary>Stops painting for good.</summary>
    Abort
}

/// <summary>A key, with the modifier keys that have to be held, that works whichever window is in front.</summary>
/// <param name="Modifiers">The modifiers as Windows numbers them: 1 for Alt, 2 for Ctrl, 4 for Shift.</param>
/// <param name="VirtualKey">The Windows virtual key code.</param>
public readonly record struct Hotkey(uint Modifiers, uint VirtualKey)
{
    public const uint Alt = 1;
    public const uint Ctrl = 2;
    public const uint Shift = 4;

    /// <summary>The key as the user reads it, such as "F8" or "Ctrl+Shift+P".</summary>
    public string Display
    {
        get
        {
            List<string> parts = [];
            if ((Modifiers & Ctrl) != 0) parts.Add("Ctrl");
            if ((Modifiers & Alt) != 0) parts.Add("Alt");
            if ((Modifiers & Shift) != 0) parts.Add("Shift");
            parts.Add(KeyName(VirtualKey));
            return string.Join('+', parts);
        }
    }

    /// <summary>The key as it is written in the current language, for showing. <see cref="Display"/> stays the same in every language, for saving.</summary>
    public string Localized
    {
        get
        {
            List<string> parts = [];
            if ((Modifiers & Ctrl) != 0) parts.Add(Loc.Get("Key_Ctrl"));
            if ((Modifiers & Alt) != 0) parts.Add(Loc.Get("Key_Alt"));
            if ((Modifiers & Shift) != 0) parts.Add(Loc.Get("Key_Shift"));

            string name = KeyName(VirtualKey);
            parts.Add(Loc.Has($"Key_{name}") ? Loc.Get($"Key_{name}") : name);
            return string.Join('+', parts);
        }
    }

    /// <summary>Reads text such as "F8" or "Ctrl+Shift+P". Letters, digits and F1 to F24 can be used as the key.</summary>
    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        uint modifiers = 0;
        uint? key = null;

        foreach (string part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL" or "CONTROL":
                    modifiers |= Ctrl;
                    break;
                case "ALT":
                    modifiers |= Alt;
                    break;
                case "SHIFT":
                    modifiers |= Shift;
                    break;
                default:
                    if (key is not null || ParseKey(part) is not { } parsed)
                        return false;

                    key = parsed;
                    break;
            }
        }

        if (key is null)
            return false;

        hotkey = new Hotkey(modifiers, key.Value);
        return true;
    }

    private static readonly (string Name, uint Key)[] NamedKeys =
    [
        ("Space", 0x20), ("Esc", 0x1B), ("Enter", 0x0D), ("Tab", 0x09), ("Backspace", 0x08), ("Insert", 0x2D), ("Delete", 0x2E),
        ("Home", 0x24), ("End", 0x23), ("PageUp", 0x21), ("PageDown", 0x22), ("Up", 0x26), ("Down", 0x28), ("Left", 0x25), ("Right", 0x27),
        ("Pause", 0x13)
    ];

    /// <summary>Whether a Windows virtual key code is one of the keys that can be used.</summary>
    public static bool IsUsableKey(uint virtualKey)
        => virtualKey is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A or >= 0x70 and <= 0x87 || NamedKeys.Any(named => named.Key == virtualKey);

    private static uint? ParseKey(string name)
    {
        if (name.Equals("Escape", StringComparison.OrdinalIgnoreCase))
            return 0x1B;

        foreach ((string known, uint key) in NamedKeys)
            if (known.Equals(name, StringComparison.OrdinalIgnoreCase))
                return key;

        name = name.ToUpperInvariant();

        if (name.Length == 1 && name[0] is >= 'A' and <= 'Z')
            return name[0];

        if (name.Length == 1 && name[0] is >= '0' and <= '9')
            return name[0];

        if (name.Length is 2 or 3 && name[0] == 'F' && int.TryParse(name[1..], out int number) && number is >= 1 and <= 24)
            return (uint)(0x6F + number);

        return null;
    }

    private static string KeyName(uint virtualKey)
    {
        foreach ((string name, uint key) in NamedKeys)
            if (key == virtualKey)
                return name;

        return virtualKey is >= 0x70 and <= 0x87 ? $"F{virtualKey - 0x6F}" : ((char)virtualKey).ToString();
    }
}

/// <summary>Listens for hotkeys that work while another program, such as the game, is in front.</summary>
public interface IHotkeyService : IDisposable
{
    /// <summary>Raised when a registered hotkey is pressed. Raised on a background thread.</summary>
    event Action<HotkeyAction>? Pressed;

    /// <summary>Whether the hotkeys are registered right now.</summary>
    bool IsActive { get; }

    /// <summary>
    /// Starts listening for the hotkeys, replacing any set before. A hotkey is only taken while the game is in front, so no
    /// other program loses a key.
    /// </summary>
    /// <returns>The actions whose key could not be registered, for example because another program already uses it.</returns>
    IReadOnlyList<HotkeyAction> Start(IReadOnlyDictionary<HotkeyAction, Hotkey> bindings);

    /// <summary>Gives the keys back to everyone else.</summary>
    void Stop();
}

public static class HotkeyDefaults
{
    /// <summary>Space. F8 and the other function keys are bound in the game itself.</summary>
    public static Hotkey StartPause { get; } = new(0, 0x20);

    /// <summary>Escape.</summary>
    public static Hotkey Abort { get; } = new(0, 0x1B);

    /// <summary>Reads the two keys from settings text, falling back to the defaults for anything that cannot be read or that clash.</summary>
    public static IReadOnlyDictionary<HotkeyAction, Hotkey> FromSettings(string? startPause, string? abort)
    {
        // The first versions saved F8 and F9 as the defaults, and F8 is taken in the game, so that pair means "never changed".
        if (startPause == "F8" && abort == "F9")
            (startPause, abort) = (null, null);

        Hotkey first = Hotkey.TryParse(startPause, out Hotkey a) ? a : StartPause;
        Hotkey second = Hotkey.TryParse(abort, out Hotkey b) ? b : Abort;

        // The same key for both could never do the right thing, so keep the defaults.
        if (first == second)
            (first, second) = (StartPause, Abort);

        return new Dictionary<HotkeyAction, Hotkey> { [HotkeyAction.StartPause] = first, [HotkeyAction.Abort] = second };
    }
}

/// <summary>
/// Decides what a key press means. Pure, so it can be tested without a keyboard: the hook only has to ask it.
/// A key is taken only while the game is in front; anywhere else it goes on to the program it was meant for.
/// </summary>
public sealed class HotkeyMatcher(IReadOnlyDictionary<HotkeyAction, Hotkey> bindings)
{
    /// <summary>The action a key press stands for, or <see langword="null"/> if it is none of the hotkeys.</summary>
    /// <param name="virtualKey">The key that went down.</param>
    /// <param name="modifiers">The modifiers that are held, as <see cref="Hotkey.Alt"/>, <see cref="Hotkey.Ctrl"/> and <see cref="Hotkey.Shift"/>.</param>
    /// <param name="gameInFront">Whether the game is the window in front.</param>
    public HotkeyAction? Match(uint virtualKey, uint modifiers, bool gameInFront)
    {
        if (!gameInFront)
            return null;

        foreach ((HotkeyAction action, Hotkey key) in bindings)
            if (key.VirtualKey == virtualKey && key.Modifiers == modifiers)
                return action;

        return null;
    }
}
