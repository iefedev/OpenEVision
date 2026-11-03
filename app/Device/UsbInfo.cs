using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OpenEVision.Device;

/// <summary>
/// Reads the keyboard's endpoint descriptors from its USB hub to find the real polling interval (bInterval).
/// The keyboard's own "polling" config byte is not trusted (it contradicts the descriptor).
/// </summary>
public static class UsbInfo
{
    static string VidPid => $@"USB\VID_{DeviceProfiles.Active.Vid:X4}&PID_{DeviceProfiles.Active.Pid:X4}\";
    const string HubGuid = "{f18a0e88-c30c-11d0-8815-00a0c906bed8}";
    const uint IoctlConnectionInfoEx = 0x220448, IoctlDescriptor = 0x220410;

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Get_Device_ID_List_SizeW(out int len, string filter, int flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Get_Device_ID_ListW(string filter, char[] buf, int len, int flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Locate_DevNodeW(out int node, string id, int flags);
    [DllImport("cfgmgr32.dll")] static extern int CM_Get_DevNode_Registry_PropertyW(int node, int prop, IntPtr type, out int value, ref int len, int flags);
    [DllImport("cfgmgr32.dll")] static extern int CM_Get_Parent(out int parent, int node, int flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Get_Device_IDW(int node, char[] buf, int len, int flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sa, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(SafeFileHandle h, uint code, byte[] inBuf, int inLen, byte[] outBuf, int outLen, out int returned, IntPtr ov);

    /// <summary>Polling rate in Hz of the keyboard's interrupt IN endpoints, or null when it can't be read.</summary>
    public static int? PollingHz()
    {
#if DEBUG
        if (Dev.DemoMode.Enabled) return 1000;
#endif
        try
        {
            CM_Get_Device_ID_List_SizeW(out int len, "USB", 1); // CM_GETIDLIST_FILTER_ENUMERATOR
            var buf = new char[len];
            CM_Get_Device_ID_ListW("USB", buf, len, 1);
            var id = new string(buf).Split('\0').FirstOrDefault(s => s.StartsWith(VidPid, StringComparison.OrdinalIgnoreCase));
            if (id == null) return null;
            CM_Locate_DevNodeW(out int node, id, 0);
            int size = 4;
            CM_Get_DevNode_Registry_PropertyW(node, 0x1D, IntPtr.Zero, out int port, ref size, 0); // CM_DRP_ADDRESS
            CM_Get_Parent(out int parent, node, 0);
            var pid = new char[400];
            CM_Get_Device_IDW(parent, pid, pid.Length, 0);
            var hubPath = @"\\?\" + new string(pid).TrimEnd('\0').Replace('\\', '#') + "#" + HubGuid;

            using var h = CreateFile(hubPath, 0x40000000, 2, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (h.IsInvalid) return null;

            var info = new byte[128];
            var inInfo = new byte[68];
            BitConverter.GetBytes(port).CopyTo(inInfo, 0);
            if (!DeviceIoControl(h, IoctlConnectionInfoEx, inInfo, inInfo.Length, info, info.Length, out _, IntPtr.Zero)) return null;
            int speed = info[4 + 18 + 1]; // after ConnectionIndex, device descriptor, CurrentConfigurationValue

            // USB_DESCRIPTOR_REQUEST: ConnectionIndex, setup packet (GET_DESCRIPTOR configuration 0, 512 bytes), data.
            var req = new byte[12 + 512];
            BitConverter.GetBytes(port).CopyTo(req, 0);
            req[4] = 0x80; req[5] = 0x06; req[6] = 0x00; req[7] = 0x02; req[10] = 0x00; req[11] = 0x02;
            var resp = new byte[req.Length];
            if (!DeviceIoControl(h, IoctlDescriptor, req, req.Length, resp, resp.Length, out int got, IntPtr.Zero)) return null;

            int best = 0;
            for (int i = 12; i + 6 < got && resp[i] != 0; i += resp[i])
            {
                if (resp[i + 1] != 5 || (resp[i + 2] & 0x80) == 0 || (resp[i + 3] & 3) != 3) continue; // interrupt IN endpoint
                int interval = resp[i + 6];
                double ms = speed < 2 ? interval : Math.Pow(2, interval - 1) * 0.125;
                if (ms > 0) best = Math.Max(best, (int)Math.Round(1000 / ms));
            }
            return best > 0 ? best : null;
        }
        catch { return null; }
    }
}
