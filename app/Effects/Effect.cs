using OpenEVision.Device;

namespace OpenEVision.Effects;

public enum ParamKind { Range, Color, Select, Check }

public sealed record ParamDef(string Key, string Label, ParamKind Kind, object Default,
    double Min = 0, double Max = 1, double Step = 0.05, (string Value, string Text)[]? Options = null);

/// <summary>LED geometry in custom order: position of each LED in key units, from the on-screen layout.</summary>
public sealed class Ctx
{
    public static readonly int N = KeyboardService.LedCount;
    public readonly double[] X = new double[N], Y = new double[N];
    public readonly int[] Gx = new int[N], Gy = new int[N];
    public readonly double XMax;
    public readonly int Gw;

    public Ctx()
    {
        var pos = new (double X, double Y)?[N];
        foreach (var k in KeyLayout.Keys)
            if (KeyLayout.CustomLed.TryGetValue(k.Code, out int led)) pos[led] = (k.X + k.Width / 2, k.Y + 0.5);
        for (int i = 0; i < N; i++)
        {
            var p = pos[i] ?? ((i % KeyLayout.Columns) * 1.1 + 0.5, i / KeyLayout.Columns + 0.5);
            X[i] = p.X; Y[i] = p.Y;
        }
        XMax = X.Max();
        Gw = (int)XMax + 1;
        for (int i = 0; i < N; i++) { Gx[i] = Math.Clamp((int)X[i], 0, Gw - 1); Gy[i] = Math.Clamp((int)Y[i], 0, 5); }
    }

    public int? Led(string code) => KeyLayout.CustomLed.TryGetValue(code, out int i) ? i : null;
}

/// <summary>A software effect: fills rgb (N * 3, 0..1, custom order) once per frame.</summary>
public abstract class Effect : IDisposable
{
    protected readonly Ctx C;
    public Dictionary<string, object> P { get; }

    protected Effect(Ctx ctx, Dictionary<string, object> p) { C = ctx; P = p; }

    public abstract void Render(double t, double dt, float[] rgb);
    public virtual void Dispose() { }

    protected double D(string k) => Convert.ToDouble(P[k]);
    protected bool B(string k) => Convert.ToBoolean(P[k]);
    protected string S(string k) => Convert.ToString(P[k])!;
    protected (float R, float G, float B) Col(string k) => Hex(S(k));

    public static (float R, float G, float B) Hex(string h)
    {
        h = h.TrimStart('#');
        if (h.Length != 6) return (1, 1, 1);
        int v = Convert.ToInt32(h, 16);
        return ((v >> 16 & 255) / 255f, (v >> 8 & 255) / 255f, (v & 255) / 255f);
    }

    public static (float R, float G, float B) Hsv(double h, double s = 1, double v = 1)
    {
        h -= Math.Floor(h);
        float F(int n)
        {
            double k = (n / 6.0 + h) % 1.0 * 6;
            return (float)(v * (1 - s * Math.Clamp(Math.Min(k, 4 - k), 0, 1)));
        }
        return (F(5), F(3), F(1));
    }

    protected static void Set(float[] rgb, int i, (float R, float G, float B) c, double scale = 1)
    {
        rgb[i * 3] = (float)(c.R * scale); rgb[i * 3 + 1] = (float)(c.G * scale); rgb[i * 3 + 2] = (float)(c.B * scale);
    }

    protected static void Max(float[] rgb, int i, (float R, float G, float B) c, double scale)
    {
        rgb[i * 3] = Math.Max(rgb[i * 3], (float)(c.R * scale));
        rgb[i * 3 + 1] = Math.Max(rgb[i * 3 + 1], (float)(c.G * scale));
        rgb[i * 3 + 2] = Math.Max(rgb[i * 3 + 2], (float)(c.B * scale));
    }
}

public sealed record EffectInfo(string Name, string Label, string Description, ParamDef[] Params, Func<Ctx, Dictionary<string, object>, Effect> Create);
