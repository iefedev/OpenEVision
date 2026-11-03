using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenEVision.Device;
using OpenEVision.Effects;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using TextBlock = Wpf.Ui.Controls.TextBlock;

namespace OpenEVision.Pages;

public partial class BackupsPage : Page
{
    readonly KeyboardService _kb = KeyboardService.Instance;

    public BackupsPage()
    {
        InitializeComponent();
        FolderText.Text = Path.Combine(Storage.DisplayRoot, "backups");
        Loaded += async (_, _) =>
        {
            try { await _kb.EnsureFactoryAsync(); } catch (Exception e) { ShowError(e); }
            Fill();
        };
    }

    static string DisplayName(BackupEntry b) => string.IsNullOrEmpty(b.Label) ? b.Name : b.Label;

    void Fill()
    {
        List.Children.Clear();
        foreach (var b in Backups.List())
        {
            var restore = new Button { Content = "Restore", Margin = new Thickness(8, 0, 0, 0) };
            restore.Click += async (_, _) => await RestoreAsync(b);
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            if (b.Deletable)
            {
                var del = new Button { Icon = new SymbolIcon(SymbolRegular.Delete24), ToolTip = "Delete" };
                del.Click += async (_, _) => await DeleteAsync(b);
                actions.Children.Add(del);
            }
            actions.Children.Add(restore);

            var header = new StackPanel();
            header.Children.Add(new TextBlock { Text = DisplayName(b) });
            header.Children.Add(new TextBlock
            {
                Text = $"{b.Created} · {b.Source}", FontTypography = FontTypography.Caption,
                Foreground = (Brush)FindResource("TextFillColorSecondaryBrush"),
            });
            List.Children.Add(new CardControl
            {
                Header = header, Content = actions, Margin = new Thickness(0, 0, 0, 4),
                Icon = new SymbolIcon(b.Path == Backups.FactoryPath ? SymbolRegular.Shield24 : SymbolRegular.Archive24),
            });
        }
    }

    async void Create_Click(object sender, RoutedEventArgs e)
    {
        CreateButton.IsEnabled = false;
        try
        {
            var b = await Backups.CreateAsync(_kb, LabelBox.Text.Trim());
            LabelBox.Text = "";
            Show($"Backup saved: {b.Name}");
            Fill();
        }
        catch (Exception ex) { ShowError(ex); }
        finally { CreateButton.IsEnabled = true; }
    }

    async Task RestoreAsync(BackupEntry b)
    {
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = "Restore backup",
            Content = $"Write \"{DisplayName(b)}\" to the keyboard?\nLighting settings, key assignments and Custom mode colors will change.",
            PrimaryButtonText = "Restore", CloseButtonText = "Cancel",
        };
        if (await box.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary) return;
        try
        {
            var (cfg, cust) = Backups.Load(b);
            await EffectEngine.Instance.StopAsync();
            await _kb.RestoreAsync(cfg, cust);
            Show("Restored.");
        }
        catch (Exception ex) { ShowError(ex); }
    }

    async Task DeleteAsync(BackupEntry b)
    {
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = "Delete backup", Content = $"Permanently delete \"{DisplayName(b)}\"?",
            PrimaryButtonText = "Delete", CloseButtonText = "Cancel",
        };
        if (await box.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary) return;
        File.Delete(b.Path);
        Fill();
    }

    void OpenFolder_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("explorer.exe", Storage.BackupDir) { UseShellExecute = true });

    void Show(string msg)
    {
        Info.Title = msg;
        Info.IsOpen = true;
        Error.IsOpen = false;
    }

    void ShowError(Exception e)
    {
        Error.Message = e.Message;
        Error.IsOpen = true;
    }
}
