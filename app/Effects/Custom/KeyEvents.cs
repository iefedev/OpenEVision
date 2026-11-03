namespace OpenEVision.Effects.Custom;

/// <summary>
/// One shared keyboard hook for all reactive layers. Reference-counted: the hook exists only while a custom effect
/// with a reactive layer is running.
/// </summary>
public static class KeyEvents
{
    static readonly object Gate = new();
    static KeyHook? _hook;
    static int _refs;
    static readonly HashSet<string> Down = [];

    public static event Action<string>? Pressed;
    public static event Action<string>? Released;

    public static bool IsDown(string code) { lock (Down) return Down.Contains(code); }

    public static IDisposable Acquire()
    {
        lock (Gate)
        {
            if (_refs++ == 0) _hook = new KeyHook(OnDown, OnUp);
        }
        return new Release();
    }

#if DEBUG
    /// <summary>Tests and developer checks only: behave as if a key went down and up.</summary>
    internal static void Simulate(string code)
    {
        OnDown(code);
        OnUp(code);
    }
#endif

    static void OnDown(string code)
    {
        lock (Down) Down.Add(code);
        Pressed?.Invoke(code);
    }

    static void OnUp(string code)
    {
        lock (Down) Down.Remove(code);
        Released?.Invoke(code);
    }

    sealed class Release : IDisposable
    {
        bool _done;
        public void Dispose()
        {
            if (_done) return;
            _done = true;
            lock (Gate)
            {
                if (--_refs > 0) return;
                _hook?.Dispose();
                _hook = null;
            }
            lock (Down) Down.Clear();
        }
    }
}
