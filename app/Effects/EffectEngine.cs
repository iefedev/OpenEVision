using System.Diagnostics;
using OpenEVision.Device;

namespace OpenEVision.Effects;

/// <summary>
/// Runs one software effect at ~25 FPS on its own thread and streams it to the keyboard's RAM (nothing goes to flash).
/// </summary>
public sealed class EffectEngine
{
    public static readonly EffectEngine Instance = new();
    const int Fps = 25;

    public static readonly EffectInfo[] All =
    [
        new("typing", "Typing reaction", "A ripple spreads from each key you press, or the key fades out", TypingEffect.Params, (c, p) => new TypingEffect(c, p)),
        new("audio", "Music visualizer", "What your PC plays, as frequency bars", AudioEffect.Params, (c, p) => new AudioEffect(c, p)),
        new("system", "System gauges", "CPU load on the F keys, GPU load on the number row, GPU temperature as background (GPU: NVIDIA only)", SystemEffect.Params, (c, p) => new SystemEffect(c, p)),
        new("rainbow", "Rainbow wave", "Smoothly scrolling rainbow", RainbowEffect.Params, (c, p) => new RainbowEffect(c, p)),
        new("plasma", "Plasma", "Flowing color waves", PlasmaEffect.Params, (c, p) => new PlasmaEffect(c, p)),
        new("fire", "Fire", "Flames rising from the bottom", FireEffect.Params, (c, p) => new FireEffect(c, p)),
        new("matrix", "Matrix rain", "Drops falling from the top", MatrixEffect.Params, (c, p) => new MatrixEffect(c, p)),
    ];

    readonly object _gate = new();
    Thread? _thread;
    CancellationTokenSource? _cts;
    Effect? _effect;

    public EffectInfo? Running { get; private set; }
    public double Brightness { get; set; } = 1;
    public double FrameRate { get; private set; }
    public string? Error { get; private set; }
    public event Action? Changed;

    public static Dictionary<string, object> Defaults(EffectInfo info) => info.Params.ToDictionary(p => p.Key, p => p.Default);

    /// <summary>Starts an effect (stopping the current one). Throws if the effect can't start (e.g. no audio device).</summary>
    public async Task StartAsync(EffectInfo info, Dictionary<string, object> parameters, double brightness)
    {
        await StopAsync(notify: false);
        var p = Defaults(info);
        foreach (var (k, v) in parameters) if (p.ContainsKey(k)) p[k] = v;
        var effect = info.Create(new Ctx(), p);
        lock (_gate)
        {
            _effect = effect;
            Running = info;
            Brightness = brightness;
            Error = null;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            _thread = new Thread(() => Loop(effect, ct)) { IsBackground = true, Name = "effect" };
            _thread.Start();
        }
        EffectSettings.LastEffect = info.Name;
        Changed?.Invoke();
    }

    public const string CustomPrefix = "custom:";

    /// <summary>A user-made layered effect as an EffectInfo the engine can run (name "custom:&lt;id&gt;").</summary>
    public static EffectInfo InfoFor(Custom.CustomEffectDef def)
    {
        var snapshot = def.Clone(); // later edits in the editor don't touch the running copy
        return new EffectInfo(CustomPrefix + def.Id, def.Name, "Custom effect", [], (c, _) => new Custom.CustomEffect(c, snapshot));
    }

    public Task StartCustomAsync(Custom.CustomEffectDef def, double brightness) =>
        StartAsync(InfoFor(def), new Dictionary<string, object>(), brightness);

    /// <summary>Starts the effect that was running when the app last closed, if any.</summary>
    public async Task ResumeAsync()
    {
        if (!EffectSettings.ResumeOnStart || EffectSettings.LastEffect is not { } last) return;
        try
        {
            if (last.StartsWith(CustomPrefix))
            {
                if (Custom.CustomEffectStore.Find(last[CustomPrefix.Length..]) is { } def)
                    await StartCustomAsync(def, EffectSettings.Brightness);
            }
            else if (All.FirstOrDefault(e => e.Name == last) is { } info)
                await StartAsync(info, EffectSettings.ParamsFor(info), EffectSettings.Brightness);
        }
        catch (Exception e) { Error = e.Message; }
    }

    public void Update(string key, object value)
    {
        lock (_gate) if (_effect != null && _effect.P.ContainsKey(key)) _effect.P[key] = value;
    }

    /// <param name="forget">false when the app is closing: the effect should resume on next start.</param>
    public async Task StopAsync(bool notify = true, bool forget = true)
    {
        if (forget && notify && Running != null) EffectSettings.LastEffect = null;
        Thread? t; Effect? e;
        lock (_gate)
        {
            t = _thread; e = _effect;
            _cts?.Cancel();
            _thread = null; _effect = null; Running = null; FrameRate = 0;
        }
        if (t == null) return;
        await Task.Run(() => t.Join(3000));
        e?.Dispose();
        try { await LiveService.Instance.EndAsync(); } catch { }
        if (notify) Changed?.Invoke();
    }

    void Loop(Effect effect, CancellationToken ct)
    {
        var rgb = new float[Ctx.N * 3];
        var bytes = new byte[Ctx.N * 3];
        var clock = Stopwatch.StartNew();
        double last = 0, fpsT = 0;
        int frames = 0;
        while (!ct.IsCancellationRequested)
        {
            double now = clock.Elapsed.TotalSeconds;
            try
            {
                Array.Clear(rgb);
                lock (_gate) effect.Render(now, now - last, rgb);
                double br = Brightness;
                for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)Math.Clamp(rgb[i] * br * 255, 0, 255);
                LiveService.Instance.ShowAsync(bytes).GetAwaiter().GetResult();
                Error = null;
            }
            catch (Exception e)
            {
                Error = e.Message; // keep running across unplugs
                ct.WaitHandle.WaitOne(1000);
            }
            last = now;
            frames++;
            if (now - fpsT >= 1)
            {
                FrameRate = frames / (now - fpsT);
                frames = 0; fpsT = now;
            }
            int sleep = (int)(1000.0 / Fps - (clock.Elapsed.TotalSeconds - now) * 1000);
            if (sleep > 0) ct.WaitHandle.WaitOne(sleep);
        }
    }
}
