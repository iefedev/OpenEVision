using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OpenEVision.Device;
using OpenEVision.Effects;

namespace OpenEVision.Pages;

/// <summary>Per-key colors of the Custom hardware mode, with a live (RAM) preview while painting.</summary>
public partial class PaintPage : Page
{
    static readonly string[] PaletteColors = ["FF0040", "FF8000", "FFFF00", "00FF40", "00C0FF", "4040FF", "C000FF", "FFFFFF", "000000"];

    readonly KeyboardService _kb = KeyboardService.Instance;
    byte[] _saved = new byte[KeyboardService.LedCount * 3];
    byte[] _colors = new byte[KeyboardService.LedCount * 3];
    Color _brush = Color.FromRgb(0xFF, 0x00, 0x40);
    bool _liveActive;
    // Coalesce live frames: at most one in flight, the newest wins.
    int _livePending, _liveBusy;

    public PaintPage()
    {
        InitializeComponent();
        foreach (var hex in PaletteColors)
        {
            var c = (Color)ColorConverter.ConvertFromString("#" + hex);
            var b = new Button { Width = 30, Height = 30, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(0), Background = new SolidColorBrush(c), ToolTip = "#" + hex };
            b.Click += (_, _) => SetBrush(c);
            Palette.Children.Add(b);
        }
        SetBrush(_brush);
        Board.KeyPressed += Paint;
        Board.KeyDragOver += Paint;
        Loaded += async (_, _) => await LoadAsync();
        Unloaded += async (_, _) => await EndLiveAsync();
    }

    async Task LoadAsync()
    {
        try
        {
            _saved = await _kb.GetCustomAsync();
            _colors = (byte[])_saved.Clone();
            Board.SetColors(_colors);
            Error.IsOpen = false;
        }
        catch (Exception e) { ShowError(e); }
    }

    void Paint(string code)
    {
        if (!KeyLayout.CustomLed.TryGetValue(code, out int led)) return;
        _colors[led * 3] = _brush.R; _colors[led * 3 + 1] = _brush.G; _colors[led * 3 + 2] = _brush.B;
        Board.SetColors(_colors);
        _ = PushLiveAsync();
    }

    async Task PushLiveAsync()
    {
        if (LiveSwitch.IsChecked != true) return;
        Interlocked.Exchange(ref _livePending, 1);
        if (Interlocked.Exchange(ref _liveBusy, 1) == 1) return;
        try
        {
            if (!_liveActive)
            {
                await EffectEngine.Instance.StopAsync();
                _liveActive = true;
            }
            while (Interlocked.Exchange(ref _livePending, 0) == 1)
                await LiveService.Instance.ShowAsync((byte[])_colors.Clone());
        }
        catch (Exception e) { ShowError(e); }
        finally { Interlocked.Exchange(ref _liveBusy, 0); }
        if (Volatile.Read(ref _livePending) == 1) await PushLiveAsync(); // a stroke landed while we were finishing
    }

    async Task EndLiveAsync()
    {
        if (!_liveActive) return;
        _liveActive = false;
        try { await LiveService.Instance.EndAsync(); } catch { }
    }

    async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _kb.SetCustomAsync(_colors);
            await EndLiveAsync();
            _saved = await _kb.GetCustomAsync();
            bool same = _saved.AsSpan().SequenceEqual(_colors);
            StatusText.Text = same ? $"Saved to the keyboard, Custom mode active · {DateTime.Now:HH:mm:ss}" : "Saved, but the colors read back are different.";
            Error.IsOpen = false;
        }
        catch (Exception ex) { ShowError(ex); }
    }

    async void Discard_Click(object sender, RoutedEventArgs e)
    {
        _colors = (byte[])_saved.Clone();
        Board.SetColors(_colors);
        await EndLiveAsync();
        StatusText.Text = "Changes discarded.";
    }

    void Fill_Click(object sender, RoutedEventArgs e) => FillAll(_brush);
    void Clear_Click(object sender, RoutedEventArgs e) => FillAll(Colors.Black);

    void FillAll(Color c)
    {
        for (int i = 0; i < KeyboardService.LedCount; i++) { _colors[i * 3] = c.R; _colors[i * 3 + 1] = c.G; _colors[i * 3 + 2] = c.B; }
        Board.SetColors(_colors);
        _ = PushLiveAsync();
    }

    async void Live_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        if (LiveSwitch.IsChecked == true) await PushLiveAsync(); else await EndLiveAsync();
    }

    void SetBrush(Color c)
    {
        _brush = c;
        BrushSwatch.Background = new SolidColorBrush(c);
        HexBox.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    void Hex_LostFocus(object sender, RoutedEventArgs e)
    {
        var s = HexBox.Text.Trim().TrimStart('#');
        if (s.Length == 6 && uint.TryParse(s, NumberStyles.HexNumber, null, out var v))
            SetBrush(Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v));
        else SetBrush(_brush);
    }

    void Hex_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Hex_LostFocus(sender, e);
    }

    void ShowError(Exception e)
    {
        Dispatcher.Invoke(() => { Error.Message = e.Message; Error.IsOpen = true; });
    }
}
