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

    private static uint? ParseKey(string name)
    {
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
        => virtualKey is >= 0x70 and <= 0x87 ? $"F{virtualKey - 0x6F}" : ((char)virtualKey).ToString();
}

/// <summary>Listens for hotkeys that work while another program, such as the game, is in front.</summary>
public interface IHotkeyService : IDisposable
{
    /// <summary>Raised when a registered hotkey is pressed. Raised on a background thread.</summary>
    event Action<HotkeyAction>? Pressed;

    /// <summary>Whether the hotkeys are registered right now.</summary>
    bool IsActive { get; }

    /// <summary>
    /// Registers the hotkeys, replacing any registered before. While registered no other program gets the keys.
    /// </summary>
    /// <returns>The actions whose key could not be registered, for example because another program already uses it.</returns>
    IReadOnlyList<HotkeyAction> Start(IReadOnlyDictionary<HotkeyAction, Hotkey> bindings);

    /// <summary>Gives the keys back to everyone else.</summary>
    void Stop();
}

public static class HotkeyDefaults
{
    public static Hotkey StartPause { get; } = new(0, 0x77);   // F8

    public static Hotkey Abort { get; } = new(0, 0x78);        // F9

    /// <summary>Reads the two keys from settings text, falling back to the defaults for anything that cannot be read or that clash.</summary>
    public static IReadOnlyDictionary<HotkeyAction, Hotkey> FromSettings(string? startPause, string? abort)
    {
        Hotkey first = Hotkey.TryParse(startPause, out Hotkey a) ? a : StartPause;
        Hotkey second = Hotkey.TryParse(abort, out Hotkey b) ? b : Abort;

        // The same key for both could never do the right thing, so keep the defaults.
        if (first == second)
            (first, second) = (StartPause, Abort);

        return new Dictionary<HotkeyAction, Hotkey> { [HotkeyAction.StartPause] = first, [HotkeyAction.Abort] = second };
    }
}
