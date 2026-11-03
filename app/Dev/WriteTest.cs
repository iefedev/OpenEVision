using System.IO;
using System.Text;
using OpenEVision.Device;

namespace OpenEVision.Dev;

/// <summary>
/// --write-test FILE: exercises every flash write path on the real keyboard and leaves it as it was.
/// backup -> remap ScrollLock to X -> factory default for that key -> custom colors rewrite -> restore backup -> compare.
/// </summary>
public static class WriteTest
{
    public static string Run()
    {
        var sb = new StringBuilder();
        var kb = KeyboardService.Instance;
        var p = KeyboardService.Profile;
        BackupEntry? backup = null;
        try
        {
            kb.EnsureFactoryAsync().GetAwaiter().GetResult();
            sb.AppendLine($"factory dump={Storage.HasFactory}");
            sb.AppendLine($"polling={UsbInfo.PollingHz()?.ToString() ?? "?"} Hz");
            var (cfg0, cust0) = kb.RawDumpAsync().GetAwaiter().GetResult();

            backup = Backups.CreateAsync(kb, "write-test").GetAwaiter().GetResult();
            sb.AppendLine($"backup={backup.Name} listed={Backups.List().Any(b => b.Path == backup.Path)}");

            var factory = Storage.FactoryConfig()!.AsSpan(p.KeyTableOffset, KeyboardService.KeyTableSize).ToArray();
            int idx = LiveMap.Decode(factory)["ScrollLock"];
            kb.SetKeysAsync(new Dictionary<int, byte[]> { [idx] = KeyActions.ToBytes("k:1b") }).GetAwaiter().GetResult();
            var t1 = kb.GetKeyTableAsync().GetAwaiter().GetResult();
            sb.AppendLine($"remap ScrollLock->X: {(KeyActions.FromBytes(t1.AsSpan(idx * 3, 3)) == "k:1b" ? "ok" : "FAILED")}");
            kb.SetKeysAsync(new Dictionary<int, byte[]> { [idx] = factory.AsSpan(idx * 3, 3).ToArray() }).GetAwaiter().GetResult();
            var t2 = kb.GetKeyTableAsync().GetAwaiter().GetResult();
            sb.AppendLine($"default ScrollLock: {(t2.AsSpan(idx * 3, 3).SequenceEqual(factory.AsSpan(idx * 3, 3)) ? "ok" : "FAILED")}");

            var custom = kb.GetCustomAsync().GetAwaiter().GetResult();
            kb.SetCustomAsync(custom, activate: false).GetAwaiter().GetResult();
            sb.AppendLine($"custom rewrite: {(kb.GetCustomAsync().GetAwaiter().GetResult().AsSpan().SequenceEqual(custom) ? "ok" : "FAILED")}");

            var (bc, bu) = Backups.Load(backup);
            kb.RestoreAsync(bc, bu).GetAwaiter().GetResult();
            var (cfg1, cust1) = kb.RawDumpAsync().GetAwaiter().GetResult();
            bool header = cfg1.AsSpan(0, p.HeaderSize).SequenceEqual(cfg0.AsSpan(0, p.HeaderSize));
            bool table = cfg1.AsSpan(p.KeyTableOffset, KeyboardService.KeyTableSize).SequenceEqual(cfg0.AsSpan(p.KeyTableOffset, KeyboardService.KeyTableSize));
            bool colors = cust1.AsSpan(0, p.LedCount * 3).SequenceEqual(cust0.AsSpan(0, p.LedCount * 3));
            sb.AppendLine($"restore -> header={(header ? "same" : "DIFF")} keytable={(table ? "same" : "DIFF")} custom={(colors ? "same" : "DIFF")}");
        }
        catch (Exception e) { sb.AppendLine($"error={e.GetType().Name}: {e.Message}"); }
        finally { if (backup != null) File.Delete(backup.Path); }
        return sb.ToString();
    }
}
