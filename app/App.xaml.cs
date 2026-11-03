using System.Windows;
using OpenEVision.Device;
using OpenEVision.Effects;
using Wpf.Ui.Appearance;

namespace OpenEVision;

public partial class App : Application
{
    const string InstanceMutexName = @"Local\OpenEVision.App";
    const string ShowEventName = @"Local\OpenEVision.Show";

    Mutex? _instance;
    EventWaitHandle? _showEvent;
    TrayIcon? _tray;
    MainWindow? _window;
    bool _exiting;

    public static bool CloseToTray
    {
        get => Storage.Get("closeToTray", true);
        set => Storage.Set("closeToTray", value);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
#if DEBUG
        // Developer switches; they must run before anything touches Storage (demo mode uses its own data folder).
        if (Dev.DevSwitches.Handle(this, e.Args)) return;
#endif
        ApplyTheme(Storage.Get("theme", "system"));

        // One instance: a second launch just brings the running one to the front.
        string suffix = "";
#if DEBUG
        if (Dev.DemoMode.Enabled) suffix = ".Demo";
#endif
        _instance = new Mutex(true, InstanceMutexName + suffix, out bool first);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName + suffix);
        if (!first)
        {
            _showEvent.Set();
            Shutdown();
            return;
        }
        new Thread(() =>
        {
            while (_showEvent.WaitOne()) Dispatcher.Invoke(ShowWindow);
        }) { IsBackground = true, Name = "show-signal" }.Start();

        _tray = new TrayIcon(ShowWindow, Quit);
        if (!e.Args.Contains("--minimized")) ShowWindow();
        _ = EffectEngine.Instance.ResumeAsync();
    }

    void ShowWindow()
    {
        if (_window == null)
        {
            _window = new MainWindow();
            _window.Closing += (_, args) =>
            {
                if (_exiting) return;
                if (CloseToTray)
                {
                    args.Cancel = true;
                    _window.Hide();
                    if (!Storage.Get("trayHintShown", false))
                    {
                        _tray?.Notify("OpenEVision is still running", "Effects keep running. Open or quit it from the tray icon.");
                        Storage.Set("trayHintShown", true);
                    }
                }
                else Quit();
            };
            MainWindow = _window;
        }
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    void Quit()
    {
        _exiting = true;
        _window?.Close();
        Shutdown();
    }

    public static void ApplyTheme(string theme)
    {
        switch (theme)
        {
            case "light": ApplicationThemeManager.Apply(ApplicationTheme.Light); break;
            case "dark": ApplicationThemeManager.Apply(ApplicationTheme.Dark); break;
            default: ApplicationThemeManager.ApplySystemTheme(); break;
        }
        ApplicationAccentColorManager.ApplySystemAccent();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Remember the running effect for next start, then hand the keyboard back to its hardware mode.
        EffectSettings.Save();
        Task.Run(() => EffectEngine.Instance.StopAsync(notify: false, forget: false)).Wait(TimeSpan.FromSeconds(3));
        _tray?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
