using System.Text;

namespace Orbix.Core.Hotkeys;

/// <summary>Modifier flags. The values intentionally match the Win32 <c>MOD_*</c> constants of RegisterHotKey.</summary>
[Flags]
public enum KeyModifiers
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Win = 0x0008,
}

/// <summary>
/// A global keyboard shortcut: modifiers + Win32 virtual-key code, convertible to / from text such as
/// "Ctrl+Alt+Space".
/// </summary>
public readonly record struct HotkeyGesture(KeyModifiers Modifiers, int VirtualKey)
{
    public static readonly HotkeyGesture None = new(KeyModifiers.None, 0);

    private static readonly Dictionary<string, int> NameToKey = BuildNameTable();
    private static readonly Dictionary<int, string> KeyToName = BuildReverseTable();

    public bool IsEmpty => VirtualKey == 0;

    /// <summary>
    /// A usable global hotkey: has a key and at least one modifier
    /// (F13..F24 are allowed without modifiers because they are not used by applications).
    /// </summary>
    public bool IsValid => VirtualKey != 0 && (Modifiers != KeyModifiers.None || (VirtualKey >= 0x7C && VirtualKey <= 0x87));

    public override string ToString()
    {
        if (IsEmpty)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        if (Modifiers.HasFlag(KeyModifiers.Control)) sb.Append("Ctrl+");
        if (Modifiers.HasFlag(KeyModifiers.Alt)) sb.Append("Alt+");
        if (Modifiers.HasFlag(KeyModifiers.Shift)) sb.Append("Shift+");
        if (Modifiers.HasFlag(KeyModifiers.Win)) sb.Append("Win+");
        sb.Append(KeyName(VirtualKey));
        return sb.ToString();
    }

    /// <summary>Human readable name of a virtual key ("Space", "F5", "A", "0x5B" when unknown).</summary>
    public static string KeyName(int virtualKey) =>
        KeyToName.TryGetValue(virtualKey, out var name) ? name : "0x" + virtualKey.ToString("X2");

    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = None;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var modifiers = KeyModifiers.None;
        int key = 0;

        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                case "ctl":
                    modifiers |= KeyModifiers.Control;
                    continue;
                case "alt":
                    modifiers |= KeyModifiers.Alt;
                    continue;
                case "shift":
                    modifiers |= KeyModifiers.Shift;
                    continue;
                case "win":
                case "windows":
                case "super":
                case "meta":
                    modifiers |= KeyModifiers.Win;
                    continue;
            }

            if (key != 0)
            {
                return false; // two non-modifier keys
            }

            if (NameToKey.TryGetValue(raw, out var vk))
            {
                key = vk;
            }
            else if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                     int.TryParse(raw.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out var hex) &&
                     hex > 0 && hex < 0xFF)
            {
                key = hex;
            }
            else
            {
                return false;
            }
        }

        if (key == 0)
        {
            return false;
        }

        gesture = new HotkeyGesture(modifiers, key);
        return true;
    }

    public static HotkeyGesture Parse(string text) =>
        TryParse(text, out var g) ? g : throw new FormatException($"Invalid hotkey: '{text}'.");

    private static Dictionary<string, int> BuildNameTable()
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (char c = 'A'; c <= 'Z'; c++) map[c.ToString()] = c;
        for (char c = '0'; c <= '9'; c++) map[c.ToString()] = c;
        for (int i = 1; i <= 24; i++) map["F" + i] = 0x70 + i - 1;
        for (int i = 0; i <= 9; i++) map["Num" + i] = 0x60 + i;

        map["Space"] = 0x20;
        map["Enter"] = 0x0D;
        map["Return"] = 0x0D;
        map["Tab"] = 0x09;
        map["Esc"] = 0x1B;
        map["Escape"] = 0x1B;
        map["Backspace"] = 0x08;
        map["Back"] = 0x08;
        map["Insert"] = 0x2D;
        map["Ins"] = 0x2D;
        map["Delete"] = 0x2E;
        map["Del"] = 0x2E;
        map["Home"] = 0x24;
        map["End"] = 0x23;
        map["PageUp"] = 0x21;
        map["PgUp"] = 0x21;
        map["PageDown"] = 0x22;
        map["PgDn"] = 0x22;
        map["Left"] = 0x25;
        map["Up"] = 0x26;
        map["Right"] = 0x27;
        map["Down"] = 0x28;
        map["Pause"] = 0x13;
        map["PrintScreen"] = 0x2C;
        map["PrtSc"] = 0x2C;
        map["ScrollLock"] = 0x91;
        map["NumMultiply"] = 0x6A;
        map["NumAdd"] = 0x6B;
        map["NumSubtract"] = 0x6D;
        map["NumDecimal"] = 0x6E;
        map["NumDivide"] = 0x6F;
        map["`"] = 0xC0;
        map["-"] = 0xBD;
        map["="] = 0xBB;
        map["["] = 0xDB;
        map["]"] = 0xDD;
        map["\\"] = 0xDC;
        map[";"] = 0xBA;
        map["'"] = 0xDE;
        map[","] = 0xBC;
        map["."] = 0xBE;
        map["/"] = 0xBF;
        return map;
    }

    private static Dictionary<int, string> BuildReverseTable()
    {
        // First name wins, so the canonical spelling is listed first.
        var reverse = new Dictionary<int, string>();
        var canonical = new[]
        {
            "Space", "Enter", "Tab", "Esc", "Backspace", "Insert", "Delete", "Home", "End", "PageUp", "PageDown",
            "Left", "Up", "Right", "Down", "Pause", "PrintScreen", "ScrollLock",
        };

        foreach (var name in canonical)
        {
            reverse[NameToKey[name]] = name;
        }

        foreach (var pair in NameToKey)
        {
            reverse.TryAdd(pair.Value, pair.Key);
        }

        return reverse;
    }
}
