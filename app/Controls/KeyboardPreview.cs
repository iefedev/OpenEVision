using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OpenEVision.Device;

namespace OpenEVision.Controls;

/// <summary>
/// Scalable drawing of the keyboard. Keys can be tinted (LED colors in custom order), outlined, captioned and
/// clicked/dragged over (for painting and remapping).
/// </summary>
public sealed class KeyboardPreview : Viewbox
{
    const double Unit = 40, Gap = 4;
    static readonly Brush OffBrush = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20));
    static readonly Brush EdgeBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));

    readonly Dictionary<string, (Border Box, TextBlock Label, TextBlock Caption)> _keys = new();

    public bool ShowLabels { get; init; } = true;
    public bool Interactive { get; init; }

    /// <summary>Mouse went down on a key.</summary>
    public event Action<string>? KeyPressed;
    /// <summary>Mouse moved onto a key with the left button held (drag painting).</summary>
    public event Action<string>? KeyDragOver;

    public KeyboardPreview()
    {
        Stretch = Stretch.Uniform;
        var canvas = new Canvas { Width = KeyLayout.TotalWidth * Unit, Height = (KeyLayout.Keys.Max(k => k.Y) + 1) * Unit };
        foreach (var k in KeyLayout.Keys)
        {
            var label = new TextBlock { Text = k.Label, FontSize = 11, Foreground = Brushes.White };
            var caption = new TextBlock { FontSize = 9, Foreground = Brushes.Gold, Visibility = Visibility.Collapsed, TextTrimming = TextTrimming.CharacterEllipsis };
            var stack = new StackPanel { Margin = new Thickness(5, 0, 3, 3), VerticalAlignment = VerticalAlignment.Bottom };
            stack.Children.Add(caption);
            stack.Children.Add(label);
            var b = new Border
            {
                Width = k.Width * Unit - Gap, Height = Unit - Gap, CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1), Background = OffBrush, BorderBrush = EdgeBrush, ToolTip = k.Code,
                Child = stack,
            };
            string code = k.Code;
            b.MouseLeftButtonDown += (_, e) => { if (Interactive) { KeyPressed?.Invoke(code); e.Handled = true; } };
            b.MouseEnter += (_, _) => { if (Interactive && Mouse.LeftButton == MouseButtonState.Pressed) KeyDragOver?.Invoke(code); };
            if (Interactive) b.Cursor = Cursors.Hand;
            Canvas.SetLeft(b, k.X * Unit);
            Canvas.SetTop(b, k.Y * Unit);
            canvas.Children.Add(b);
            _keys[code] = (b, label, caption);
        }
        Child = canvas;
        Loaded += (_, _) =>
        {
            foreach (var (b, label, _) in _keys.Values)
            {
                label.Visibility = ShowLabels ? Visibility.Visible : Visibility.Collapsed;
                if (Interactive) b.Cursor = Cursors.Hand;
            }
        };
    }

    /// <summary>colors: 3 bytes per LED in custom order (row * Columns + slot), as read with command 0x10.</summary>
    public void SetColors(byte[] colors)
    {
        foreach (var (code, (b, label, _)) in _keys)
        {
            if (!KeyLayout.CustomLed.TryGetValue(code, out int led) || led * 3 + 2 >= colors.Length) continue;
            var c = Color.FromRgb(colors[led * 3], colors[led * 3 + 1], colors[led * 3 + 2]);
            bool off = c.R + c.G + c.B < 24;
            b.Background = off ? OffBrush : new SolidColorBrush(c);
            label.Foreground = !off && 0.299 * c.R + 0.587 * c.G + 0.114 * c.B > 150 ? Brushes.Black : Brushes.White;
        }
    }

    /// <summary>Uniform look for all keys (remap view): no LED colors.</summary>
    public void ClearColors()
    {
        foreach (var (b, label, _) in _keys.Values) { b.Background = OffBrush; label.Foreground = Brushes.White; }
    }

    public void SetOutline(string code, Brush? brush, double thickness = 2)
    {
        if (!_keys.TryGetValue(code, out var k)) return;
        k.Box.BorderBrush = brush ?? EdgeBrush;
        k.Box.BorderThickness = new Thickness(brush == null ? 1 : thickness);
    }

    public void SetCaption(string code, string? text)
    {
        if (!_keys.TryGetValue(code, out var k)) return;
        k.Caption.Text = text ?? "";
        k.Caption.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    public void SetEnabled(string code, bool enabled)
    {
        if (_keys.TryGetValue(code, out var k)) k.Box.Opacity = enabled ? 1 : 0.35;
    }

    public IEnumerable<string> Codes => _keys.Keys;
}
