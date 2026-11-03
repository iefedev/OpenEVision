using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenEVision.Device;

namespace OpenEVision.Effects.Custom;

public enum Blend { Normal, Add, Multiply, Erase }

public enum ModSource { None, Audio, Bass, Cpu, GpuLoad, GpuTemp }

/// <summary>One layer of a custom effect. Params are typed by the layer kind's ParamDefs.</summary>
public sealed class LayerDef
{
    public string Kind { get; set; } = "solid";
    public bool Enabled { get; set; } = true;
    public Blend Blend { get; set; } = Blend.Normal;
    public double Opacity { get; set; } = 1;
    /// <summary>Key codes this layer applies to; null = whole keyboard.</summary>
    public List<string>? Keys { get; set; }
    public Dictionary<string, JsonElement> Params { get; set; } = new();
    /// <summary>Drives this layer's opacity from a live signal: opacity *= (1 - ModAmount) + ModAmount * signal.</summary>
    public ModSource Mod { get; set; } = ModSource.None;
    public double ModAmount { get; set; } = 1;

    public LayerDef Clone() => JsonSerializer.Deserialize<LayerDef>(JsonSerializer.Serialize(this, CustomEffectStore.Json), CustomEffectStore.Json)!;
}

/// <summary>A user-made effect: an ordered layer stack (first = bottom).</summary>
public sealed class CustomEffectDef
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Name { get; set; } = "New effect";
    public List<LayerDef> Layers { get; set; } = [];

    public CustomEffectDef Clone() => JsonSerializer.Deserialize<CustomEffectDef>(JsonSerializer.Serialize(this, CustomEffectStore.Json), CustomEffectStore.Json)!;
}

/// <summary>Custom effects as one JSON file each in the data folder's effects\ (also the export format).</summary>
public static class CustomEffectStore
{
    public static readonly string Dir = Path.Combine(Storage.Root, "effects");
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static event Action? Changed;

    public static List<CustomEffectDef> All()
    {
        Directory.CreateDirectory(Dir);
        if (!Storage.Get("customPresetsCreated", false))
        {
            foreach (var p in CustomPresets.Create()) Save(p, notify: false);
            Storage.Set("customPresetsCreated", true);
        }
        var list = new List<CustomEffectDef>();
        foreach (var f in Directory.GetFiles(Dir, "*.json"))
        {
            try { if (Load(f) is { } d) list.Add(d); } catch { /* broken file: skip */ }
        }
        return list.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static CustomEffectDef? Find(string id) => All().FirstOrDefault(d => d.Id == id);

    public static CustomEffectDef? Load(string path) => JsonSerializer.Deserialize<CustomEffectDef>(File.ReadAllText(path), Json);

    public static void Save(CustomEffectDef d, bool notify = true)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(Path.Combine(Dir, d.Id + ".json"), JsonSerializer.Serialize(d, Json));
        if (notify) Changed?.Invoke();
    }

    public static void Delete(string id)
    {
        File.Delete(Path.Combine(Dir, id + ".json"));
        Changed?.Invoke();
    }

    public static void Export(CustomEffectDef d, string path) => File.WriteAllText(path, JsonSerializer.Serialize(d, Json));

    /// <summary>Imports a file as a new effect (fresh id, so it never overwrites an existing one).</summary>
    public static CustomEffectDef Import(string path)
    {
        var d = Load(path) ?? throw new InvalidDataException("The file does not contain an effect.");
        foreach (var l in d.Layers)
            if (!LayerKinds.ByName.ContainsKey(l.Kind)) throw new InvalidDataException($"Unknown layer type: {l.Kind}");
        d.Id = Guid.NewGuid().ToString("N")[..12];
        Save(d);
        return d;
    }
}
