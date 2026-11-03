using System.Text;
using OpenEVision.Effects;

namespace OpenEVision.Dev;

/// <summary>--effect-test FILE: runs every effect briefly on the keyboard (RAM only) and reports FPS/errors.</summary>
public static class EffectTest
{
    public static string Run()
    {
        var sb = new StringBuilder();
        var engine = EffectEngine.Instance;
        foreach (var info in EffectEngine.All)
        {
            try
            {
                engine.StartAsync(info, EffectEngine.Defaults(info), 0.8).GetAwaiter().GetResult();
                Thread.Sleep(2500);
                sb.AppendLine($"{info.Name,-8} fps={engine.FrameRate:0.0} error={engine.Error ?? "-"}");
            }
            catch (Exception e) { sb.AppendLine($"{info.Name,-8} START FAILED: {e.GetType().Name}: {e.Message}"); }
        }
        foreach (var def in Effects.Custom.CustomEffectStore.All())
        {
            try
            {
                engine.StartCustomAsync(def, 0.8).GetAwaiter().GetResult();
                Thread.Sleep(2500);
                sb.AppendLine($"custom '{def.Name}' layers={def.Layers.Count} fps={engine.FrameRate:0.0} error={engine.Error ?? "-"}");
            }
            catch (Exception e) { sb.AppendLine($"custom '{def.Name}' START FAILED: {e.Message}"); }
        }
        engine.StopAsync().GetAwaiter().GetResult();
        sb.AppendLine($"stopped running={engine.Running?.Name ?? "none"}");
        return sb.ToString();
    }
}
