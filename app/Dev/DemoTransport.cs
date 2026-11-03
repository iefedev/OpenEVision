using OpenEVision.Device;
using OpenEVision.Effects;

namespace OpenEVision.Dev;

/// <summary>
/// A simulated EVision keyboard for demo mode and tests: answers the vendor commands from in-memory config and custom
/// color tables. The memory lives for the whole process, so reconnecting keeps earlier writes.
/// </summary>
public sealed class DemoTransport : IReportTransport
{
    static readonly object Gate = new();
    static byte[]? _config, _custom;

    public string Product => "EVision RGB Keyboard (demo)";
    public ushort Version => 0x0104;

    public DemoTransport(DeviceProfile profile)
    {
        lock (Gate)
        {
            if (_config != null) return;
            (_config, _custom) = FactoryMemory(profile);
            // Show a couple of remapped keys on the remap page: CapsLock -> Escape, ScrollLock -> Play/Pause.
            int Index(string code) => LiveMap.Decode(_config.AsSpan(profile.KeyTableOffset, profile.KeyTableEntries * 3))[code];
            KeyActions.ToBytes("k:29").CopyTo(_config, profile.KeyTableOffset + Index("CapsLock") * 3);
            KeyActions.ToBytes("c:00cd").CopyTo(_config, profile.KeyTableOffset + Index("ScrollLock") * 3);
        }
    }

    /// <summary>Resets the simulated keyboard to its factory memory (tests).</summary>
    public static void Reset()
    {
        lock (Gate) _config = _custom = null;
    }

    /// <summary>Plausible factory memory: a hardware mode, one key-table entry per key, rainbow custom colors.</summary>
    public static (byte[] Config, byte[] Custom) FactoryMemory(DeviceProfile p)
    {
        var config = new byte[p.ConfigSize];
        byte[] mode = [0x04, 4, 2, 0, 0, 0x00, 0xC0, 0xFF]; // Spectrum cycle, brightness 4/4
        mode.CopyTo(config, 0);
        int entry = 0;
        foreach (var (code, _) in KeyLayout.CustomLed.OrderBy(x => x.Value))
            if (LiveMap.Encode(code) is { } e && entry < p.KeyTableEntries)
                e.CopyTo(config, p.KeyTableOffset + entry++ * 3);

        var custom = new byte[p.CustomSize];
        foreach (var key in KeyLayout.Keys)
        {
            if (!KeyLayout.CustomLed.TryGetValue(key.Code, out int led)) continue;
            var (r, g, b) = Effect.Hsv((key.X + key.Width / 2) / KeyLayout.TotalWidth * 0.85);
            custom[led * 3] = (byte)(r * 255); custom[led * 3 + 1] = (byte)(g * 255); custom[led * 3 + 2] = (byte)(b * 255);
        }
        return (config, custom);
    }

    public byte[] Exchange(byte[] req, int timeoutMs)
    {
        byte cmd = req[3], size = req[4];
        int offset = req[5] | req[6] << 8;
        var reply = new byte[HidKeyboard.Pkt];
        reply[0] = HidKeyboard.ReportId; reply[3] = cmd; reply[4] = size; reply[5] = req[5]; reply[6] = req[6];
        lock (Gate)
        {
            byte[]? mem = cmd switch
            {
                HidKeyboard.CmdReadConfig or HidKeyboard.CmdWriteConfig => _config,
                HidKeyboard.CmdReadCustom or HidKeyboard.CmdWriteCustom => _custom,
                _ => null,
            };
            bool ok = true;
            switch (cmd)
            {
                case HidKeyboard.CmdReadConfig or HidKeyboard.CmdReadCustom:
                    ok = offset + size <= mem!.Length;
                    if (ok) Array.Copy(mem, offset, reply, 8, size);
                    break;
                case HidKeyboard.CmdWriteConfig or HidKeyboard.CmdWriteCustom:
                    ok = offset + size <= mem!.Length;
                    if (ok) Array.Copy(req, 8, mem, offset, size);
                    break;
                case HidKeyboard.CmdBegin or HidKeyboard.CmdEnd or HidKeyboard.CmdLive or HidKeyboard.CmdLiveEnd:
                    break;
                default:
                    ok = false;
                    break;
            }
            reply[7] = ok ? (byte)0 : (byte)1;
        }
        return reply;
    }

    public void Dispose() { }
}
