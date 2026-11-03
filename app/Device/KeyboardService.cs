using System.IO;

namespace OpenEVision.Device;

public record HardwareMode(byte Mode, byte Brightness, byte Speed, byte Direction, bool Random, byte R, byte G, byte B);

/// <summary>
/// One shared device handle for the whole app. All I/O is serialized and runs off the UI thread;
/// a failed call reopens the handle once (unplug/replug).
/// Before the first command on a newly opened keyboard runs, the keyboard's own factory dump is read and saved
/// (once per data folder), so nothing is ever written to a keyboard whose original state is not on disk.
/// </summary>
public sealed class KeyboardService
{
    public static DeviceProfile Profile => DeviceProfiles.Active;
    public static int LedCount => Profile.LedCount;
    public static int KeyTableSize => Profile.KeyTableEntries * 3;
    const int ModeBytes = 8;
    public const byte ModeCustom = 0x14;

    public static readonly KeyboardService Instance = new();

    readonly SemaphoreSlim _lock = new(1, 1);
    HidKeyboard? _kb;

    public event Action<bool>? ConnectionChanged;
    public bool IsConnected { get; private set; }

    /// <summary>Hardware lighting modes (config byte 0), names as in OpenRGB's EVision controller.</summary>
    public static readonly IReadOnlyDictionary<byte, string> Modes = new Dictionary<byte, string>
    {
        [0x01] = "Wave (short)", [0x02] = "Wave (long)", [0x03] = "Color wheel", [0x04] = "Spectrum cycle",
        [0x05] = "Breathing", [0x06] = "Static", [0x07] = "Reactive", [0x08] = "Reactive ripple", [0x09] = "Reactive line",
        [0x0A] = "Starlight (fast)", [0x0B] = "Blooming", [0x0C] = "Rainbow wave (vertical)", [0x0D] = "Hurricane",
        [0x0E] = "Accumulate", [0x0F] = "Starlight (slow)", [0x10] = "Visor", [0x11] = "Surmount", [0x12] = "Rainbow wave (circular)",
        [ModeCustom] = "Custom (per key)",
    };

    // One transaction on the keyboard at a time across processes (e.g. a second copy started with a developer switch).
    // A Mutex is thread-affine, so it is taken and released inside the same synchronous block below.
    static readonly Mutex DeviceMutex = new(false, @"Local\OpenEVision.Device");

    public Task<T> RunAsync<T>(Func<HidKeyboard, T> fn) => Task.Run(async () =>
    {
        await _lock.WaitAsync();
        try { return RunLocked(fn); }
        finally { _lock.Release(); }
    });

    T RunLocked<T>(Func<HidKeyboard, T> fn)
    {
        try
        {
            if (!DeviceMutex.WaitOne(5000)) throw new IOException("The keyboard is busy (another OpenEVision process is using it).");
        }
        catch (AbandonedMutexException) { }
        try
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    if (_kb == null)
                    {
                        var kb = HidKeyboard.Open();
                        try { Storage.EnsureFactory(kb); }
                        catch { kb.Dispose(); throw; }
                        _kb = kb;
                    }
                    var r = fn(_kb);
                    SetConnected(true);
                    return r;
                }
                catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException)
                {
                    _kb?.Dispose();
                    _kb = null;
                    if (attempt == 2) { SetConnected(false); throw; }
                }
            }
        }
        finally { DeviceMutex.ReleaseMutex(); }
    }

    public Task RunAsync(Action<HidKeyboard> fn) => RunAsync<bool>(kb => { fn(kb); return true; });

    /// <summary>Connects (which saves the factory dump on first contact) without doing anything else.</summary>
    public Task EnsureFactoryAsync() => RunAsync(_ => { });

    void SetConnected(bool v)
    {
        if (IsConnected == v) return;
        IsConnected = v;
        ConnectionChanged?.Invoke(v);
    }

    public Task<HardwareMode> GetModeAsync() => RunAsync(kb =>
    {
        var d = kb.Read(HidKeyboard.CmdReadConfig, 0, ModeBytes);
        return new HardwareMode(d[0], d[1], d[2], d[3], d[4] != 0, d[5], d[6], d[7]);
    });

    public Task SetModeAsync(HardwareMode m) => RunAsync(kb => kb.Configure(() =>
        kb.Write(HidKeyboard.CmdWriteConfig, 0,
            [m.Mode, m.Brightness, m.Speed, m.Direction, (byte)(m.Random ? 1 : 0), m.R, m.G, m.B])));

    public Task<(string Product, ushort Version)> GetInfoAsync() => RunAsync(kb => (kb.Product, kb.Version));

    /// <summary>Custom-mode colors stored in flash, in custom LED order (row * Columns + slot).</summary>
    public Task<byte[]> GetCustomAsync() => RunAsync(kb => kb.Read(HidKeyboard.CmdReadCustom, 0, LedCount * 3));

    /// <summary>Writes per-LED colors to flash and (optionally) switches to the Custom mode, keeping brightness.</summary>
    public Task SetCustomAsync(byte[] colors, bool activate = true) => RunAsync(kb => kb.Configure(() =>
    {
        kb.Write(HidKeyboard.CmdWriteCustom, 0, colors.AsSpan(0, LedCount * 3));
        if (activate) kb.Write(HidKeyboard.CmdWriteConfig, 0, [ModeCustom]);
    }));

    /// <summary>Active key-assignment table (3 bytes per entry).</summary>
    public Task<byte[]> GetKeyTableAsync() => RunAsync(kb => kb.Read(HidKeyboard.CmdReadConfig, Profile.KeyTableOffset, KeyTableSize));

    /// <summary>Writes single key-table entries; untouched entries stay as they are.</summary>
    public Task SetKeysAsync(IReadOnlyDictionary<int, byte[]> entries) => RunAsync(kb => kb.Configure(() =>
    {
        foreach (var (i, e) in entries.OrderBy(x => x.Key)) kb.Write(HidKeyboard.CmdWriteConfig, Profile.KeyTableOffset + i * 3, e);
    }));

    public Task<(byte[] Config, byte[] Custom)> RawDumpAsync() => RunAsync(RawDump);

    public static (byte[] Config, byte[] Custom) RawDump(HidKeyboard kb) =>
        (kb.Read(HidKeyboard.CmdReadConfig, 0, kb.Profile.ConfigSize), kb.Read(HidKeyboard.CmdReadCustom, 0, kb.Profile.CustomSize));

    /// <summary>Writes back the regions this app changes: effect header, key table, custom colors.</summary>
    public Task RestoreAsync(byte[] config, byte[] custom) => RunAsync(kb => kb.Configure(() =>
    {
        var p = kb.Profile;
        kb.Write(HidKeyboard.CmdWriteConfig, 0, config.AsSpan(0, p.HeaderSize));
        kb.Write(HidKeyboard.CmdWriteConfig, p.KeyTableOffset, config.AsSpan(p.KeyTableOffset, KeyTableSize));
        kb.Write(HidKeyboard.CmdWriteCustom, 0, custom.AsSpan(0, p.LedCount * 3));
    }));
}
