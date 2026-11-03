using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using OpenEVision.Effects;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using TextBlock = Wpf.Ui.Controls.TextBlock;

namespace OpenEVision.Pages;

public partial class EffectsPage : Page
{
    static readonly Dictionary<string, SymbolRegular> Icons = new()
    {
        ["typing"] = SymbolRegular.KeyboardLayoutFloat24, ["audio"] = SymbolRegular.MusicNote224,
        ["system"] = SymbolRegular.DeveloperBoard24, ["rainbow"] = SymbolRegular.Color24,
        ["plasma"] = SymbolRegular.Blur24, ["fire"] = SymbolRegular.Fire24, ["matrix"] = SymbolRegular.Code24,
    };

    readonly EffectEngine _engine = EffectEngine.Instance;
    readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(1) };

    public EffectsPage()
    {
        InitializeComponent();
        foreach (var info in EffectEngine.All) EffectList.Children.Add(BuildCard(info));
        BrightnessSlider.Value = _engine.Running != null ? _engine.Brightness : EffectSettings.Brightness;
        _poll.Tick += (_, _) => ShowStatus();
        Loaded += (_, _) => { _engine.Changed += OnChanged; _poll.Start(); ShowStatus(); };
        Unloaded += (_, _) => { _engine.Changed -= OnChanged; _poll.Stop(); };
    }

    void OnChanged() => Dispatcher.Invoke(ShowStatus);

    UIElement BuildCard(EffectInfo info)
    {
        var start = new Button { Content = "Start", Appearance = ControlAppearance.Primary, Margin = new Thickness(0, 0, 16, 0) };
        start.Icon = new SymbolIcon(SymbolRegular.Play24);
        start.Click += async (_, e) => { e.Handled = true; await StartAsync(info); };

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titles = new StackPanel();
        titles.Children.Add(new TextBlock { Text = info.Label });
        titles.Children.Add(new TextBlock
        {
            Text = info.Description, FontTypography = FontTypography.Caption,
            Foreground = (Brush)FindResource("TextFillColorSecondaryBrush"),
        });
        header.Children.Add(titles);
        Grid.SetColumn(start, 1);
        header.Children.Add(start);

        var body = new StackPanel();
        foreach (var p in info.Params) body.Children.Add(BuildParam(info, p));

        return new CardExpander
        {
            Header = header, Content = body, Margin = new Thickness(0, 0, 0, 4),
            Icon = new SymbolIcon(Icons.GetValueOrDefault(info.Name, SymbolRegular.Sparkle24)),
        };
    }

    UIElement BuildParam(EffectInfo info, ParamDef p)
    {
        var values = EffectSettings.ParamsFor(info);
        void Set(object v)
        {
            values[p.Key] = v;
            if (_engine.Running?.Name == info.Name) _engine.Update(p.Key, v);
            EffectSettings.Changed();
        }
        return Controls.ParamEditor.Row(p, values[p.Key], Set);
    }

    async Task StartAsync(EffectInfo info)
    {
        try
        {
            await _engine.StartAsync(info, EffectSettings.ParamsFor(info), BrightnessSlider.Value);
            Error.IsOpen = false;
        }
        catch (Exception e)
        {
            Error.Message = e.Message;
            Error.IsOpen = true;
        }
        ShowStatus();
    }

    async void Stop_Click(object sender, RoutedEventArgs e) => await _engine.StopAsync();

    void Brightness_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _engine.Brightness = e.NewValue;
        if (IsLoaded) EffectSettings.Brightness = e.NewValue;
    }

    void ShowStatus()
    {
        var r = _engine.Running;
        StopButton.IsEnabled = r != null;
        StatusIcon.Symbol = r != null ? SymbolRegular.Play24 : SymbolRegular.Pause24;
        StatusTitle.Text = r != null ? $"Running: {r.Label}" : "No effect running";
        StatusDetail.Text = r == null ? "The keyboard is in its own lighting mode"
            : _engine.Error != null ? $"Cannot send to the keyboard: {_engine.Error}"
            : $"{_engine.FrameRate:0} FPS";
    }
}
