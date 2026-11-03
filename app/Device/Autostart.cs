using Microsoft.Win32;

namespace OpenEVision.Device;

/// <summary>Start with Windows via HKCU\Software\Microsoft\Windows\CurrentVersion\Run (value "OpenEVision", started with --minimized).</summary>
public static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "OpenEVision";

    static string Command => $"\"{Environment.ProcessPath}\" --minimized";

    public static bool Enabled
    {
        get
        {
#if DEBUG
            if (Dev.DemoMode.Enabled) return false;
#endif
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(Name) is string v && v == Command;
        }
        set
        {
#if DEBUG
            if (Dev.DemoMode.Enabled) return; // demo mode never touches the registry
#endif
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) k.SetValue(Name, Command);
            else k.DeleteValue(Name, throwOnMissingValue: false);
        }
    }
}
