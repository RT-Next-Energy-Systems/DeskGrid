using System.Globalization;

namespace DeskGrid.Hotkeys;

/// <summary>The modifier keys of a <see cref="Hotkey"/>. The values are Win32's <c>MOD_*</c> flags.</summary>
[Flags]
internal enum HotkeyModifiers
{
    None = 0,
    Alt = 0x1,
    Ctrl = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

/// <summary>
/// A keyboard shortcut: modifiers plus one key, written as <c>Ctrl+Alt+M</c>. Only keys that have a clear name
/// (letters, digits, function keys, space, enter, arrows and the navigation keys) are allowed.
/// </summary>
internal readonly record struct Hotkey(HotkeyModifiers Modifiers, int VirtualKey)
{
    // Without one of these a shortcut would take a key away from every other program (typing a capital "M" ...).
    private const HotkeyModifiers RequiredOneOf = HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Win;

    private static readonly (HotkeyModifiers Flag, string Name)[] ModifierNames =
    [
        (HotkeyModifiers.Ctrl, "Ctrl"),
        (HotkeyModifiers.Alt, "Alt"),
        (HotkeyModifiers.Shift, "Shift"),
        (HotkeyModifiers.Win, "Win"),
    ];

    private static readonly Dictionary<int, string> KeyNames = BuildKeyNames();

    /// <summary>Whether it can be registered: a name for the key, and Ctrl, Alt or Win among the modifiers.</summary>
    public bool IsValid => (Modifiers & RequiredOneOf) != 0 && KeyNames.ContainsKey(VirtualKey);

    /// <summary>Whether <paramref name="virtualKey"/> is a key a shortcut can use.</summary>
    public static bool IsKnownKey(int virtualKey) => KeyNames.ContainsKey(virtualKey);

    /// <summary>Reads <c>Ctrl+Alt+M</c> (any order and case; <c>Control</c> and <c>Windows</c> also work). Anything else is <see langword="false"/>.</summary>
    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        int? key = null;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries))
        {
            if (ModifierOf(part) is { } modifier)
            {
                modifiers |= modifier;
            }
            else if (key is null && VirtualKeyOf(part) is { } virtualKey)
            {
                key = virtualKey;
            }
            else
            {
                return false;
            }
        }

        if (key is not { } found)
        {
            return false;
        }

        var parsed = new Hotkey(modifiers, found);
        if (!parsed.IsValid)
        {
            return false;
        }

        hotkey = parsed;
        return true;
    }

    /// <summary>The same as <see cref="TryParse"/>, or <see langword="null"/> when the text isn't a usable shortcut.</summary>
    public static Hotkey? Parse(string? text) => TryParse(text, out var hotkey) ? hotkey : null;

    /// <summary>The text for the key alone ("M", "F5", "Space"), or its number when it has no name.</summary>
    public static string NameOf(int virtualKey) =>
        KeyNames.TryGetValue(virtualKey, out var name) ? name : virtualKey.ToString(CultureInfo.InvariantCulture);

    /// <summary>The modifiers held down, e.g. <c>Ctrl+Alt+</c>; what is shown while the keys are still being pressed.</summary>
    public static string ModifierPrefix(HotkeyModifiers modifiers) =>
        string.Concat(ModifierNames.Where(m => modifiers.HasFlag(m.Flag)).Select(m => m.Name + "+"));

    public override string ToString() => ModifierPrefix(Modifiers) + NameOf(VirtualKey);

    private static HotkeyModifiers? ModifierOf(string name) => name.ToLowerInvariant() switch
    {
        "ctrl" or "control" => HotkeyModifiers.Ctrl,
        "alt" => HotkeyModifiers.Alt,
        "shift" => HotkeyModifiers.Shift,
        "win" or "windows" => HotkeyModifiers.Win,
        _ => null,
    };

    private static int? VirtualKeyOf(string name)
    {
        foreach (var (virtualKey, keyName) in KeyNames)
        {
            if (string.Equals(keyName, name, StringComparison.OrdinalIgnoreCase))
            {
                return virtualKey;
            }
        }

        return null;
    }

    private static Dictionary<int, string> BuildKeyNames()
    {
        var names = new Dictionary<int, string>
        {
            [0x20] = "Space",
            [0x0D] = "Enter",
            [0x21] = "PageUp",
            [0x22] = "PageDown",
            [0x23] = "End",
            [0x24] = "Home",
            [0x25] = "Left",
            [0x26] = "Up",
            [0x27] = "Right",
            [0x28] = "Down",
            [0x2D] = "Insert",
            [0x2E] = "Delete",
        };

        for (var digit = 0; digit <= 9; digit++)
        {
            names[0x30 + digit] = digit.ToString(CultureInfo.InvariantCulture);
        }

        for (var letter = 0; letter < 26; letter++)
        {
            names[0x41 + letter] = ((char)('A' + letter)).ToString();
        }

        for (var function = 1; function <= 24; function++)
        {
            names[0x6F + function] = $"F{function}";
        }

        return names;
    }
}
