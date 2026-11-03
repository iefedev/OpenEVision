using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using OpenEVision.Device;
using OpenEVision.Effects;

namespace OpenEVision.Pages;

public partial class SettingsPage : Page
{
    bool _loading = true;

    public SettingsPage()
    {
        InitializeComponent();
        AutostartSwitch.IsChecked = Autostart.Enabled;
        ResumeSwitch.IsChecked = EffectSettings.ResumeOnStart;
        TraySwitch.IsChecked = App.CloseToTray;
        var theme = Storage.Get("theme", "system");
        ThemeBox.SelectedItem = ThemeBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == theme) ?? ThemeBox.Items[0];
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        AboutTitle.Text = $"OpenEVision {version?.ToString(3)}";
        AboutText.Text = $"Unofficial control app for {KeyboardService.Profile.Name} keyboards. Not affiliated with any keyboard vendor. "
                         + "Licensed under GPL-2.0-only.\n"
                         + $"Data folder: {Storage.DisplayRoot}";
        _loading = false;
    }

    void Autostart_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loading) Autostart.Enabled = AutostartSwitch.IsChecked == true;
    }

    void Resume_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loading) EffectSettings.ResumeOnStart = ResumeSwitch.IsChecked == true;
    }

    void Tray_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loading) App.CloseToTray = TraySwitch.IsChecked == true;
    }

    void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeBox.SelectedItem is not ComboBoxItem { Tag: string theme }) return;
        Storage.Set("theme", theme);
        App.ApplyTheme(theme);
    }
}
