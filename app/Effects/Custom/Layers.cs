using System.Diagnostics;
using System.Text.Json;

namespace OpenEVision.Effects.Custom;

/// <summary>Shared services for the layers of one running custom effect (created lazily, disposed together).</summary>
public sealed class LayerServices : IDisposable
{
    public Ctx Ctx { get; }
    LoopbackAnalyzer? _audio;
    SystemMonitor? _system;
    IDisposable? _keys;

    public LayerServices(Ctx ctx) => Ctx = ctx;

    public LoopbackAnalyzer Audio => _audio ??= new LoopbackAnalyzer(Ctx.Gw);
    public SystemMonitor System => _system ??= new SystemMonitor();
    public void UseKeys() => _keys ??= KeyEvents.Acquire();
    public bool HasAudio => _audio != null;

    public void Dispose()
    {
        _audio?.Dispose();
        _system?.Dispose();
        _keys?.Dispose();
    }
}

/// <summary>A running layer. Fills color (N * 3) and alpha (N, 0..1) once per frame, in custom LED order.</summary>
public abstract class Layer : IDisposable
{
    public abstract void Render(double t, double dt, float[] rgb, float[] alpha);
    public virtual void Dispose() { }
}

public sealed record LayerKind(string Name, string Label, string Group, string Description, ParamDef[] Params,
    Func<LayerServices, Dictionary<string, object>, Layer> Create);

public static class LayerKinds
{
    public const string BaseGroup = "Base", ReactiveGroup = "Reactive";

    static readonly ParamDef Color = new("color", "Color", ParamKind.Color, "ff0040");
    static readonly ParamDef Random = new("random", "Random color on every press", ParamKind.Check, false);
    static readonly ParamDef Duration = new("duration", "Duration (s)", ParamKind.Range, 1.0, 0.1, 5, 0.1);

    public static readonly LayerKind[] All =
    [
        new("solid", "Solid color", BaseGroup, "A single color", [new("color", "Color", ParamKind.Color, "0040ff")],
            (s, p) => new SolidLayer(p)),
        new("gradient", "Gradient", BaseGroup, "A scrolling blend between two colors",
            [new("color1", "Color 1", ParamKind.Color, "ff0080"), new("color2", "Color 2", ParamKind.Color, "00c0ff"),
             new("angle", "Angle", ParamKind.Range, 0.0, 0, 360, 15), new("speed", "Scroll speed", ParamKind.Range, 0.0, 0, 2, 0.05)],
            (s, p) => new GradientLayer(s.Ctx, p)),
        new("breathing", "Breathing", BaseGroup, "A color that slowly fades in and out",
            [new("color", "Color", ParamKind.Color, "00ffa0"), new("speed", "Speed", ParamKind.Range, 0.5, 0.1, 3, 0.05)],
            (s, p) => new BreathingLayer(p)),
        new("stars", "Stars", BaseGroup, "Random keys twinkle on and off",
            [new("color", "Color", ParamKind.Color, "ffffff"), new("random", "Random colors", ParamKind.Check, false),
             new("density", "Density", ParamKind.Range, 0.15, 0.02, 1, 0.02), new("duration", "Duration (s)", ParamKind.Range, 1.2, 0.2, 4, 0.1)],
            (s, p) => new StarsLayer(p)),
        Wrap("rainbow", "Rainbow", "Scrolling rainbow", RainbowEffect.Params, (c, p) => new RainbowEffect(c, p)),
        Wrap("plasma", "Plasma", "Flowing color waves", PlasmaEffect.Params, (c, p) => new PlasmaEffect(c, p)),
        Wrap("fire", "Fire", "Flames rising from the bottom", FireEffect.Params, (c, p) => new FireEffect(c, p)),
        Wrap("matrix", "Matrix", "Falling drops", MatrixEffect.Params, (c, p) => new MatrixEffect(c, p)),
        new("audio", "Music bars", BaseGroup, "Audio frequencies as bars",
            [new("gain", "Gain", ParamKind.Range, 1.0, 0.3, 3, 0.1), new("hue", "Hue", ParamKind.Range, 0.33, 0, 1, 0.05)],
            (s, p) => new AudioBarsLayer(s, p)),

        new("keypress", "Key press", ReactiveGroup, "The pressed key lights up (or goes dark with 'Erase') and returns to normal after the duration",
            [Color, Random, Duration, new("spread", "Spread to neighbours", ParamKind.Range, 0.0, 0, 3, 0.25)],
            (s, p) => new KeyPressLayer(s, p)),
        new("ripple", "Ripple", ReactiveGroup, "A ring spreads out from the pressed key",
            [new("color", "Color", ParamKind.Color, "00c0ff"), new("random", "Random color on every press", ParamKind.Check, true),
             new("speed", "Speed (keys/s)", ParamKind.Range, 12.0, 2, 30, 1), new("width", "Width", ParamKind.Range, 1.0, 0.3, 4, 0.1), Duration],
            (s, p) => new RippleLayer(s, p)),
        new("hold", "Hold", ReactiveGroup, "Lit while the key is held, fades out on release",
            [new("color", "Color", ParamKind.Color, "ffffff"), new("release", "Fade after release (s)", ParamKind.Range, 0.3, 0, 3, 0.05)],
            (s, p) => new HoldLayer(s, p)),
    ];

    public static readonly IReadOnlyDictionary<string, LayerKind> ByName = All.ToDictionary(k => k.Name);

    static LayerKind Wrap(string name, string label, string desc, ParamDef[] ps, Func<Ctx, Dictionary<string, object>, Effect> make) =>
        new(name, label, BaseGroup, desc, ps, (s, p) => new EffectLayer(make(s.Ctx, p)));

    /// <summary>Stored params (JSON) merged over the kind's defaults, typed for the layer code.</summary>
    public static Dictionary<string, object> Typed(LayerKind kind, Dictionary<string, JsonElement> stored)
    {
        var p = kind.Params.ToDictionary(d => d.Key, d => d.Default);
        foreach (var d in kind.Params)
        {
            if (!stored.TryGetValue(d.Key, out var v)) continue;
            try
            {
                p[d.Key] = d.Kind switch
                {
                    ParamKind.Range => v.GetDouble(),
                    ParamKind.Check => v.GetBoolean(),
                    _ => v.GetString() ?? d.Default,
                };
            }
            catch { /* keep default */ }
        }
        return p;
    }

    public static JsonElement ToJson(object v) => JsonSerializer.SerializeToElement(v);
}

// ---- base layers --------------------------------------------------------------------------------

/// <summary>Any full-keyboard effect used as an opaque layer.</summary>
sealed class EffectLayer(Effect effect) : Layer
{
    public override void Render(double t, double dt, float[] rgb, float[] alpha)
    {
        effect.Render(t, dt, rgb);
        Array.Fill(alpha, 1f);
    }

    public override void Dispose() => effect.Dispose();
}

sealed class SolidLayer(Dictionary<string, object> p) : Layer
{
    public override void Render(double t, double dt, float[] rgb, float[] alpha)
    {
        var c = Effect.Hex((string)p["color"]);
        for (int i = 0; i < Ctx.N; i++) { rgb[i * 3] = c.R; rgb[i * 3 + 1] = c.G; rgb[i * 3 + 2] = c.B; }
        Array.Fill(alpha, 1f);
    }
}

sealed class GradientLayer(Ctx c, Dictionary<string, object> p) : Layer
{
    public override void Render(double t, double dt, float[] rgb, float[] alpha)
    {
        var a = Effect.Hex((string)p["color1"]);
        var b = Effect.Hex((string)p["color2"]);
        double ang = Convert.ToDouble(p["angle"]) * Math.PI / 180, ca = Math.Cos(ang), sa = Math.Sin(ang);
        double speed = Convert.ToDouble(p["speed"]);
        double span = Math.Abs(c.XMax * ca) + Math.Abs(6 * sa) + 1e-6;
        for (int i = 0; i < Ctx.N; i++)
        {
            double u = (c.X[i] * ca + c.Y[i] * sa) / span - t * speed;
            double k = 0.5 - 0.5 * Math.Cos((u - Math.Floor(u)) * 2 * Math.PI); // smooth there-and-back
            rgb[i * 3] = (float)(a.R + (b.R - a.R) * k);
            rgb[i * 3 + 1] = (float)(a.G + (b.G - a.G) * k);
            rgb[i * 3 + 2] = (float)(a.B + (b.B - a.B) * k);
        }
        Array.Fill(alpha, 1f);
    }
}

sealed class BreathingLayer(Dictionary<string, object> p) : Layer
{
    public override void Render(double t, double dt, float[] rgb, float[] alpha)
    {
        var c = Effect.Hex((string)p["color"]);
        float v = (float)(0.5 - 0.5 * Math.Cos(t * Convert.ToDouble(p["speed"]) * 2 * Math.PI));
        for (int i = 0; i < Ctx.N; i++) { rgb[i * 3] = c.R; rgb[i * 3 + 1] = c.G; rgb[i * 3 + 2] = c.B; alpha[i] = v; }
    }
}

sealed class StarsLayer(Dictionary<string, object> p) : Layer
{
    readonly double[] _born = Enumerable.Repeat(-100.0, Ctx.N).ToArray();
    readonly (float R, float G, float B)[] _col = new (float, float, float)[Ctx.N];
    readonly Random _rnd = new();

    public override void Render(double t, double dt, float[] rgb, float[] alpha)
    {
        double dur = Convert.ToDouble(p["duration"]);
        double rate = Convert.ToDouble(p["density"]) * Ctx.N / dur; // stars per second so that ~density of keys are lit
        for (int n = 0; n < Poisson(rate * dt); n++)
        {
            int i = _rnd.Next(Ctx.N);
            _born[i] = t;
            _col[i] = (bool)p["random"] ? Effect.Hsv(_rnd.NextDouble()) : Effect.Hex((string)p["color"]);
        }
        for (int i = 0; i < Ctx.N; i++)
        {
            double age = (t - _born[i]) / dur;
            float a = age is >= 0 and < 1 ? (float)Math.Sin(age * Math.PI) : 0;
            rgb[i * 3] = _col[i].R; rgb[i * 3 + 1] = _col[i].G; rgb[i * 3 + 2] = _col[i].B; alpha[i] = a;
        }
    }

    int Poisson(double mean)
    {
        int k = 0;
        for (double l = Math.Exp(-mean), prod = _rnd.NextDouble(); prod > l; prod *= _rnd.NextDouble()) k++;
        return k;
    }
}

sealed class AudioBarsLayer(LayerServices s, Dictionary<string, object> p) : Layer
{
    readonly LoopbackAnalyzer _audio = s.Audio;

    public override void Render(double t, double dt, float[] rgb, float[] alpha)
    {
        var lv = _audio.Levels; // updated once per frame by the custom effect
        double gain = Convert.ToDouble(p["gain"]);
        var c = s.Ctx;
        for (int i = 0; i < Ctx.N; i++)
        {
            double fromBottom = 6 - c.Gy[i] - 0.5;
            var col = Effect.Hsv(Convert.ToDouble(p["hue"]) - fromBottom / 6 * 0.33);
            rgb[i * 3] = col.R; rgb[i * 3 + 1] = col.G; rgb[i * 3 + 2] = col.B;
            alpha[i] = (float)Math.Clamp(Math.Min(1, lv[c.Gx[i]] * gain) * 6 - fromBottom + 0.5, 0, 1);
        }
    }
}

// ---- reactive layers ------------------------------------------------------------------------------

abstract class KeyLayer : Layer
{
    protected readonly LayerServices S;
    protected readonly Dictionary<string, object> P;
    protected readonly Random Rnd = new();

    protected KeyLayer(LayerServices s, Dictionary<string, object> p)
    {
        S = s; P = p;
        s.UseKeys();
        KeyEvents.Pressed += OnPressed;
        KeyEvents.Released += OnReleased;
    }

    protected virtual void OnPressed(string code) { }
    protected virtual void OnReleased(string code) { }
    protected static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    protected (float R, float G, float B) PickColor() =>
        P.TryGetValue("random", out var r) && (bool)r ? Effect.Hsv(Rnd.NextDouble()) : Effect.Hex((string)P["color"]);

    public override void Dispose()
    {
        KeyEvents.Pressed -= OnPressed;
        KeyEvents.Released -= OnReleased;
    }
}

sealed class KeyPressLayer(LayerServices s, Dictionary<string, object> p) : KeyLayer(s, p)
{
    readonly List<(int Led, double T0, (float R, float G, float B) C)> _hits = [];

    protected override void OnPressed(string code)
    {
        if (S.Ctx.Led(code) is not int led) return;
        lock (_hits) { _hits.Add((led, Now, PickColor())); if (_hits.Count > 64) _hits.RemoveAt(0); }
    }

    public override void Render(double t, double dt, float[] rgb, float[] alpha)
    {
        Array.Clear(alpha);
        double dur = Convert.ToDouble(P["duration"]), spread = Convert.ToDouble(P["spread"]), now = Now;
        (int Led, double T0, (float R, float G, float B) C)[] hits;
        lock (_hits) hits = _hits.ToArray();
        var c = S.Ctx;
        foreach (var (led, t0, col) in hits)
        {
            double k = 1 - (now - t0) / dur;
            if (k <= 0) continue;
            for (int i = 0; i < Ctx.N; i++)
            {
                double a = k;
                if (i != led)
                {
                    if (spread <= 0) continue;
                    double dx = c.X[i] - c.X[led], dy = c.Y[i] - c.Y[led];
                    double d = Math.Sqrt(dx * dx + dy * dy);
                    if (d > spread) continue;
                    a *= 1 - d / (spread + 0.5);
                }
                if (a > alpha[i]) { alpha[i] = (float)a; rgb[i * 3] = col.R; rgb[i * 3 + 1] = col.G; rgb[i * 3 + 2] = col.B; }
            }
        }
    }
}

sealed class RippleLayer(LayerServices s, Dictionary<string, object> p) : KeyLayer(s, p)
{
    readonly List<(int Led, double T0, (float R, float G, float B) C)> _waves = [];

    protected override void OnPressed(string code)
    {
        if (S.Ctx.Led(code) is not int led) return;
        lock (_waves) { _waves.Add((led, Now, PickColor())); if (_waves.Count > 32) _waves.RemoveAt(0); }
    }

    public override void Render(double t, double dt, float[] rgb, float[] alpha)
    {
        Array.Clear(alpha);
        double dur = Convert.ToDouble(P["duration"]), speed = Convert.ToDouble(P["speed"]), width = Convert.ToDouble(P["width"]), now = Now;
        (int Led, double T0, (float R, float G, float B) C)[] waves;
        lock (_waves) waves = _waves.ToArray();
        var c = S.Ctx;
        foreach (var (led, t0, col) in waves)
        {
            double age = now - t0;
            if (age >= dur) continue;
            double fade = 1 - age / dur, r = age * speed;
            for (int i = 0; i < Ctx.N; i++)
            {
                double dx = c.X[i] - c.X[led], dy = (c.Y[i] - c.Y[led]) * 1.2;
                double d = Math.Sqrt(dx * dx + dy * dy) - r;
                double a = Math.Exp(-d * d / (width * width)) * fade;
                if (i == led) a = Math.Max(a, fade);
                if (a > alpha[i]) { alpha[i] = (float)a; rgb[i * 3] = col.R; rgb[i * 3 + 1] = col.G; rgb[i * 3 + 2] = col.B; }
            }
        }
    }
}

sealed class HoldLayer(LayerServices s, Dictionary<string, object> p) : KeyLayer(s, p)
{
    readonly Dictionary<int, double> _released = new(); // led -> release time

    protected override void OnPressed(string code)
    {
        if (S.Ctx.Led(code) is int led) lock (_released) _released.Remove(led);
    }

    protected override void OnReleased(string code)
    {
        if (S.Ctx.Led(code) is int led) lock (_released) _released[led] = Now;
    }

    public override void Render(double t, double dt, float[] rgb, float[] alpha)
    {
        var col = Effect.Hex((string)P["color"]);
        double rel = Convert.ToDouble(P["release"]), now = Now;
        Array.Clear(alpha);
        foreach (var k in Device.KeyLayout.CustomLed)
            if (KeyEvents.IsDown(k.Key)) alpha[k.Value] = 1;
        lock (_released)
            foreach (var (led, t0) in _released.ToArray())
            {
                double a = rel <= 0 ? 0 : 1 - (now - t0) / rel;
                if (a <= 0) { _released.Remove(led); continue; }
                alpha[led] = Math.Max(alpha[led], (float)a);
            }
        for (int i = 0; i < Ctx.N; i++) { rgb[i * 3] = col.R; rgb[i * 3 + 1] = col.G; rgb[i * 3 + 2] = col.B; }
    }
}
