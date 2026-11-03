using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using OpenEVision.Controls;
using OpenEVision.Device;
using OpenEVision.Effects;
using OpenEVision.Effects.Custom;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using TextBlock = Wpf.Ui.Controls.TextBlock;

namespace OpenEVision.Pages;

/// <summary>Layer-stack editor for one custom effect. Every change is saved automatically and previewed live.</summary>
public partial class CustomEditorPage : Page
{
    sealed record AddItem(string Label, LayerKind? Kind);

    static string? _targetId;

    public static void Open(string id)
    {
        _targetId = id;
        (Application.Current.MainWindow as MainWindow)?.NavigateTo(typeof(CustomEditorPage));
    }

    static readonly (string Name, List<string> Keys)[] KeyPresets =
    [
        ("WASD", ["KeyW", "KeyA", "KeyS", "KeyD"]),
        ("F row", ["Escape", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12"]),
        ("Number row", ["Backquote", "Digit1", "Digit2", "Digit3", "Digit4", "Digit5", "Digit6", "Digit7", "Digit8", "Digit9", "Digit0", "Minus", "Equal"]),
        ("Letters", KeyLayout.Keys.Select(k => k.Code).Where(c => c.StartsWith("Key")).ToList()),
        ("Numpad", KeyLayout.Keys.Select(k => k.Code).Where(c => c.StartsWith("Numpad") || c == "NumLock").ToList()),
        ("Arrow keys", ["ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight"]),
    ];

    static readonly (ModSource Source, string Label)[] ModSources =
    [
        (ModSource.None, "None"), (ModSource.Audio, "Music volume"), (ModSource.Bass, "Bass (music)"),
        (ModSource.Cpu, "CPU load"), (ModSource.GpuLoad, "GPU load (NVIDIA only)"), (ModSource.GpuTemp, "GPU temperature (NVIDIA only)"),
    ];

    readonly EffectEngine _engine = EffectEngine.Instance;
    readonly DispatcherTimer _frame = new() { Interval = TimeSpan.FromMilliseconds(33) };
    readonly DispatcherTimer _rebuild = new() { Interval = TimeSpan.FromMilliseconds(150) };
    readonly DispatcherTimer _save = new() { Interval = TimeSpan.FromMilliseconds(400) };
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly float[] _rgb = new float[Ctx.N * 3];
    readonly byte[] _bytes = new byte[Ctx.N * 3];
    readonly Ctx _ctx = new();

    CustomEffectDef _def = new();
    int _selected = -1;
    CustomEffect? _preview;
    double _last;
    bool _loading = true;
    bool _devicePreview;

    public CustomEditorPage()
    {
        InitializeComponent();
        var add = new List<AddItem> { new("Add layer…", null) };
        add.AddRange(LayerKinds.All.Select(k => new AddItem($"{k.Group}: {k.Label}", k)));
        AddBox.ItemsSource = add;
        AddBox.SelectedIndex = 0;

        _frame.Tick += (_, _) => RenderFrame();
        _rebuild.Tick += (_, _) => { _rebuild.Stop(); RebuildPreview(); };
        _save.Tick += async (_, _) => { _save.Stop(); await SaveAsync(); };
        Board.KeyPressed += ToggleKey;
        Board.KeyDragOver += ToggleKey;
        Loaded += (_, _) => Load();
        Unloaded += async (_, _) => await LeaveAsync();
    }

    void Load()
    {
        _def = (_targetId != null ? CustomEffectStore.Find(_targetId) : null) ?? new CustomEffectDef();
        NameBox.Text = _def.Name;
        _selected = _def.Layers.Count - 1;
        _loading = false;
        RenderLayers();
        RenderProps();
        RebuildPreview();
        _frame.Start();
    }

    async Task LeaveAsync()
    {
        _frame.Stop();
        _preview?.Dispose();
        _preview = null;
        if (_save.IsEnabled) { _save.Stop(); await SaveAsync(); }
        if (_devicePreview && _engine.Running?.Name == EffectEngine.CustomPrefix + _def.Id) await _engine.StopAsync();
    }

    // ---- change handling ---------------------------------------------------------------------

    void Changed(bool layersChanged = false)
    {
        if (_loading) return;
        if (layersChanged) RenderLayers();
        _rebuild.Stop(); _rebuild.Start();
        _save.Stop(); _save.Start();
    }

    async Task SaveAsync()
    {
        CustomEffectStore.Save(_def);
        if (_devicePreview) await RunOnDeviceAsync();
    }

    void RebuildPreview()
    {
        _preview?.Dispose();
        _preview = null;
        try
        {
            _preview = new CustomEffect(_ctx, _def.Clone());
            Error.IsOpen = false;
        }
        catch (Exception e)
        {
            Error.Message = e.Message;
            Error.IsOpen = true;
        }
        HighlightKeys();
    }

    void RenderFrame()
    {
        if (_preview == null) return;
        double now = _clock.Elapsed.TotalSeconds;
        try { _preview.Render(now, now - _last, _rgb); }
        catch (Exception e) { Error.Message = e.Message; Error.IsOpen = true; _preview = null; return; }
        _last = now;
        for (int i = 0; i < _bytes.Length; i++) _bytes[i] = (byte)Math.Clamp(_rgb[i] * 255, 0, 255);
        Board.SetColors(_bytes);
    }

    async Task RunOnDeviceAsync()
    {
        try
        {
            await _engine.StartCustomAsync(_def, EffectSettings.Brightness);
            Error.IsOpen = false;
        }
        catch (Exception e)
        {
            Error.Message = e.Message;
            Error.IsOpen = true;
        }
    }

    // ---- header ------------------------------------------------------------------------------

    void Name_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        _def.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? "Untitled effect" : NameBox.Text.Trim();
        Changed();
    }

    async void Device_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _devicePreview = DeviceSwitch.IsChecked == true;
        if (_devicePreview) await RunOnDeviceAsync();
        else if (_engine.Running?.Name == EffectEngine.CustomPrefix + _def.Id) await _engine.StopAsync();
    }

    async void Start_Click(object sender, RoutedEventArgs e)
    {
        CustomEffectStore.Save(_def);
        await RunOnDeviceAsync();
        // It keeps running after leaving the editor (unlike the preview toggle).
        _devicePreview = false;
        _loading = true;
        DeviceSwitch.IsChecked = false;
        _loading = false;
    }

    void Back_Click(object sender, RoutedEventArgs e) =>
        (Application.Current.MainWindow as MainWindow)?.NavigateTo(typeof(CustomEffectsPage));

    // ---- layers list ---------------------------------------------------------------------------

    void Add_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (AddBox.SelectedItem is not AddItem { Kind: { } kind }) return;
        AddBox.SelectedIndex = 0;
        _def.Layers.Add(new LayerDef { Kind = kind.Name });
        _selected = _def.Layers.Count - 1;
        Changed(layersChanged: true);
        RenderProps();
    }

    void RenderLayers()
    {
        LayerList.Children.Clear();
        if (_def.Layers.Count == 0)
        {
            LayerList.Children.Add(new TextBlock { Text = "No layers yet. Add one above.", Margin = new Thickness(4, 8, 0, 8) });
            return;
        }
        for (int i = _def.Layers.Count - 1; i >= 0; i--) LayerList.Children.Add(LayerRow(i));
    }

    UIElement LayerRow(int i)
    {
        var l = _def.Layers[i];
        var kind = LayerKinds.ByName.GetValueOrDefault(l.Kind);
        int index = i;

        var enabled = new CheckBox { IsChecked = l.Enabled, VerticalAlignment = VerticalAlignment.Center, MinWidth = 0, ToolTip = "On/off" };
        enabled.Click += (_, _) => { l.Enabled = enabled.IsChecked == true; Changed(layersChanged: true); };

        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
        titles.Children.Add(new TextBlock { Text = kind?.Label ?? l.Kind, Opacity = l.Enabled ? 1 : 0.5 });
        var bits = new List<string> { CustomEffectsPage.BlendLabel(l.Blend) };
        if (l.Opacity < 0.999) bits.Add($"{l.Opacity * 100:0}%");
        bits.Add(l.Keys == null ? "whole keyboard" : $"{l.Keys.Count} keys");
        if (l.Mod != ModSource.None) bits.Add(ModSources.First(m => m.Source == l.Mod).Label);
        titles.Children.Add(new TextBlock
        {
            Text = string.Join(" · ", bits), FontTypography = FontTypography.Caption,
            Foreground = (Brush)FindResource("TextFillColorSecondaryBrush"),
        });

        Button Icon(SymbolRegular s, string tip, Action a, bool enabledBtn = true)
        {
            var b = new Button { Icon = new SymbolIcon(s), ToolTip = tip, Padding = new Thickness(6), Margin = new Thickness(2, 0, 0, 0), IsEnabled = enabledBtn };
            b.Click += (_, e) => { e.Handled = true; a(); };
            return b;
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(Icon(SymbolRegular.ArrowUp24, "Move up", () => Move(index, +1), index < _def.Layers.Count - 1));
        buttons.Children.Add(Icon(SymbolRegular.ArrowDown24, "Move down", () => Move(index, -1), index > 0));
        buttons.Children.Add(Icon(SymbolRegular.Delete24, "Delete", () => Remove(index)));

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(enabled);
        Grid.SetColumn(titles, 1);
        grid.Children.Add(titles);
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);

        var row = new Border
        {
            Child = grid, Padding = new Thickness(8, 6, 6, 6), Margin = new Thickness(0, 0, 0, 4), CornerRadius = new CornerRadius(6),
            Background = (Brush)FindResource(index == _selected ? "SubtleFillColorSecondaryBrush" : "SubtleFillColorTransparentBrush"),
            BorderBrush = index == _selected ? (Brush)FindResource("AccentFillColorDefaultBrush") : Brushes.Transparent,
            BorderThickness = new Thickness(index == _selected ? 1 : 0), Cursor = System.Windows.Input.Cursors.Hand,
        };
        row.MouseLeftButtonUp += (_, _) => { _selected = index; RenderLayers(); RenderProps(); HighlightKeys(); };
        return row;
    }

    void Move(int i, int dir)
    {
        int j = i + dir;
        if (j < 0 || j >= _def.Layers.Count) return;
        (_def.Layers[i], _def.Layers[j]) = (_def.Layers[j], _def.Layers[i]);
        _selected = j;
        Changed(layersChanged: true);
        RenderProps();
    }

    void Remove(int i)
    {
        _def.Layers.RemoveAt(i);
        _selected = Math.Min(_selected, _def.Layers.Count - 1);
        Changed(layersChanged: true);
        RenderProps();
    }

    // ---- properties --------------------------------------------------------------------------------

    LayerDef? Selected => _selected >= 0 && _selected < _def.Layers.Count ? _def.Layers[_selected] : null;

    void RenderProps()
    {
        Props.Children.Clear();
        var l = Selected;
        if (l == null || !LayerKinds.ByName.TryGetValue(l.Kind, out var kind))
        {
            Props.Children.Add(new TextBlock { Text = "Select a layer on the left to see its settings." });
            return;
        }

        Props.Children.Add(new TextBlock { Text = kind.Label, FontTypography = FontTypography.Subtitle });
        Props.Children.Add(new TextBlock
        {
            Text = kind.Description, Margin = new Thickness(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("TextFillColorSecondaryBrush"),
        });

        // Blend + opacity
        var blend = new ComboBox { MinWidth = 180, DisplayMemberPath = "Text", SelectedValuePath = "Value" };
        blend.ItemsSource = new[]
        {
            new { Value = Blend.Normal, Text = "Normal (draw over)" }, new { Value = Blend.Add, Text = "Add (brighten)" },
            new { Value = Blend.Multiply, Text = "Multiply (tint)" }, new { Value = Blend.Erase, Text = "Erase (darken below)" },
        };
        blend.SelectedValue = l.Blend;
        blend.SelectionChanged += (_, _) => { if (blend.SelectedValue is Blend b) { l.Blend = b; Changed(layersChanged: true); } };
        Props.Children.Add(ParamEditor.Labelled("Blend", blend));
        Props.Children.Add(ParamEditor.Row(new ParamDef("opacity", "Opacity", ParamKind.Range, 1.0, 0, 1, 0.05), l.Opacity,
            v => { l.Opacity = Convert.ToDouble(v); Changed(layersChanged: true); }));

        // Layer-specific settings
        if (kind.Params.Length > 0) Section("Settings");
        var typed = LayerKinds.Typed(kind, l.Params);
        foreach (var p in kind.Params)
            Props.Children.Add(ParamEditor.Row(p, typed[p.Key], v => { l.Params[p.Key] = LayerKinds.ToJson(v); Changed(); }));

        // Keys
        Section("Keys");
        var all = new ToggleSwitch { Content = "Whole keyboard", IsChecked = l.Keys == null, Margin = new Thickness(0, 0, 0, 8) };
        all.Click += (_, _) =>
        {
            l.Keys = all.IsChecked == true ? null : new List<string>();
            Changed(layersChanged: true);
            RenderProps();
        };
        Props.Children.Add(all);
        if (l.Keys != null)
        {
            var presets = new WrapPanel();
            foreach (var (name, keys) in KeyPresets)
            {
                var b = new Button { Content = "+ " + name, Margin = new Thickness(0, 0, 6, 6) };
                b.Click += (_, _) => { foreach (var k in keys) if (!l.Keys.Contains(k)) l.Keys.Add(k); Changed(layersChanged: true); HighlightKeys(); };
                presets.Children.Add(b);
            }
            var clear = new Button { Content = "Clear", Margin = new Thickness(0, 0, 6, 6) };
            clear.Click += (_, _) => { l.Keys.Clear(); Changed(layersChanged: true); HighlightKeys(); };
            presets.Children.Add(clear);
            Props.Children.Add(presets);
            Props.Children.Add(new TextBlock
            {
                Text = "Click (or drag over) keys on the keyboard above to add or remove them. Selected keys have an accent outline.",
                FontTypography = FontTypography.Caption, TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("TextFillColorSecondaryBrush"),
            });
        }

        // Modulation
        Section("Live control");
        var mod = new ComboBox { MinWidth = 180, DisplayMemberPath = "Label", SelectedValuePath = "Source" };
        mod.ItemsSource = ModSources.Select(m => new { m.Source, m.Label }).ToList();
        mod.SelectedValue = l.Mod;
        mod.SelectionChanged += (_, _) => { if (mod.SelectedValue is ModSource s) { l.Mod = s; Changed(layersChanged: true); RenderProps(); } };
        Props.Children.Add(ParamEditor.Labelled("Opacity driven by", mod));
        if (l.Mod != ModSource.None)
            Props.Children.Add(ParamEditor.Row(new ParamDef("modAmount", "Amount", ParamKind.Range, 1.0, 0, 1, 0.05), l.ModAmount,
                v => { l.ModAmount = Convert.ToDouble(v); Changed(); }));
    }

    void Section(string title) => Props.Children.Add(new TextBlock { Text = title, FontTypography = FontTypography.BodyStrong, Margin = new Thickness(0, 16, 0, 4) });

    // ---- key selection -------------------------------------------------------------------------

    string? _dragMode; // "add" / "remove" for the current drag

    void ToggleKey(string code)
    {
        var l = Selected;
        if (l?.Keys == null) return;
        bool pressed = System.Windows.Input.Mouse.LeftButton == System.Windows.Input.MouseButtonState.Pressed;
        if (_dragMode == null || !pressed) _dragMode = l.Keys.Contains(code) ? "remove" : "add";
        if (_dragMode == "add" && !l.Keys.Contains(code)) l.Keys.Add(code);
        else if (_dragMode == "remove") l.Keys.Remove(code);
        Changed(layersChanged: true);
        HighlightKeys();
    }

    void HighlightKeys()
    {
        var keys = Selected?.Keys;
        var accent = (Brush)FindResource("AccentFillColorDefaultBrush");
        foreach (var code in Board.Codes) Board.SetOutline(code, keys != null && keys.Contains(code) ? accent : null, 2.5);
    }
}
