using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OpenEVision.Device;

/// <summary>Moves one 64-byte request report to the keyboard and returns the matching reply report.</summary>
public interface IReportTransport : IDisposable
{
    string Product { get; }
    /// <summary>bcdDevice from the USB descriptor, e.g. 0x0104.</summary>
    ushort Version { get; }
    /// <summary>Sends the request and returns the reply with the same cmd + offset, or throws TimeoutException.</summary>
    byte[] Exchange(byte[] request, int timeoutMs);
}

/// <summary>
/// EVision vendor protocol on top of a report transport (normally the vendor HID interface).
/// Packet: [0]=report id 4, [1..2]=checksum of bytes 3..63 (LE), [3]=cmd, [4]=size, [5..6]=offset (LE), [7]=status, [8..]=data.
/// </summary>
public sealed class HidKeyboard : IDisposable
{
    public const int Pkt = 64;
    public const byte ReportId = 0x04;

    public const byte CmdBegin = 0x01, CmdEnd = 0x02, CmdReadCaps = 0x03, CmdReadConfig = 0x05, CmdWriteConfig = 0x06,
        CmdReadCustom = 0x10, CmdWriteCustom = 0x11, CmdLive = 0x12, CmdLiveEnd = 0x13;

    readonly IReportTransport _io;
    public DeviceProfile Profile { get; }
    public ushort Version => _io.Version;
    public string Product => _io.Product;

    internal HidKeyboard(IReportTransport io, DeviceProfile profile)
    {
        _io = io;
        Profile = profile;
    }

    /// <summary>Opens the active profile's keyboard, or throws IOException when it is not connected.</summary>
    public static HidKeyboard Open()
    {
        var profile = DeviceProfiles.Active;
#if DEBUG
        if (Dev.DemoMode.Enabled) return new HidKeyboard(new Dev.DemoTransport(profile), profile);
#endif
        return new HidKeyboard(HidTransport.Open(profile), profile);
    }

    /// <summary>Builds a request report with its checksum.</summary>
    public static byte[] Packet(byte cmd, ushort offset, ReadOnlySpan<byte> data, byte size)
    {
        var b = new byte[Pkt];
        b[0] = ReportId; b[3] = cmd; b[4] = data.IsEmpty ? size : (byte)data.Length;
        b[5] = (byte)offset; b[6] = (byte)(offset >> 8);
        data.CopyTo(b.AsSpan(8));
        int sum = 0;
        for (int i = 3; i < Pkt; i++) sum += b[i];
        b[1] = (byte)sum; b[2] = (byte)(sum >> 8);
        return b;
    }

    /// <summary>One request/reply. Returns the reply's (status, size, payload).</summary>
    public (byte Status, byte Size, byte[] Data) Query(byte cmd, ushort offset = 0, ReadOnlySpan<byte> data = default, byte size = 0, int timeoutMs = 500)
    {
        var r = _io.Exchange(Packet(cmd, offset, data, size), timeoutMs);
        return (r[7], r[4], r.AsSpan(8, Math.Min((int)r[4], Pkt - 8)).ToArray());
    }

    public byte[] Read(byte cmd, int offset, int size)
    {
        var outBuf = new List<byte>(size);
        while (size > 0)
        {
            byte n = (byte)Math.Min(size, Pkt - 8);
            var (status, got, data) = Query(cmd, (ushort)offset, size: n);
            if (status != 0 || got == 0) throw new IOException($"Read refused (cmd 0x{cmd:X2}, offset 0x{offset:X}, status {status}).");
            outBuf.AddRange(data);
            offset += got; size -= got;
        }
        return outBuf.ToArray();
    }

    /// <summary>Regions this app may write; everything else on the keyboard is unknown territory.</summary>
    public static bool WriteAllowed(DeviceProfile p, byte cmd, int offset, int length) => offset >= 0 && length >= 0 && cmd switch
    {
        CmdWriteConfig => offset + length <= p.HeaderSize
                          || (offset >= p.KeyTableOffset && offset + length <= p.KeyTableOffset + p.KeyTableEntries * 3),
        CmdWriteCustom => offset + length <= p.LedCount * 3,
        CmdLive => offset + length <= p.KeyTableEntries * 3,
        _ => false,
    };

    public void Write(byte cmd, int offset, ReadOnlySpan<byte> data)
    {
        if (!WriteAllowed(Profile, cmd, offset, data.Length))
            throw new InvalidOperationException($"Write blocked by the safety guard: cmd 0x{cmd:X2} offset 0x{offset:X} length {data.Length}.");
        while (!data.IsEmpty)
        {
            var chunk = data[..Math.Min(data.Length, Pkt - 8)];
            var (status, _, _) = Query(cmd, (ushort)offset, chunk);
            if (status != 0) throw new IOException($"Write refused (cmd 0x{cmd:X2}, offset 0x{offset:X}, status {status}).");
            offset += chunk.Length;
            data = data[chunk.Length..];
        }
    }

    /// <summary>Begin/end bracket used by the vendor for configuration writes.</summary>
    public void Configure(Action body)
    {
        Query(CmdBegin);
        try { body(); }
        finally { Query(CmdEnd); }
    }

    public void Dispose() => _io.Dispose();
}

/// <summary>The vendor HID interface opened through hid.dll / setupapi.dll.</summary>
sealed class HidTransport : IReportTransport
{
    readonly SafeFileHandle _handle;
    readonly FileStream _stream;
    public ushort Version { get; }
    public string Product { get; }

    HidTransport(SafeFileHandle handle)
    {
        _handle = handle;
        (Version, Product) = Native.Describe(handle);
        // bufferSize 0: every Read/Write is exactly one HID report.
        _stream = new FileStream(handle, FileAccess.ReadWrite, 0, isAsync: true);
    }

    public static HidTransport Open(DeviceProfile profile)
    {
        foreach (var path in Native.HidDevicePaths())
        {
            if (!path.Contains($"vid_{profile.Vid:x4}&pid_{profile.Pid:x4}", StringComparison.OrdinalIgnoreCase)) continue;
            var h = Native.CreateFile(path, Native.GenericRead | Native.GenericWrite, 3, IntPtr.Zero, 3, Native.FileFlagOverlapped, IntPtr.Zero);
            if (h.IsInvalid) continue;
            var (page, outLen) = Native.CapsOf(h);
            if (page == profile.UsagePage)
            {
                if (outLen != HidKeyboard.Pkt)
                {
                    h.Dispose();
                    throw new IOException($"Unexpected report length {outLen} on the vendor interface (expected {HidKeyboard.Pkt}).");
                }
                return new HidTransport(h);
            }
            h.Dispose();
        }
        throw new IOException(DeviceProfiles.NotFoundMessage);
    }

    public byte[] Exchange(byte[] b, int timeoutMs)
    {
        // Every open handle (including other programs') receives every input report: drop stale ones, and match the
        // reply on cmd + offset. FileStream completes a cancelled overlapped read before returning, so buffers are safe.
        Native.HidD_FlushQueue(_handle);
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            _stream.WriteAsync(b, cts.Token).AsTask().GetAwaiter().GetResult();
            var r = new byte[HidKeyboard.Pkt];
            for (int tries = 0; tries < 8; tries++)
            {
                int n = _stream.ReadAsync(r, cts.Token).AsTask().GetAwaiter().GetResult();
                if (n == 0) break;
                if (r[0] == HidKeyboard.ReportId && r[3] == b[3] && r[5] == b[5] && r[6] == b[6]) return r;
            }
        }
        catch (OperationCanceledException) { }
        throw new TimeoutException($"The keyboard did not answer command 0x{b[3]:X2}.");
    }

    public void Dispose()
    {
        _stream.Dispose();
        _handle.Dispose();
    }

    static class Native
    {
        public const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, FileFlagOverlapped = 0x40000000;

        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid guid; public int flags; public IntPtr reserved; }

        [StructLayout(LayoutKind.Sequential)]
        struct HIDP_CAPS
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 27)] public ushort[] rest;
        }

        [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
        [DllImport("hid.dll")] public static extern bool HidD_FlushQueue(SafeFileHandle h);
        [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
        [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr p);
        [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr p, out HIDP_CAPS caps);
        [DllImport("setupapi.dll", SetLastError = true)] static extern IntPtr SetupDiGetClassDevsW(ref Guid g, IntPtr e, IntPtr w, int flags);
        [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, int i, ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr s, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int needed, IntPtr info);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sa, uint creation, uint flags, IntPtr template);

        public static IEnumerable<string> HidDevicePaths()
        {
            HidD_GetHidGuid(out var g);
            var set = SetupDiGetClassDevsW(ref g, IntPtr.Zero, IntPtr.Zero, 0x12); // PRESENT | DEVICEINTERFACE
            if (set == new IntPtr(-1)) throw new Win32Exception();
            var paths = new List<string>();
            try
            {
                for (int i = 0; ; i++)
                {
                    var d = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref d)) break;
                    SetupDiGetDeviceInterfaceDetailW(set, ref d, IntPtr.Zero, 0, out int needed, IntPtr.Zero);
                    var buf = Marshal.AllocHGlobal(needed);
                    try
                    {
                        Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6); // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W
                        if (SetupDiGetDeviceInterfaceDetailW(set, ref d, buf, needed, out _, IntPtr.Zero))
                            paths.Add(Marshal.PtrToStringUni(buf + 4)!);
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return paths;
        }

        [StructLayout(LayoutKind.Sequential)] struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID, ProductID, VersionNumber; }
        [DllImport("hid.dll")] static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
        [DllImport("hid.dll", CharSet = CharSet.Unicode)] static extern bool HidD_GetProductString(SafeFileHandle h, char[] buf, int len);

        public static (ushort Version, string Product) Describe(SafeFileHandle h)
        {
            var a = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
            HidD_GetAttributes(h, ref a);
            var buf = new char[128];
            var name = HidD_GetProductString(h, buf, buf.Length * 2) ? new string(buf).TrimEnd('\0') : "RGB Keyboard";
            return (a.VersionNumber, name);
        }

        public static (ushort UsagePage, int OutputLength) CapsOf(SafeFileHandle h)
        {
            if (!HidD_GetPreparsedData(h, out var p)) return (0, 0);
            try
            {
                return HidP_GetCaps(p, out var caps) == 0x00110000 // HIDP_STATUS_SUCCESS
                    ? (caps.UsagePage, caps.OutputReportByteLength) : ((ushort)0, 0);
            }
            finally { HidD_FreePreparsedData(p); }
        }
    }
}
