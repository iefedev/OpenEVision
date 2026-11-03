using OpenEVision.Device;

namespace OpenEVision.Effects.Custom;

/// <summary>
/// Runs a CustomEffectDef: renders each enabled layer and composites bottom-to-top with its blend mode, opacity,
/// key mask and modulation. Plugs into EffectEngine like any other effect.
/// </summary>
public sealed class CustomEffect : Effect
{
    sealed record Running(LayerDef Def, Layer Layer, bool[]? Mask);

    readonly LayerServices _services;
    readonly List<Running> _layers = [];
    readonly float[] _lrgb = new float[Ctx.N * 3], _la = new float[Ctx.N];

    public CustomEffect(Ctx c, CustomEffectDef def) : base(c, new Dictionary<string, object>())
    {
        _services = new LayerServices(c);
        try
        {
            foreach (var l in def.Layers.Where(l => l.Enabled))
            {
                if (!LayerKinds.ByName.TryGetValue(l.Kind, out var kind)) continue;
                var layer = kind.Create(_services, LayerKinds.Typed(kind, l.Params));
                _layers.Add(new Running(l, layer, Mask(l.Keys)));
                if (l.Mod is ModSource.Audio or ModSource.Bass) _ = _services.Audio;
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    static bool[]? Mask(List<string>? keys)
    {
        if (keys == null) return null;
        var m = new bool[Ctx.N];
        foreach (var k in keys) if (KeyLayout.CustomLed.TryGetValue(k, out int i)) m[i] = true;
        return m;
    }

    double Signal(ModSource s) => s switch
    {
        ModSource.Audio => _services.Audio.Overall,
        ModSource.Bass => _services.Audio.Bass,
        ModSource.Cpu => _services.System.Cpu,
        ModSource.GpuLoad => (_services.System.GpuUtil ?? 0) / 100,
        ModSource.GpuTemp => Math.Clamp(((_services.System.GpuTemp ?? 35) - 35) / 50, 0, 1),
        _ => 1,
    };

    public override void Render(double t, double dt, float[] rgb)
    {
        if (_services.HasAudio) _services.Audio.Update();
        Array.Clear(rgb);
        foreach (var (def, layer, mask) in _layers)
        {
            Array.Clear(_lrgb);
            Array.Clear(_la);
            layer.Render(t, dt, _lrgb, _la);
            double op = def.Opacity;
            if (def.Mod != ModSource.None) op *= 1 - def.ModAmount + def.ModAmount * Signal(def.Mod);
            for (int i = 0; i < Ctx.N; i++)
            {
                if (mask != null && !mask[i]) continue;
                float a = (float)(_la[i] * op);
                if (a <= 0) continue;
                for (int ch = 0; ch < 3; ch++)
                {
                    int j = i * 3 + ch;
                    float o = rgb[j], c = _lrgb[j];
                    rgb[j] = def.Blend switch
                    {
                        Blend.Add => Math.Min(1, o + c * a),
                        Blend.Multiply => o * (1 - a + c * a),
                        Blend.Erase => o * (1 - a),
                        _ => o * (1 - a) + c * a,
                    };
                }
            }
        }
    }

    public override void Dispose()
    {
        foreach (var r in _layers) r.Layer.Dispose();
        _services.Dispose();
    }
}
