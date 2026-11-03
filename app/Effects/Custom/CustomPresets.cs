namespace OpenEVision.Effects.Custom;

/// <summary>Examples created on first run (they are ordinary custom effects: editable, deletable).</summary>
public static class CustomPresets
{
    public const string PlasmaEraseId = "preset-plasma", GamingId = "preset-gaming", MusicId = "preset-music";

    static LayerDef L(string kind, Blend blend = Blend.Normal, double opacity = 1, List<string>? keys = null,
        ModSource mod = ModSource.None, params (string Key, object Value)[] ps) => new()
    {
        Kind = kind, Blend = blend, Opacity = opacity, Keys = keys, Mod = mod,
        Params = ps.ToDictionary(x => x.Key, x => LayerKinds.ToJson(x.Value)),
    };

    static readonly List<string> Wasd = ["KeyW", "KeyA", "KeyS", "KeyD"];
    static readonly List<string> FRow = ["Escape", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12"];

    public static IEnumerable<CustomEffectDef> Create() =>
    [
        new() { Id = PlasmaEraseId, Name = "Plasma + keys go dark when pressed", Layers =
        [
            L("plasma"),
            L("keypress", Blend.Erase, ps: [("duration", 1.5)]),
        ] },
        new() { Id = GamingId, Name = "Gaming: fire + red WASD", Layers =
        [
            L("fire", opacity: 0.6),
            L("solid", keys: Wasd, ps: [("color", "ff0000")]),
            L("ripple", Blend.Add, ps: [("color", "ffa000"), ("random", false), ("duration", 0.8)]),
        ] },
        new() { Id = MusicId, Name = "Music pulse + white ripples", Layers =
        [
            L("gradient", mod: ModSource.Bass, ps: [("color1", "6000ff"), ("color2", "ff0080"), ("speed", 0.15)]),
            L("audio", keys: FRow),
            L("ripple", Blend.Add, ps: [("color", "ffffff"), ("random", false)]),
        ] },
    ];
}
