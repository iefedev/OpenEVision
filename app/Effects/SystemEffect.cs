using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OpenEVision.Effects;

/// <summary>
/// CPU load on the F row, GPU load on the number row, GPU temperature as the background color.
/// GPU values come from nvidia-smi, so they only work on NVIDIA GPUs; elsewhere the GPU bar stays empty.
/// </summary>
public sealed class SystemEffect(Ctx c, Dictionary<string, object> p) : Effect(c, p)
{
    public static readonly ParamDef[] Params = [new("dim", "Background brightness", ParamKind.Range, 0.25, 0, 1, 0.05)];
    static readonly string[] CpuKeys = ["F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12"];
    static readonly string[] GpuKeys = ["Digit1", "Digit2", "Digit3", "Digit4", "Digit5", "Digit6", "Digit7", "Digit8", "Digit9", "Digit0", "Minus", "Equal"];

    readonly SystemMonitor _mon = new();

    public override void Render(double t, double dt, float[] rgb)
    {
        double k = Math.Clamp(((_mon.GpuTemp ?? 35) - 35) / 50, 0, 1); // 35 C blue -> 85 C red
        var bg = Hsv(0.66 * (1 - k));
        for (int i = 0; i < Ctx.N; i++) Set(rgb, i, bg, D("dim"));
        Bar(rgb, CpuKeys, _mon.Cpu);
        Bar(rgb, GpuKeys, (_mon.GpuUtil ?? 0) / 100);
    }

    void Bar(float[] rgb, string[] keys, double frac)
    {
        var leds = keys.Select(C.Led).OfType<int>().ToArray();
        for (int j = 0; j < leds.Length; j++)
        {
            double fill = Math.Clamp(frac * leds.Length - j, 0, 1);
            Set(rgb, leds[j], Hsv(0.33 * (1 - (double)j / Math.Max(1, leds.Length - 1))), Math.Max(fill, 0.04));
        }
    }

    public override void Dispose() => _mon.Dispose();
}

/// <summary>CPU load (GetSystemTimes) every 0.5 s, NVIDIA GPU temperature/load (nvidia-smi) every 2 s.</summary>
public sealed class SystemMonitor : IDisposable
{
    public double Cpu { get; private set; }
    public double? GpuTemp { get; private set; }
    public double? GpuUtil { get; private set; }

    readonly CancellationTokenSource _cts = new();

    [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    public SystemMonitor() => Task.Run(() => Loop(_cts.Token));

    async Task Loop(CancellationToken ct)
    {
        GetSystemTimes(out long pi, out long pk, out long pu);
        for (int n = 0; !ct.IsCancellationRequested; n++)
        {
            try { await Task.Delay(500, ct); } catch (OperationCanceledException) { return; }
            GetSystemTimes(out long i, out long k, out long u);
            long total = (k - pk) + (u - pu);
            if (total > 0) Cpu = Cpu * 0.5 + (1 - (double)(i - pi) / total) * 0.5;
            (pi, pk, pu) = (i, k, u);
            if (n % 4 == 0) await ReadGpu();
        }
    }

    async Task ReadGpu()
    {
        try
        {
            var psi = new ProcessStartInfo("nvidia-smi", "--query-gpu=temperature.gpu,utilization.gpu --format=csv,noheader,nounits")
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi)!;
            var line = (await p.StandardOutput.ReadLineAsync()) ?? "";
            var parts = line.Split(',');
            GpuTemp = double.Parse(parts[0].Trim());
            GpuUtil = double.Parse(parts[1].Trim());
        }
        catch { GpuTemp = GpuUtil = null; }
    }

    public void Dispose() => _cts.Cancel();
}
