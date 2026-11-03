using System.IO;
using System.Text.Json.Nodes;

namespace OpenEVision.Device;

public sealed record BackupEntry(string Path, string Name, string Label, string Created, string Source, bool Deletable);

/// <summary>
/// Backups as JSON files ({label, created, device, config: hex, custom: hex}) in the data folder's backups\,
/// plus the factory dump taken from the keyboard on first contact.
/// </summary>
public static class Backups
{
    public const string FactoryPath = "factory";

    public static async Task<BackupEntry> CreateAsync(KeyboardService kb, string label)
    {
        var (cfg, cust) = await kb.RawDumpAsync();
        var p = KeyboardService.Profile;
        if (cfg.Length != p.ConfigSize || cust.Length != p.CustomSize)
            throw new IOException($"Incomplete read ({cfg.Length}/{cust.Length} bytes).");
        var name = $"backup-{DateTime.Now:yyyyMMdd-HHmmss}";
        var path = Path.Combine(Storage.BackupDir, name + ".json");
        var created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var json = new JsonObject
        {
            ["label"] = label, ["created"] = created, ["device"] = p.UsbId,
            ["config"] = Convert.ToHexString(cfg).ToLowerInvariant(), ["custom"] = Convert.ToHexString(cust).ToLowerInvariant(),
        };
        await File.WriteAllTextAsync(path, json.ToJsonString());
        return new BackupEntry(path, name, label, created, "Backup", true);
    }

    public static List<BackupEntry> List()
    {
        var list = new List<BackupEntry>();
        if (Directory.Exists(Storage.BackupDir))
            foreach (var f in Directory.GetFiles(Storage.BackupDir, "backup-*.json").OrderDescending())
            {
                try
                {
                    var j = JsonNode.Parse(File.ReadAllText(f))!;
                    list.Add(new BackupEntry(f, Path.GetFileNameWithoutExtension(f), (string?)j["label"] ?? "", (string?)j["created"] ?? "", "Backup", true));
                }
                catch { /* unreadable file: skip */ }
            }
        if (Storage.HasFactory)
            list.Add(new BackupEntry(FactoryPath, "factory", "Factory state (first read of this keyboard, before any write)",
                File.GetLastWriteTime(Storage.FactoryConfigFile).ToString("yyyy-MM-dd HH:mm"), "Factory", false));
        return list;
    }

    public static (byte[] Config, byte[] Custom) Load(BackupEntry b)
    {
        var p = KeyboardService.Profile;
        byte[] cfg, cust;
        if (b.Path == FactoryPath)
        {
            cfg = File.ReadAllBytes(Storage.FactoryConfigFile);
            cust = File.ReadAllBytes(Storage.FactoryCustomFile);
        }
        else
        {
            var j = JsonNode.Parse(File.ReadAllText(b.Path))!;
            if ((string?)j["device"] is { } device && device != p.UsbId)
                throw new InvalidDataException($"This backup was taken from a different keyboard ({device}); it is not restored.");
            cfg = Convert.FromHexString((string)j["config"]!);
            cust = Convert.FromHexString((string)j["custom"]!);
        }
        if (cfg.Length < p.KeyTableOffset + KeyboardService.KeyTableSize || cust.Length < p.LedCount * 3)
            throw new InvalidDataException("The backup file is incomplete.");
        return (cfg, cust);
    }
}
