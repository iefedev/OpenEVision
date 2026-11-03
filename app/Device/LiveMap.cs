namespace OpenEVision.Device;

/// <summary>
/// Live (RAM, command 0x12) LED order = the keyboard's key table: 3-byte entries, 8 per matrix column,
/// entry index == live LED index. Also the HID usage names used by the remap page.
/// </summary>
public static class LiveMap
{
    static readonly Dictionary<int, string> Usage = BuildUsage();
    static readonly Dictionary<int, string> Modifier = new()
    {
        [0x01] = "ControlLeft", [0x02] = "ShiftLeft", [0x04] = "AltLeft", [0x08] = "MetaLeft",
        [0x10] = "ControlRight", [0x20] = "ShiftRight", [0x40] = "AltRight", [0x80] = "MetaRight",
    };

    static Dictionary<int, string> BuildUsage()
    {
        var u = new Dictionary<int, string>
        {
            [0x28] = "Enter", [0x29] = "Escape", [0x2A] = "Backspace", [0x2B] = "Tab", [0x2C] = "Space", [0x2D] = "Minus",
            [0x2E] = "Equal", [0x2F] = "BracketLeft", [0x30] = "BracketRight", [0x32] = "Backslash", [0x33] = "Semicolon",
            [0x34] = "Quote", [0x35] = "Backquote", [0x36] = "Comma", [0x37] = "Period", [0x38] = "Slash", [0x39] = "CapsLock",
            [0x46] = "PrintScreen", [0x47] = "ScrollLock", [0x48] = "Pause", [0x49] = "Insert", [0x4A] = "Home", [0x4B] = "PageUp",
            [0x4C] = "Delete", [0x4D] = "End", [0x4E] = "PageDown", [0x4F] = "ArrowRight", [0x50] = "ArrowLeft",
            [0x51] = "ArrowDown", [0x52] = "ArrowUp", [0x53] = "NumLock", [0x54] = "NumpadDivide", [0x55] = "NumpadMultiply",
            [0x56] = "NumpadSubtract", [0x57] = "NumpadAdd", [0x58] = "NumpadEnter", [0x62] = "Numpad0", [0x63] = "NumpadDecimal",
            [0x64] = "IntlBackslash", [0x65] = "ContextMenu",
        };
        for (int i = 0; i < 26; i++) u[0x04 + i] = $"Key{(char)('A' + i)}";
        for (int i = 0; i < 10; i++) u[0x1E + i] = $"Digit{(i + 1) % 10}";
        for (int i = 0; i < 12; i++) u[0x3A + i] = $"F{i + 1}";
        for (int i = 0; i < 9; i++) u[0x59 + i] = $"Numpad{i + 1}";
        return u;
    }

    /// <summary>key code -> live index, decoded from a key table (3 bytes per entry).</summary>
    public static Dictionary<string, int> Decode(ReadOnlySpan<byte> table)
    {
        var map = new Dictionary<string, int>();
        for (int i = 0; i * 3 + 2 < table.Length; i++)
        {
            byte kind = table[i * 3], page = table[i * 3 + 1], usage = table[i * 3 + 2];
            string? code = kind switch
            {
                0x02 when page == 0x02 => Usage.GetValueOrDefault(usage),
                0x02 when page == 0x01 => Modifier.GetValueOrDefault(usage),
                0x05 => "Fn",
                _ => null,
            };
            if (code != null) map.TryAdd(code, i);
        }
        return map;
    }

    /// <summary>Inverse of <see cref="Decode"/> for one key: the factory-style entry for a key code, or null.</summary>
    public static byte[]? Encode(string code)
    {
        if (code == "Fn") return [0x05, 0x00, 0x00];
        foreach (var (u, name) in Usage) if (name == code) return [0x02, 0x02, (byte)u];
        foreach (var (bit, name) in Modifier) if (name == code) return [0x02, 0x01, (byte)bit];
        return null;
    }

    public static IReadOnlyDictionary<int, string> Usages => Usage;
    public static IReadOnlyDictionary<int, string> Modifiers => Modifier;
}
