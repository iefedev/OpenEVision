using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenEVision.Effects;
using Wpf.Ui.Controls;
using TextBlock = Wpf.Ui.Controls.TextBlock;

namespace OpenEVision.Controls;

/// <summary>Builds a labelled input row for an effect/layer parameter.</summary>
public static class ParamEditor
{
    public static UIElement Row(ParamDef p, object value, Action<object> set)
    {
        FrameworkElement input;
        switch (p.Kind)
        {
            case ParamKind.Range:
                var slider = new Slider { Width = 240, Minimum = p.Min, Maximum = p.Max, SmallChange = p.Step, Value = Convert.ToDouble(value) };
                var readout = new TextBlock { Width = 44, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                void Show() => readout.Text = slider.Value.ToString(p.Step < 1 ? "0.##" : "0", CultureInfo.InvariantCulture);
                Show();
                slider.ValueChanged += (_, e) => { Show(); set(e.NewValue); };
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(slider);
                sp.Children.Add(readout);
                input = sp;
                break;
            case ParamKind.Check:
                var sw = new ToggleSwitch { IsChecked = Convert.ToBoolean(value) };
                sw.Checked += (_, _) => set(true);
                sw.Unchecked += (_, _) => set(false);
                input = sw;
                break;
            case ParamKind.Select:
                var box = new ComboBox { MinWidth = 180, DisplayMemberPath = "Text", SelectedValuePath = "Value" };
                box.ItemsSource = p.Options!.Select(o => new { o.Value, o.Text }).ToList();
                box.SelectedValue = value;
                box.SelectionChanged += (_, _) => { if (box.SelectedValue is string v) set(v); };
                input = box;
                break;
            default:
                input = ColorInput(Convert.ToString(value)!, set);
                break;
        }
        return Labelled(p.Label, input);
    }

    public static Grid Labelled(string label, FrameworkElement input)
    {
        var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(input, 1);
        row.Children.Add(input);
        return row;
    }

    static readonly string[] Quick = ["ff0040", "ff8000", "ffff00", "00ff40", "00c0ff", "4040ff", "c000ff", "ffffff"];

    public static FrameworkElement ColorInput(string hex, Action<object> set)
    {
        var swatch = new Border { Width = 28, Height = 20, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 8, 0), Background = Brush(hex) };
        var text = new Wpf.Ui.Controls.TextBox { Width = 100, Text = "#" + hex.ToUpperInvariant() };
        void Apply(string s)
        {
            swatch.Background = Brush(s);
            text.Text = "#" + s.ToUpperInvariant();
            set(s.ToLowerInvariant());
        }
        void Commit()
        {
            var s = text.Text.Trim().TrimStart('#');
            if (s.Length == 6 && int.TryParse(s, NumberStyles.HexNumber, null, out _)) Apply(s);
        }
        text.LostFocus += (_, _) => Commit();
        text.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) Commit(); };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var q in Quick)
        {
            var b = new System.Windows.Controls.Button
            {
                Width = 18, Height = 18, Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(0), Background = Brush(q),
                BorderThickness = new Thickness(0), ToolTip = "#" + q.ToUpperInvariant(),
            };
            b.Click += (_, _) => Apply(q);
            panel.Children.Add(b);
        }
        panel.Children.Add(new Border { Width = 8 });
        panel.Children.Add(swatch);
        panel.Children.Add(text);
        return panel;
    }

    public static SolidColorBrush Brush(string hex)
    {
        var (r, g, b) = Effect.Hex(hex);
        return new SolidColorBrush(Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255)));
    }
}
