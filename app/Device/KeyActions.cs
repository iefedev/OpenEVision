namespace OpenEVision.Device;

public sealed record KeyAction(string Id, string Label, string Group);

/// <summary>
/// What a key-table entry can be set to: "k:XX" key usage [02 02 XX], "m:XX" modifier [02 01 XX],
/// "c:XXXX" consumer/media [03 lo hi], "off" disabled [02 02 00]. Fn ([05 ..]) is not remappable.
/// </summary>
public static class KeyActions
{
    static readonly (int Usage, string Name)[] Media =
    [
        (0xE9, "Volume up"), (0xEA, "Volume down"), (0xE2, "Mute"), (0xCD, "Play/Pause"), (0xB5, "Next track"),
        (0xB6, "Previous track"), (0xB7, "Stop"), (0x183, "Media player"), (0x192, "Calculator"),
        (0x196, "Browser"), (0x18A, "Email"), (0x194, "My Computer"),
    ];

    public static readonly IReadOnlyList<KeyAction> All = Build();

    static string Group(string code) =>
        code.StartsWith("Key") ? "Letters" :
        code.StartsWith("Digit") ? "Digits" :
        code.Length > 1 && code[0] == 'F' && char.IsDigit(code[1]) ? "Function keys" :
        code.StartsWith("Numpad") || code == "NumLock" ? "Numpad" :
        code.StartsWith("Arrow") || code is "Insert" or "Delete" or "Home" or "End" or "PageUp" or "PageDown" ? "Navigation" : "Other";

    static List<KeyAction> Build()
    {
        var labels = KeyLayout.Keys.GroupBy(k => k.Code).ToDictionary(g => g.Key, g => g.First().Label);
        labels["Space"] = "Space";
        labels["MetaRight"] = "Right Win";
        string L(string code) => $"{(labels.TryGetValue(code, out var l) && l != "" ? l : code)} ({code})";

        var list = new List<KeyAction>();
        foreach (var (u, code) in LiveMap.Usages.OrderBy(x => x.Key)) list.Add(new($"k:{u:x2}", L(code), Group(code)));
        foreach (var (bit, code) in LiveMap.Modifiers) list.Add(new($"m:{bit:x2}", L(code), "Modifiers"));
        foreach (var (u, name) in Media) list.Add(new($"c:{u:x4}", name, "Media"));
        list.Add(new("off", "Disabled", "Special"));
        return list;
    }

    public static string Label(string? id) => All.FirstOrDefault(a => a.Id == id)?.Label ?? id ?? "?";

    public static byte[] ToBytes(string id)
    {
        if (id == "off") return [0x02, 0x02, 0x00];
        var parts = id.Split(':');
        int v = Convert.ToInt32(parts[1], 16);
        return parts[0] switch
        {
            "k" when LiveMap.Usages.ContainsKey(v) => [0x02, 0x02, (byte)v],
            "m" when LiveMap.Modifiers.ContainsKey(v) => [0x02, 0x01, (byte)v],
            "c" when Media.Any(m => m.Usage == v) => [0x03, (byte)v, (byte)(v >> 8)],
            _ => throw new ArgumentException($"Unknown key assignment: {id}"),
        };
    }

    /// <summary>Action id of a table entry, or null for something not modelled (e.g. Fn).</summary>
    public static string? FromBytes(ReadOnlySpan<byte> e) => (e[0], e[1]) switch
    {
        (0x02, 0x02) => e[2] == 0 ? "off" : $"k:{e[2]:x2}",
        (0x02, 0x01) => $"m:{e[2]:x2}",
        (0x03, _) => $"c:{e[1] | e[2] << 8:x4}",
        _ => null,
    };
}
