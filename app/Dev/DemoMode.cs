using System.IO;
using System.Text.Json.Nodes;
using OpenEVision.Device;

namespace OpenEVision.Dev;

/// <summary>
/// Debug-only demo mode (--demo, implied by --snapshot): a simulated keyboard (<see cref="DemoTransport"/>) and a
/// throwaway data folder under %TEMP% with a factory dump and two example backups. Nothing touches a real keyboard,
/// the real data folder or the registry, and the UI shows a placeholder data path.
/// </summary>
public static class DemoMode
{
    public static bool Enabled { get; private set; }
    public static readonly string DataRoot = Path.Combine(Path.GetTempPath(), "OpenEVision-demo");
    public const string DisplayRoot = @"C:\Users\demo\AppData\Roaming\OpenEVision";

    /// <summary>Must run before anything reads <see cref="Storage"/>.</summary>
    public static void Enable()
    {
        Enabled = true;
        if (Directory.Exists(DataRoot)) Directory.Delete(DataRoot, recursive: true);
        var backups = Directory.CreateDirectory(Path.Combine(DataRoot, "backups")).FullName;

        var profile = DeviceProfiles.Active;
        var (config, custom) = DemoTransport.FactoryMemory(profile);
        File.WriteAllBytes(Path.Combine(DataRoot, "factory-config.bin"), config);
        File.WriteAllBytes(Path.Combine(DataRoot, "factory-custom.bin"), custom);

        void Backup(string stamp, string created, string label)
        {
            var json = new JsonObject
            {
                ["label"] = label, ["created"] = created, ["device"] = profile.UsbId,
                ["config"] = Convert.ToHexString(config).ToLowerInvariant(), ["custom"] = Convert.ToHexString(custom).ToLowerInvariant(),
            };
            File.WriteAllText(Path.Combine(backups, $"backup-{stamp}.json"), json.ToJsonString());
        }
        Backup("20260214-191502", "2026-02-14 19:15:02", "Before remapping CapsLock");
        Backup("20260301-102233", "2026-03-01 10:22:33", "Rainbow per-key colors");
        var factoryTime = new DateTime(2026, 2, 14, 19, 2, 0);
        File.SetLastWriteTime(Path.Combine(DataRoot, "factory-config.bin"), factoryTime);
    }
}
