namespace OpenEVision.Effects;

public sealed class RainbowEffect(Ctx c, Dictionary<string, object> p) : Effect(c, p)
{
    public static readonly ParamDef[] Params =
    [
        new("speed", "Speed", ParamKind.Range, 0.4, 0.05, 2, 0.05),
        new("scale", "Width", ParamKind.Range, 0.05, 0.01, 0.2, 0.01),
        new("angle", "Angle", ParamKind.Range, 0.0, 0, 360, 15),
    ];

    public override void Render(double t, double dt, float[] rgb)
    {
        double a = D("angle") * Math.PI / 180, ca = Math.Cos(a), sa = Math.Sin(a);
        for (int i = 0; i < Ctx.N; i++)
            Set(rgb, i, Hsv((C.X[i] * ca + C.Y[i] * sa * 3) * D("scale") - t * D("speed")));
    }
}

public sealed class PlasmaEffect(Ctx c, Dictionary<string, object> p) : Effect(c, p)
{
    public static readonly ParamDef[] Params =
    [
        new("speed", "Speed", ParamKind.Range, 1.0, 0.1, 3, 0.1),
        new("hue", "Hue shift", ParamKind.Range, 0.6, 0, 1, 0.05),
        new("spread", "Color range", ParamKind.Range, 0.35, 0.1, 1, 0.05),
    ];

    public override void Render(double t, double dt, float[] rgb)
    {
        double s = t * D("speed");
        for (int i = 0; i < Ctx.N; i++)
        {
            double x = C.X[i] / 3, y = C.Y[i] / 1.5;
            double v = Math.Sin(x + s) + Math.Sin((y + s) * 0.7) + Math.Sin((x + y + s) * 0.5)
                       + Math.Sin(Math.Sqrt((x - 4) * (x - 4) + (y - 2) * (y - 2)) + s * 1.3);
            Set(rgb, i, Hsv(D("hue") + v / 8 * D("spread") * 4, 1, 0.35 + 0.65 * (v + 4) / 8));
        }
    }
}

public sealed class FireEffect : Effect
{
    public static readonly ParamDef[] Params =
    [
        new("intensity", "Intensity", ParamKind.Range, 0.8, 0.3, 1, 0.05),
        new("cooling", "Cooling", ParamKind.Range, 0.18, 0.05, 0.5, 0.01),
    ];

    readonly double[,] _heat;
    readonly Random _rnd = new();
    double _acc;

    public FireEffect(Ctx c, Dictionary<string, object> p) : base(c, p) => _heat = new double[7, c.Gw];

    public override void Render(double t, double dt, float[] rgb)
    {
        _acc += dt;
        int w = C.Gw;
        while (_acc > 1 / 20.0) // fixed 20 Hz simulation
        {
            _acc -= 1 / 20.0;
            for (int x = 0; x < w; x++) _heat[6, x] = _rnd.NextDouble() * D("intensity") * 1.4;
            for (int y = 0; y < 6; y++)
                for (int x = 0; x < w; x++)
                {
                    double up = (2 * _heat[y + 1, x] + _heat[y + 1, (x + w - 1) % w] + _heat[y + 1, (x + 1) % w]) / 4.05;
                    _heat[y, x] = Math.Clamp(up - _rnd.NextDouble() * D("cooling"), 0, 1);
                }
        }
        for (int i = 0; i < Ctx.N; i++)
        {
            double v = _heat[C.Gy[i] + 1, C.Gx[i]] * 3;
            rgb[i * 3] = (float)Math.Clamp(v, 0, 1);
            rgb[i * 3 + 1] = (float)Math.Clamp(v - 1, 0, 1);
            rgb[i * 3 + 2] = (float)Math.Clamp(v - 2, 0, 1);
        }
    }
}

public sealed class MatrixEffect : Effect
{
    public static readonly ParamDef[] Params =
    [
        new("color", "Color", ParamKind.Color, "00ff40"),
        new("density", "Density", ParamKind.Range, 0.12, 0.02, 0.4, 0.02),
        new("speed", "Speed", ParamKind.Range, 8.0, 2, 20, 1),
    ];

    readonly double[,] _grid;
    readonly List<(int X, int Y)> _drops = [];
    readonly Random _rnd = new();
    double _acc;

    public MatrixEffect(Ctx c, Dictionary<string, object> p) : base(c, p) => _grid = new double[6, c.Gw];

    public override void Render(double t, double dt, float[] rgb)
    {
        _acc += dt;
        double step = 1 / D("speed");
        while (_acc > step)
        {
            _acc -= step;
            for (int y = 0; y < 6; y++) for (int x = 0; x < C.Gw; x++) _grid[y, x] *= 0.55;
            for (int k = _drops.Count - 1; k >= 0; k--)
            {
                var d = _drops[k];
                if (d.Y + 1 < 6) _drops[k] = (d.X, d.Y + 1); else _drops.RemoveAt(k);
            }
            for (int x = 0; x < C.Gw; x++) if (_rnd.NextDouble() < D("density")) _drops.Add((x, 0));
            foreach (var d in _drops) _grid[d.Y, d.X] = 1;
        }
        var col = Col("color");
        for (int i = 0; i < Ctx.N; i++)
        {
            double v = _grid[C.Gy[i], C.Gx[i]];
            double head = v > 0.99 ? 0.6 : 0;
            rgb[i * 3] = (float)Math.Min(1, v * col.R + head);
            rgb[i * 3 + 1] = (float)Math.Min(1, v * col.G + head);
            rgb[i * 3 + 2] = (float)Math.Min(1, v * col.B + head);
        }
    }
}
