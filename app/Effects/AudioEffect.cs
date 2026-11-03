// WasapiLoopbackCapture is marked obsolete in NAudio 3 in favour of a builder API; it still does system-wide
// loopback of the default output, which is exactly what this needs.
#pragma warning disable CS0618
using System.Diagnostics;
using NAudio.Dsp;
using NAudio.Wave;

namespace OpenEVision.Effects;

/// <summary>What you hear (WASAPI loopback of the default output) as frequency bars or a bass pulse.</summary>
public sealed class AudioEffect : Effect
{
    public static readonly ParamDef[] Params =
    [
        new("gain", "Gain", ParamKind.Range, 1.0, 0.3, 3, 0.1),
        new("style", "Style", ParamKind.Select, "bars", Options: [("bars", "Bars"), ("pulse", "Pulse (whole keyboard)")]),
        new("hue", "Hue", ParamKind.Range, 0.33, 0, 1, 0.05),
    ];

    readonly LoopbackAnalyzer _audio;

    public AudioEffect(Ctx c, Dictionary<string, object> p) : base(c, p) => _audio = new LoopbackAnalyzer(c.Gw);

    public override void Render(double t, double dt, float[] rgb)
    {
        var levels = _audio.Update(D("gain"));
        if (S("style") == "pulse")
        {
            double lvl = _audio.Bass;
            for (int i = 0; i < Ctx.N; i++) Set(rgb, i, Hsv(D("hue") + C.X[i] / C.XMax * 0.3), 0.05 + 0.95 * lvl);
            return;
        }
        for (int i = 0; i < Ctx.N; i++)
        {
            double height = levels[C.Gx[i]] * 6, fromBottom = 6 - C.Gy[i] - 0.5;
            double on = Math.Clamp(height - fromBottom + 0.5, 0, 1);
            Set(rgb, i, Hsv(D("hue") - fromBottom / 6 * 0.33), on);
        }
    }

    public override void Dispose() => _audio.Dispose();
}

/// <summary>
/// WASAPI loopback of the default output + FFT into log-spaced bands with auto-gain and smoothing.
/// Call Update() once per frame; Levels/Bass/Overall are 0..1.
/// </summary>
public sealed class LoopbackAnalyzer : IDisposable
{
    const int FftSize = 2048, FftLog2 = 11;
    readonly WasapiLoopbackCapture _cap;
    readonly float[] _ring = new float[FftSize];
    readonly object _ringLock = new();
    readonly int _sampleRate;
    long _lastData;
    double _peak = 1e-3;

    public double[] Levels { get; }
    public double Bass { get; private set; }
    public double Overall { get; private set; }

    public LoopbackAnalyzer(int bands)
    {
        Levels = new double[bands];
        try { _cap = new WasapiLoopbackCapture(); }
        catch (Exception e) { throw new InvalidOperationException("Could not open the default audio output: " + e.Message, e); }
        _sampleRate = _cap.WaveFormat.SampleRate;
        _cap.DataAvailable += OnData;
        _cap.StartRecording();
    }

    void OnData(object? s, WaveInEventArgs e)
    {
        var fmt = _cap.WaveFormat;
        if (fmt.Encoding != WaveFormatEncoding.IeeeFloat && fmt.BitsPerSample != 32) return; // loopback is float32 in practice
        int ch = fmt.Channels, frames = e.BytesRecorded / 4 / ch;
        if (frames == 0) return;
        var samples = new float[frames];
        for (int f = 0; f < frames; f++)
        {
            float sum = 0;
            for (int k = 0; k < ch; k++) sum += BitConverter.ToSingle(e.Buffer, (f * ch + k) * 4);
            samples[f] = sum / ch;
        }
        lock (_ringLock)
        {
            if (frames >= FftSize) Array.Copy(samples, frames - FftSize, _ring, 0, FftSize);
            else
            {
                Array.Copy(_ring, frames, _ring, 0, FftSize - frames);
                Array.Copy(samples, 0, _ring, FftSize - frames, frames);
            }
        }
        _lastData = Stopwatch.GetTimestamp();
    }

    public double[] Update(double gain = 1)
    {
        int n = Levels.Length;
        var target = new double[n];
        // WASAPI loopback delivers nothing while the system is silent.
        if (Stopwatch.GetElapsedTime(_lastData).TotalSeconds <= 0.25)
        {
            var buf = new Complex[FftSize];
            lock (_ringLock)
                for (int i = 0; i < FftSize; i++)
                    buf[i].X = (float)(_ring[i] * FastFourierTransform.HannWindow(i, FftSize));
            FastFourierTransform.FFT(true, FftLog2, buf);
            double binHz = (double)_sampleRate / FftSize;
            var bands = new double[n];
            for (int b = 0; b < n; b++)
            {
                double lo = 40 * Math.Pow(16000.0 / 40, (double)b / n), hi = 40 * Math.Pow(16000.0 / 40, (double)(b + 1) / n);
                int a = (int)(lo / binHz), z = Math.Max(a + 1, (int)(hi / binHz));
                double sum = 0;
                for (int k = a; k < z && k < FftSize / 2; k++) sum += Math.Sqrt(buf[k].X * buf[k].X + buf[k].Y * buf[k].Y);
                bands[b] = sum / (z - a) * (1 + 3.0 * b / Math.Max(1, n - 1)); // tilt: highs are naturally quieter
            }
            _peak = Math.Max(_peak * 0.995, Math.Max(bands.Max(), 1e-3)); // slow auto-gain
            for (int b = 0; b < n; b++) target[b] = Math.Pow(Math.Clamp(bands[b] / _peak * gain, 0, 1), 0.7);
        }
        for (int b = 0; b < n; b++) Levels[b] = target[b] > Levels[b] ? target[b] : Levels[b] * 0.85;
        Bass = Levels.Take(Math.Max(3, n / 4)).Average();
        Overall = Levels.Average();
        return Levels;
    }

    public void Dispose()
    {
        _cap.StopRecording();
        _cap.Dispose();
    }
}
