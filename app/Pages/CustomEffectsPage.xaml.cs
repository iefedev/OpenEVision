using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenEVision.Effects;
using OpenEVision.Effects.Custom;
using Microsoft.Win32;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using MenuItem = System.Windows.Controls.MenuItem;
using TextBlock = Wpf.Ui.Controls.TextBlock;

namespace OpenEVision.Pages;

public partial class CustomEffectsPage : Page
{
    readonly EffectEngine _engine = EffectEngine.Instance;

    public CustomEffectsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => { _engine.Changed += OnChanged; CustomEffectStore.Changed += OnChanged; Fill(); };
        Unloaded += (_, _) => { _engine.Changed -= OnChanged; CustomEffectStore.Changed -= OnChanged; };
    }

    void OnChanged() => Dispatcher.Invoke(Fill);

    public static string Summary(CustomEffectDef d) => d.Layers.Count == 0 ? "Empty" :
        string.Join(" · ", d.Layers.Select(l =>
            (LayerKinds.ByName.TryGetValue(l.Kind, out var k) ? k.Label : l.Kind)
            + (l.Blend == Blend.Normal ? "" : $" ({BlendLabel(l.Blend)})")
            + (l.Keys != null ? $" [{l.Keys.Count} keys]" : "")));

    public static string BlendLabel(Blend b) => b switch
    {
        Blend.Add => "Add", Blend.Multiply => "Multiply", Blend.Erase => "Erase", _ => "Normal",
    };

    void Fill()
    {
        List.Children.Clear();
        StopButton.IsEnabled = _engine.Running?.Name.StartsWith(EffectEngine.CustomPrefix) == true;
        foreach (var d in CustomEffectStore.All())
        {
            bool running = _engine.Running?.Name == EffectEngine.CustomPrefix + d.Id;
            var start = new Button { Content = running ? "Running" : "Start", Appearance = running ? ControlAppearance.Secondary : ControlAppearance.Primary, Margin = new Thickness(8, 0, 0, 0) };
            start.Icon = new SymbolIcon(running ? SymbolRegular.CheckmarkCircle24 : SymbolRegular.Play24);
            start.Click += async (_, _) => await StartAsync(d);
            var edit = new Button { Content = "Edit", Margin = new Thickness(8, 0, 0, 0), Icon = new SymbolIcon(SymbolRegular.Edit24) };
            edit.Click += (_, _) => CustomEditorPage.Open(d.Id);
            var more = new Button { Icon = new SymbolIcon(SymbolRegular.MoreHorizontal24), Margin = new Thickness(8, 0, 0, 0), ToolTip = "More" };
            more.Click += (_, _) => MoreMenu(d, more);

            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(start);
            actions.Children.Add(edit);
            actions.Children.Add(more);

            var header = new StackPanel();
            header.Children.Add(new TextBlock { Text = d.Name });
            header.Children.Add(new TextBlock
            {
                Text = Summary(d), FontTypography = FontTypography.Caption, TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = (Brush)FindResource("TextFillColorSecondaryBrush"),
            });
            List.Children.Add(new CardControl
            {
                Header = header, Content = actions, Margin = new Thickness(0, 0, 0, 4),
                Icon = new SymbolIcon(SymbolRegular.Layer24),
            });
        }
    }

    void MoreMenu(CustomEffectDef d, FrameworkElement anchor)
    {
        var menu = new ContextMenu();
        var copy = new MenuItem { Header = "Duplicate" };
        copy.Click += (_, _) =>
        {
            var c = d.Clone();
            c.Id = Guid.NewGuid().ToString("N")[..12];
            c.Name = d.Name + " (copy)";
            CustomEffectStore.Save(c);
        };
        var export = new MenuItem { Header = "Export…" };
        export.Click += (_, _) =>
        {
            var dlg = new SaveFileDialog { FileName = Safe(d.Name) + ".json", Filter = "Keyboard effect (*.json)|*.json" };
            if (dlg.ShowDialog() == true) CustomEffectStore.Export(d, dlg.FileName);
        };
        var delete = new MenuItem { Header = "Delete" };
        delete.Click += async (_, _) =>
        {
            var box = new Wpf.Ui.Controls.MessageBox
            {
                Title = "Delete effect", Content = $"Delete \"{d.Name}\"?", PrimaryButtonText = "Delete", CloseButtonText = "Cancel",
            };
            if (await box.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary) return;
            if (_engine.Running?.Name == EffectEngine.CustomPrefix + d.Id) await _engine.StopAsync();
            CustomEffectStore.Delete(d.Id);
        };
        menu.Items.Add(copy);
        menu.Items.Add(export);
        menu.Items.Add(new Separator());
        menu.Items.Add(delete);
        menu.PlacementTarget = anchor;
        menu.IsOpen = true;
    }

    static string Safe(string name) => string.Concat(name.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    async Task StartAsync(CustomEffectDef d)
    {
        try
        {
            await _engine.StartCustomAsync(d, EffectSettings.Brightness);
            Error.IsOpen = false;
        }
        catch (Exception e)
        {
            Error.Message = e.Message;
            Error.IsOpen = true;
        }
    }

    void New_Click(object sender, RoutedEventArgs e)
    {
        var d = new CustomEffectDef { Name = "New effect", Layers = [new LayerDef { Kind = "plasma" }] };
        CustomEffectStore.Save(d);
        CustomEditorPage.Open(d.Id);
    }

    void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Keyboard effect (*.json)|*.json" };
        if (dlg.ShowDialog() != true) return;
        try { CustomEffectStore.Import(dlg.FileName); Error.IsOpen = false; }
        catch (Exception ex)
        {
            Error.Message = "Import failed: " + ex.Message;
            Error.IsOpen = true;
        }
    }

    async void Stop_Click(object sender, RoutedEventArgs e) => await _engine.StopAsync();
}
