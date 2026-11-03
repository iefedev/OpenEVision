using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using OpenEVision.Device;

namespace OpenEVision.Pages;

/// <summary>
/// Key remapping on the keyboard's active key table. Physical key positions and defaults come from the factory dump
/// of this keyboard, so a remapped key is still found at its physical place.
/// </summary>
public partial class RemapPage : Page
{
    readonly KeyboardService _kb = KeyboardService.Instance;
    static readonly Brush Changed = Brushes.Gold;

    byte[] _factory = [];
    byte[] _current = [];
    Dictionary<string, int> _positions = new(); // physical key -> table index (from the factory table)
    string? _selected;

    public RemapPage()
    {
        InitializeComponent();
        var view = new ListCollectionView(KeyActions.All.ToList());
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(KeyAction.Group)));
        ActionBox.ItemsSource = view;
        ActionBox.GroupStyle.Add(GroupStyle.Default);
        Board.KeyPressed += Select;
        Loaded += async (_, _) => await LoadAsync();
    }

    async Task LoadAsync()
    {
        try
        {
            await _kb.EnsureFactoryAsync();
            var cfg = Storage.FactoryConfig() ?? throw new InvalidOperationException("The factory dump is missing.");
            _factory = cfg.AsSpan(KeyboardService.Profile.KeyTableOffset, KeyboardService.KeyTableSize).ToArray();
            _positions = LiveMap.Decode(_factory);
            _current = await _kb.GetKeyTableAsync();
            Render();
            Error.IsOpen = false;
        }
        catch (Exception e) { ShowError(e); }
    }

    string? Factory(string code) => _positions.TryGetValue(code, out int i) ? KeyActions.FromBytes(_factory.AsSpan(i * 3, 3)) : null;
    string? Current(string code) => _positions.TryGetValue(code, out int i) ? KeyActions.FromBytes(_current.AsSpan(i * 3, 3)) : null;

    void Render()
    {
        Board.ClearColors();
        int changed = 0;
        foreach (var code in Board.Codes)
        {
            var f = Factory(code);
            bool remapped = f != null && Current(code) != f;
            if (remapped) changed++;
            Board.SetEnabled(code, f != null);
            Board.SetOutline(code, code == _selected ? (Brush)FindResource("AccentFillColorDefaultBrush") : remapped ? Changed : null);
            Board.SetCaption(code, remapped ? "→ " + ShortLabel(Current(code)) : null);
        }
        ResetDetail.Text = changed == 0 ? "All keys have their factory assignment" : $"{changed} key(s) remapped";
        if (_selected != null)
        {
            SelTitle.Text = $"{_selected}: {KeyActions.Label(Current(_selected))}";
            SelDetail.Text = $"Default: {KeyActions.Label(Factory(_selected))}";
            ActionBox.SelectedValue = Current(_selected);
        }
    }

    static string ShortLabel(string? id)
    {
        var l = KeyActions.Label(id);
        int p = l.IndexOf(" (", StringComparison.Ordinal);
        return p > 0 ? l[..p] : l;
    }

    void Select(string code)
    {
        if (Factory(code) == null) return; // Fn and keys the table doesn't model
        _selected = code;
        ActionBox.IsEnabled = ApplyButton.IsEnabled = DefaultButton.IsEnabled = true;
        Render();
    }

    async Task WriteAsync(string code, byte[] entry)
    {
        try
        {
            await _kb.SetKeysAsync(new Dictionary<int, byte[]> { [_positions[code]] = entry });
            _current = await _kb.GetKeyTableAsync();
            Render();
            Error.IsOpen = false;
        }
        catch (Exception e) { ShowError(e); }
    }

    async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_selected != null && ActionBox.SelectedValue is string id) await WriteAsync(_selected, KeyActions.ToBytes(id));
    }

    async void Default_Click(object sender, RoutedEventArgs e)
    {
        if (_selected != null) await WriteAsync(_selected, _factory.AsSpan(_positions[_selected] * 3, 3).ToArray());
    }

    async void Reset_Click(object sender, RoutedEventArgs e)
    {
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = "Factory key layout", Content = "Restore every key assignment to its factory state?",
            PrimaryButtonText = "Reset", CloseButtonText = "Cancel",
        };
        if (await box.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary) return;
        var diff = new Dictionary<int, byte[]>();
        for (int i = 0; i < KeyboardService.Profile.KeyTableEntries; i++)
            if (!_factory.AsSpan(i * 3, 3).SequenceEqual(_current.AsSpan(i * 3, 3))) diff[i] = _factory.AsSpan(i * 3, 3).ToArray();
        try
        {
            if (diff.Count > 0) await _kb.SetKeysAsync(diff);
            _current = await _kb.GetKeyTableAsync();
            Render();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    void ShowError(Exception e)
    {
        Error.Message = e.Message;
        Error.IsOpen = true;
    }
}
