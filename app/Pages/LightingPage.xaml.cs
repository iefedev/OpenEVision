using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using OpenEVision.Device;

namespace OpenEVision.Pages;

/// <summary>Hardware lighting modes. Changes are written to the keyboard shortly after the last edit.</summary>
public partial class LightingPage : Page
{
    static readonly string[] PaletteColors =
        ["FF0040", "FF8000", "FFFF00", "00FF40", "00C0FF", "4040FF", "C000FF", "FF00A0", "FFFFFF"];

    readonly KeyboardService _kb = KeyboardService.Instance;
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(350) };
    bool _loading = true;
    Color _color = Colors.Red;

    public LightingPage()
    {
        InitializeComponent();
        ModeBox.ItemsSource = KeyboardService.Modes.ToList();
        foreach (var hex in PaletteColors)
        {
            var c = Parse(hex)!.Value;
            var b = new Button
            {
                Width = 32, Height = 32, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(0),
                Background = new SolidColorBrush(c), ToolTip = "#" + hex,
            };
            b.Click += (_, _) => SetColor(c, apply: true);
            Palette.Children.Add(b);
        }
        _debounce.Tick += async (_, _) => { _debounce.Stop(); await ApplyAsync(); };
        Loaded += async (_, _) => await LoadAsync();
    }

    async Task LoadAsync()
    {
        _loading = true;
        try
        {
            var m = await _kb.GetModeAsync();
            ModeBox.SelectedValue = m.Mode;
            BrightnessSlider.Value = m.Brightness;
            SpeedSlider.Value = 5 - m.Speed; // device: 0 = fastest
            DirectionBox.SelectedIndex = Math.Min((int)m.Direction, 3);
            RandomSwitch.IsChecked = m.Random;
            SetColor(Color.FromRgb(m.R, m.G, m.B), apply: false);
            Error.IsOpen = false;
        }
        catch (Exception e) { ShowError(e); }
        finally
        {
            _loading = false;
            UpdateTexts();
        }
    }

    void Changed(object sender, RoutedEventArgs e)
    {
        UpdateTexts();
        if (_loading || !IsLoaded) return;
        _debounce.Stop();
        _debounce.Start();
    }

    void UpdateTexts()
    {
        if (BrightnessText != null) BrightnessText.Text = $"{(int)BrightnessSlider.Value}/4";
    }

    async Task ApplyAsync()
    {
        if (ModeBox.SelectedValue is not byte mode) return;
        var m = new HardwareMode(mode, (byte)BrightnessSlider.Value, (byte)(5 - SpeedSlider.Value),
            (byte)Math.Max(0, DirectionBox.SelectedIndex), RandomSwitch.IsChecked == true, _color.R, _color.G, _color.B);
        try
        {
            // A running software effect draws over the hardware mode: stop it so the change shows.
            await Effects.EffectEngine.Instance.StopAsync();
            await _kb.SetModeAsync(m);
            var back = await _kb.GetModeAsync();
            SavedText.Text = back == m ? $"Saved to the keyboard · {DateTime.Now:HH:mm:ss}" : "Saved, but the value read back is different.";
            Error.IsOpen = false;
        }
        catch (Exception e) { ShowError(e); }
    }

    void SetColor(Color c, bool apply)
    {
        _color = c;
        ColorSwatch.Background = new SolidColorBrush(c);
        HexBox.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        if (apply) Changed(this, new RoutedEventArgs());
    }

    void Hex_LostFocus(object sender, RoutedEventArgs e)
    {
        if (Parse(HexBox.Text) is { } c && c != _color) SetColor(c, apply: true);
        else HexBox.Text = $"#{_color.R:X2}{_color.G:X2}{_color.B:X2}";
    }

    void Hex_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Hex_LostFocus(sender, e);
    }

    static Color? Parse(string s)
    {
        s = s.Trim().TrimStart('#');
        return s.Length == 6 && uint.TryParse(s, NumberStyles.HexNumber, null, out var v)
            ? Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v) : null;
    }

    void ShowError(Exception e)
    {
        Error.Message = e.Message;
        Error.IsOpen = true;
    }
}
