using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenEVision.Device;

/// <summary>
/// App data in %APPDATA%\OpenEVision: settings.json, backups\, effects\, and the factory dump
/// (factory-config.bin, factory-custom.bin, factory-info.json) read from the user's own keyboard on first contact.
/// </summary>
public static class Storage
{
    public static readonly string Root = ResolveRoot();
    public static readonly string BackupDir = Path.Combine(Root, "backups");
    static readonly string SettingsFile = Path.Combine(Root, "settings.json");
    public static readonly string FactoryConfigFile = Path.Combine(Root, "factory-config.bin");
    public static readonly string FactoryCustomFile = Path.Combine(Root, "factory-custom.bin");
    static readonly string FactoryInfoFile = Path.Combine(Root, "factory-info.json");

    static Storage() => Directory.CreateDirectory(BackupDir);

    static string ResolveRoot()
    {
#if DEBUG
        if (Dev.DemoMode.Enabled) return Dev.DemoMode.DataRoot;
#endif
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenEVision");
    }

    /// <summary>The data folder as shown in the UI (a neutral placeholder in demo mode).</summary>
    public static string DisplayRoot
    {
        get
        {
#if DEBUG
            if (Dev.DemoMode.Enabled) return Dev.DemoMode.DisplayRoot;
#endif
            return Root;
        }
    }

    public static bool HasFactory => File.Exists(FactoryConfigFile) && File.Exists(FactoryCustomFile);

    /// <summary>
    /// Saves the keyboard's current memory as the factory dump if none exists yet. Called on every new connection,
    /// before any other command, so the first read happens before this app has written anything. Never overwritten.
    /// </summary>
    public static void EnsureFactory(HidKeyboard kb)
    {
        if (HasFactory) return;
        var (config, custom) = KeyboardService.RawDump(kb);
        if (config.Length != kb.Profile.ConfigSize || custom.Length != kb.Profile.CustomSize)
            throw new IOException($"Incomplete factory read ({config.Length}/{custom.Length} bytes).");
        WriteNew(FactoryCustomFile, custom);
        WriteNew(FactoryInfoFile, System.Text.Encoding.UTF8.GetBytes(new JsonObject
        {
            ["device"] = kb.Profile.UsbId, ["product"] = kb.Product, ["firmware"] = $"{kb.Version:X4}",
            ["created"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
        }.ToJsonString()));
        WriteNew(FactoryConfigFile, config); // last: HasFactory only turns true once everything is on disk
    }

    /// <summary>Writes through a temp file so a crash never leaves a half-written file behind; keeps an existing file.</summary>
    static void WriteNew(string path, byte[] data)
    {
        if (File.Exists(path)) return;
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, data);
        File.Move(tmp, path, overwrite: false);
    }

    public static byte[]? FactoryConfig() => File.Exists(FactoryConfigFile) ? File.ReadAllBytes(FactoryConfigFile) : null;

    // ---- settings ---------------------------------------------------------------------------

    static JsonObject? _settings;
    static readonly object Gate = new();

    static JsonObject Settings()
    {
        if (_settings != null) return _settings;
        try { _settings = JsonNode.Parse(File.ReadAllText(SettingsFile)) as JsonObject; } catch { }
        return _settings ??= new JsonObject();
    }

    public static T Get<T>(string key, T fallback)
    {
        lock (Gate)
        {
            try { return Settings()[key] is { } n ? n.Deserialize<T>() ?? fallback : fallback; }
            catch { return fallback; }
        }
    }

    public static void Set<T>(string key, T value)
    {
        lock (Gate)
        {
            _settings = null; // re-read: another process may have written other keys meanwhile
            Settings()[key] = JsonSerializer.SerializeToNode(value);
            File.WriteAllText(SettingsFile, Settings().ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
