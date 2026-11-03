using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenEVision.Device;

namespace OpenEVision.Pages;

public partial class DevicePage : Page
{
    const string EffectsHint = "Music, typing reaction, system gauges, animations";
    readonly KeyboardService _kb = KeyboardService.Instance;

    public DevicePage()
    {
        InitializeComponent();
        ProductDetail.Text = $"Supported: {KeyboardService.Profile.Name}";
        Loaded += async (_, _) => await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        RefreshButton.IsEnabled = false;
        try
        {
            var (product, version) = await _kb.GetInfoAsync();
            var mode = await _kb.GetModeAsync();
            var custom = await _kb.GetCustomAsync();
            ProductName.Text = product;
            var hz = await Task.Run(UsbInfo.PollingHz);
            ProductDetail.Text = $"USB {KeyboardService.Profile.UsbId} · firmware {version >> 8}.{version & 0xFF:X2}"
                                 + (hz is int h ? $" · {h} Hz polling" : "");
            StatusDot.Fill = (Brush)FindResource("SystemFillColorSuccessBrush");
            StatusText.Text = "Connected";
            ModeText.Text = KeyboardService.Modes.GetValueOrDefault(mode.Mode, $"Mode 0x{mode.Mode:X2}")
                            + $" · brightness {mode.Brightness}/4";
            EffectText.Text = Effects.EffectEngine.Instance.Running is { } fx ? $"Running: {fx.Label}" : EffectsHint;
            Preview.SetColors(custom);
            Error.IsOpen = false;
        }
        catch (Exception e)
        {
            StatusDot.Fill = (Brush)FindResource("SystemFillColorCriticalBrush");
            StatusText.Text = "Not connected";
            Error.Message = e.Message;
            Error.IsOpen = true;
        }
        finally { RefreshButton.IsEnabled = true; }
    }

    void Refresh_Click(object sender, RoutedEventArgs e) => _ = RefreshAsync();
    void Lighting_Click(object sender, RoutedEventArgs e) => Navigate(typeof(LightingPage));
    void Effects_Click(object sender, RoutedEventArgs e) => Navigate(typeof(EffectsPage));
    void Remap_Click(object sender, RoutedEventArgs e) => Navigate(typeof(RemapPage));
    void Paint_Click(object sender, RoutedEventArgs e) => Navigate(typeof(PaintPage));

    static void Navigate(Type page) => (Application.Current.MainWindow as MainWindow)?.NavigateTo(page);
}
