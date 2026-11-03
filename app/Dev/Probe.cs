using System.Text;
using OpenEVision.Device;

namespace OpenEVision.Dev;

/// <summary>
/// --probe FILE: read-only device check. Reads product/firmware, the hardware mode, the first 1 KiB of config and the
/// custom colors, and checks that the write guard rejects an out-of-range write before any packet is sent.
/// Nothing is written to the keyboard (the first connection only saves the factory dump to the data folder).
/// </summary>
public static class Probe
{
    public static string Run()
    {
        var sb = new StringBuilder();
        try
        {
            var svc = KeyboardService.Instance;
            var (product, version) = svc.GetInfoAsync().GetAwaiter().GetResult();
            sb.AppendLine($"product={product} version={version:X4}");
            sb.AppendLine($"mode={svc.GetModeAsync().GetAwaiter().GetResult()}");
            var cfg = svc.RunAsync(kb => kb.Read(HidKeyboard.CmdReadConfig, 0, 0x400)).GetAwaiter().GetResult();
            sb.AppendLine($"config[0..0x400]={Convert.ToHexString(cfg)}");
            var custom = svc.GetCustomAsync().GetAwaiter().GetResult();
            sb.AppendLine($"custom={Convert.ToHexString(custom)}");
            var p = KeyboardService.Profile;
            int outside = p.KeyTableOffset + p.KeyTableEntries * 3; // first byte after the key table
            sb.AppendLine(HidKeyboard.WriteAllowed(p, HidKeyboard.CmdWriteConfig, outside, 1)
                ? $"guard=FAILED (a write to 0x{outside:X} would be allowed)" : "guard=ok");
        }
        catch (Exception e) { sb.AppendLine($"error={e.GetType().Name}: {e.Message}"); }
        return sb.ToString();
    }
}
