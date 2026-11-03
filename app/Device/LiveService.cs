namespace OpenEVision.Device;

/// <summary>
/// RAM-only colors (0x12). Frames come in custom order (row * Columns + slot) and are converted to the live order.
/// The keyboard drops live colors after a short idle time, so the last frame is re-sent every 150 ms until End().
/// </summary>
public sealed class LiveService
{
    public static readonly LiveService Instance = new();
    const int KeepAliveMs = 150;

    readonly KeyboardService _kb = KeyboardService.Instance;
    readonly object _gate = new();
    byte[]? _frame;           // live order, KeyTableEntries * 3
    DateTime _lastSent;
    Dictionary<string, int>? _map;
    readonly Timer _keepAlive;

    LiveService() => _keepAlive = new Timer(_ => KeepAlive(), null, KeepAliveMs / 2, KeepAliveMs / 2);

    /// <summary>
    /// The live order is decoded from the factory key table rather than the current one, so remapped keys keep
    /// lighting at their physical position.
    /// </summary>
    async Task<Dictionary<string, int>> MapAsync()
    {
        if (_map != null) return _map;
        await _kb.EnsureFactoryAsync();
        var p = KeyboardService.Profile;
        var factory = Storage.FactoryConfig() ?? throw new InvalidOperationException("The factory dump is missing.");
        return _map = LiveMap.Decode(factory.AsSpan(p.KeyTableOffset, KeyboardService.KeyTableSize));
    }

    /// <summary>Show colors (custom order, LedCount * 3 bytes) until the next frame or End().</summary>
    public async Task ShowAsync(byte[] customColors)
    {
        var map = await MapAsync();
        var live = new byte[KeyboardService.KeyTableSize];
        foreach (var (code, li) in map)
        {
            if (!KeyLayout.CustomLed.TryGetValue(code, out int ci) || ci * 3 + 2 >= customColors.Length) continue;
            Array.Copy(customColors, ci * 3, live, li * 3, 3);
        }
        lock (_gate) _frame = live;
        await SendAsync(live);
    }

    public async Task EndAsync()
    {
        bool active;
        lock (_gate) { active = _frame != null; _frame = null; }
        if (active) await _kb.RunAsync(k => { k.Query(HidKeyboard.CmdLiveEnd); });
    }

    async Task SendAsync(byte[] live)
    {
        await _kb.RunAsync(k => k.Write(HidKeyboard.CmdLive, 0, live));
        lock (_gate) _lastSent = DateTime.UtcNow;
    }

    void KeepAlive()
    {
        byte[]? f;
        lock (_gate)
        {
            f = _frame;
            if (f == null || (DateTime.UtcNow - _lastSent).TotalMilliseconds < KeepAliveMs) return;
            _lastSent = DateTime.UtcNow; // don't queue a second keepalive while this one is in flight
        }
        try { SendAsync(f).Wait(); } catch { /* unplugged: the next frame or reconnect will tell */ }
    }
}
