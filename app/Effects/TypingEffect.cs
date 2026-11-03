using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OpenEVision.Effects;

public sealed class TypingEffect : Effect
{
    public static readonly ParamDef[] Params =
    [
        new("style", "Style", ParamKind.Select, "ripple", Options: [("ripple", "Ripple"), ("fade", "Fading trail")]),
        new("color", "Color", ParamKind.Color, "00c0ff"),
        new("random", "Random color on every press", ParamKind.Check, true),
        new("duration", "Duration (s)", ParamKind.Range, 1.0, 0.2, 3, 0.1),
        new("background", "Background", ParamKind.Color, "000008"),
    ];

    readonly KeyHook _hook;
    readonly List<(int Led, long T0, (float R, float G, float B) Color)> _events = [];
    readonly Random _rnd = new();

    public TypingEffect(Ctx c, Dictionary<string, object> p) : base(c, p) => _hook = new KeyHook(OnKey);

    void OnKey(string code)
    {
        if (C.Led(code) is not int led) return;
        var color = B("random") ? Hsv(_rnd.NextDouble()) : Col("color");
        lock (_events)
        {
            _events.Add((led, Stopwatch.GetTimestamp(), color));
            if (_events.Count > 48) _events.RemoveAt(0);
        }
    }

    public override void Render(double t, double dt, float[] rgb)
    {
        var bg = Col("background");
        for (int i = 0; i < Ctx.N; i++) Set(rgb, i, bg);
        double dur = D("duration");
        bool fade = S("style") == "fade";
        (int Led, long T0, (float R, float G, float B) Color)[] evs;
        lock (_events) evs = _events.ToArray();
        foreach (var (led, t0, color) in evs)
        {
            double age = Stopwatch.GetElapsedTime(t0).TotalSeconds / dur;
            if (age >= 1) continue;
            if (fade) { Max(rgb, led, color, 1 - age); continue; }
            for (int i = 0; i < Ctx.N; i++)
            {
                double dx = C.X[i] - C.X[led], dy = (C.Y[i] - C.Y[led]) * 1.2;
                double d = Math.Sqrt(dx * dx + dy * dy) - age * 12;
                double ring = Math.Exp(-d * d / 1.2) * (1 - age);
                if (i == led) ring = Math.Max(ring, 1 - age);
                Max(rgb, i, color, ring);
            }
        }
    }

    public override void Dispose() => _hook.Dispose();
}

/// <summary>
/// System-wide low-level keyboard hook on its own thread with a message loop (so UI stalls can't time it out).
/// Reports only which physical key went down; nothing is stored. AltGr's synthetic LCtrl (scan 0x21D) is ignored.
/// </summary>
public sealed class KeyHook : IDisposable
{
    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr extra; }

    [StructLayout(LayoutKind.Sequential)]
    struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }

    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr mod, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern int GetMessage(out MSG m, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool PostThreadMessage(uint thread, uint msg, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string? name);

    readonly Action<string> _onKey;
    readonly Action<string>? _onUp;
    readonly HookProc _proc; // keep alive for the native side
    readonly HashSet<string> _down = [];
    readonly Thread _thread;
    uint _tid;

    public KeyHook(Action<string> onKey, Action<string>? onUp = null)
    {
        _onKey = onKey;
        _onUp = onUp;
        _proc = Proc;
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "keyhook" };
        _thread.Start();
        ready.Wait(2000);
    }

    void Run(ManualResetEventSlim ready)
    {
        _tid = GetCurrentThreadId();
        var hook = SetWindowsHookEx(13, _proc, GetModuleHandle(null), 0); // WH_KEYBOARD_LL
        ready.Set();
        while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { }
        UnhookWindowsHookEx(hook);
    }

    IntPtr Proc(int n, IntPtr w, IntPtr l)
    {
        if (n == 0)
        {
            var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(l);
            if ((k.scanCode & 0x200) == 0 && ScanCodes.ToCode(k.vkCode, k.scanCode, (k.flags & 1) != 0) is { } code)
            {
                int msg = (int)w;
                if (msg is 0x100 or 0x104) // WM_KEYDOWN, WM_SYSKEYDOWN
                {
                    if (_down.Add(code)) { try { _onKey(code); } catch { } } // ignore auto-repeat
                }
                else if (_down.Remove(code)) { try { _onUp?.Invoke(code); } catch { } }
            }
        }
        return CallNextHookEx(IntPtr.Zero, n, w, l);
    }

    public void Dispose()
    {
        if (_tid != 0) PostThreadMessage(_tid, 0x0012, IntPtr.Zero, IntPtr.Zero); // WM_QUIT
        _thread.Join(2000);
    }
}

/// <summary>Set-1 scan codes (as seen by the low-level hook) to KeyboardEvent.code names.</summary>
static class ScanCodes
{
    static readonly Dictionary<uint, string> Plain = Build(), Ext = new()
    {
        [0x1C] = "NumpadEnter", [0x1D] = "ControlRight", [0x35] = "NumpadDivide", [0x37] = "PrintScreen", [0x38] = "AltRight",
        [0x47] = "Home", [0x48] = "ArrowUp", [0x49] = "PageUp", [0x4B] = "ArrowLeft", [0x4D] = "ArrowRight", [0x4F] = "End",
        [0x50] = "ArrowDown", [0x51] = "PageDown", [0x52] = "Insert", [0x53] = "Delete", [0x5B] = "MetaLeft",
        [0x5C] = "MetaRight", [0x5D] = "ContextMenu",
    };

    static Dictionary<uint, string> Build()
    {
        var m = new Dictionary<uint, string>
        {
            [0x01] = "Escape", [0x0C] = "Minus", [0x0D] = "Equal", [0x0E] = "Backspace", [0x0F] = "Tab", [0x1A] = "BracketLeft",
            [0x1B] = "BracketRight", [0x1C] = "Enter", [0x1D] = "ControlLeft", [0x27] = "Semicolon", [0x28] = "Quote",
            [0x29] = "Backquote", [0x2A] = "ShiftLeft", [0x2B] = "Backslash", [0x33] = "Comma", [0x34] = "Period", [0x35] = "Slash",
            [0x36] = "ShiftRight", [0x37] = "NumpadMultiply", [0x38] = "AltLeft", [0x39] = "Space", [0x3A] = "CapsLock",
            [0x46] = "ScrollLock", [0x47] = "Numpad7", [0x48] = "Numpad8", [0x49] = "Numpad9", [0x4A] = "NumpadSubtract",
            [0x4B] = "Numpad4", [0x4C] = "Numpad5", [0x4D] = "Numpad6", [0x4E] = "NumpadAdd", [0x4F] = "Numpad1", [0x50] = "Numpad2",
            [0x51] = "Numpad3", [0x52] = "Numpad0", [0x53] = "NumpadDecimal", [0x56] = "IntlBackslash", [0x57] = "F11", [0x58] = "F12",
        };
        for (uint i = 0; i < 10; i++) m[0x02 + i] = $"Digit{(i + 1) % 10}";
        for (uint i = 0; i < 10; i++) m[0x10 + i] = $"Key{"QWERTYUIOP"[(int)i]}";
        for (uint i = 0; i < 9; i++) m[0x1E + i] = $"Key{"ASDFGHJKL"[(int)i]}";
        for (uint i = 0; i < 7; i++) m[0x2C + i] = $"Key{"ZXCVBNM"[(int)i]}";
        for (uint i = 0; i < 10; i++) m[0x3B + i] = $"F{i + 1}";
        return m;
    }

    public static string? ToCode(uint vk, uint scan, bool extended) => vk switch
    {
        0x13 => "Pause",
        0x90 => "NumLock",
        0x2C => "PrintScreen",
        _ => (extended ? Ext : Plain).GetValueOrDefault(scan),
    };
}
