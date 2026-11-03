using System.Text.Json;
using OpenEVision.Device;

namespace OpenEVision.Effects;

/// <summary>Remembered effect parameters, brightness and the last running effect (resumed on next start).</summary>
public static class EffectSettings
{
    static readonly Dictionary<string, Dictionary<string, object>> Cache = new();
    static readonly Timer Flush = new(_ => Save(), null, Timeout.Infinite, Timeout.Infinite);
    static bool _dirty;

    /// <summary>Live parameter dictionary of an effect (edits are remembered).</summary>
    public static Dictionary<string, object> ParamsFor(EffectInfo info)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(info.Name, out var p)) return p;
            p = EffectEngine.Defaults(info);
            var saved = Storage.Get<Dictionary<string, Dictionary<string, JsonElement>>>("effectParams", new());
            if (saved.TryGetValue(info.Name, out var s))
                foreach (var def in info.Params)
                    if (s.TryGetValue(def.Key, out var v))
                        try
                        {
                            p[def.Key] = def.Kind switch
                            {
                                ParamKind.Range => v.GetDouble(),
                                ParamKind.Check => v.GetBoolean(),
                                _ => v.GetString() ?? def.Default,
                            };
                        }
                        catch { /* keep default */ }
            return Cache[info.Name] = p;
        }
    }

    public static void Changed()
    {
        _dirty = true;
        Flush.Change(1000, Timeout.Infinite); // write at most once a second while sliders move
    }

    public static double Brightness
    {
        get => Storage.Get("effectBrightness", 1.0);
        set => Storage.Set("effectBrightness", value);
    }

    /// <summary>Set by the Debug-only developer switches so a test run never changes what the app resumes.</summary>
    public static bool ReadOnly { get; set; }

    public static string? LastEffect
    {
        get => Storage.Get<string?>("lastEffect", null);
        set { if (!ReadOnly) Storage.Set("lastEffect", value); }
    }

    public static bool ResumeOnStart
    {
        get => Storage.Get("resumeEffect", true);
        set => Storage.Set("resumeEffect", value);
    }

    public static void Save()
    {
        if (!_dirty || ReadOnly) return;
        _dirty = false;
        lock (Cache) Storage.Set("effectParams", Cache);
    }
}
