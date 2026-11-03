using System.IO;
using System.Windows;
using OpenEVision.Device;
using OpenEVision.Effects;

namespace OpenEVision.Dev;

/// <summary>
/// Debug-only command-line switches:
///   --demo                 run the normal app against a simulated keyboard and a throwaway data folder
///   --snapshot DIR [--theme light|dark]   render every page in demo mode to DIR/*.png and exit
///   --probe FILE           read-only check of the real keyboard (info, mode, config, colors, write guard)
///   --effect-test FILE     run every software effect briefly on the real keyboard (RAM only)
///   --write-test FILE      exercise every flash write path on the real keyboard, then restore it
/// </summary>
public static class DevSwitches
{
    /// <returns>true when a headless switch took over and the normal app must not start.</returns>
    public static bool Handle(App app, string[] args)
    {
        string? Arg(string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        if (args.Any(a => a is "--snapshot" or "--effect-test" or "--write-test" or "--probe")) EffectSettings.ReadOnly = true;
        if (args.Contains("--demo") || args.Contains("--snapshot")) DemoMode.Enable();

        if (Arg("--snapshot") is { } dir)
        {
            App.ApplyTheme(Arg("--theme") ?? "dark");
            Snapshot.Start(dir); // shuts the app down when done
            return true;
        }
        App.ApplyTheme(Storage.Get("theme", "system"));
        if (Arg("--effect-test") is { } fxOut)
        {
            Task.Run(() => File.WriteAllText(fxOut, EffectTest.Run())).ContinueWith(_ => app.Dispatcher.Invoke(app.Shutdown));
            return true;
        }
        if (Arg("--write-test") is { } wOut)
        {
            Task.Run(() => File.WriteAllText(wOut, WriteTest.Run())).ContinueWith(_ => app.Dispatcher.Invoke(app.Shutdown));
            return true;
        }
        if (Arg("--probe") is { } pOut)
        {
            File.WriteAllText(pOut, Probe.Run());
            app.Shutdown();
            return true;
        }
        return false;
    }
}
