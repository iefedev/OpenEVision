using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenEVision.Effects;
using OpenEVision.Effects.Custom;
using OpenEVision.Pages;
using Wpf.Ui.Controls;

namespace OpenEVision.Dev;

/// <summary>
/// --snapshot DIR: renders every page to PNG (the README screenshots) from an off-screen window that never takes focus
/// or shows in the taskbar, then exits. Always runs in demo mode, so no keyboard is needed and no real data is shown.
/// </summary>
public static class Snapshot
{
    public static void Start(string dir) => _ = RunAsync(dir);

    static async Task RunAsync(string dir)
    {
        Directory.CreateDirectory(dir);
        var w = new MainWindow
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000, Top = -20000,
            ShowActivated = false, ShowInTaskbar = false,
            WindowBackdropType = WindowBackdropType.None,
        };
        w.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "ApplicationBackgroundBrush");
        try
        {
            w.Show();
            async Task Shot(string name, Type page, int waitMs = 2000)
            {
                w.NavigateTo(page);
                await Task.Delay(waitMs); // let the page read the (simulated) keyboard
                Save(w, Path.Combine(dir, name + ".png"));
            }

            await Shot("device", typeof(DevicePage));
            await Shot("lighting", typeof(LightingPage));

            var plasma = EffectEngine.All.First(e => e.Name == "plasma");
            await EffectEngine.Instance.StartAsync(plasma, EffectEngine.Defaults(plasma), 1);
            await Shot("effects", typeof(EffectsPage), 3000);
            await EffectEngine.Instance.StopAsync();

            await Shot("custom-effects", typeof(CustomEffectsPage), 1000);
            if (CustomEffectStore.Find(CustomPresets.GamingId) is { } gaming)
            {
                CustomEditorPage.Open(gaming.Id);
                await Task.Delay(2000);
                Save(w, Path.Combine(dir, "custom-editor.png"));
            }
            await Shot("paint", typeof(PaintPage));
            await Shot("remap", typeof(RemapPage));
            await Shot("backups", typeof(BackupsPage));
            await Shot("settings", typeof(SettingsPage), 1000);
        }
        catch (Exception e) { File.WriteAllText(Path.Combine(dir, "error.txt"), e.ToString()); }
        finally { Application.Current.Shutdown(); }
    }

    static void Save(Window w, string path)
    {
        var root = (FrameworkElement)w.Content;
        var bmp = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle((Brush)w.FindResource("ApplicationBackgroundBrush"), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        }
        bmp.Render(visual);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var f = File.Create(path);
        enc.Save(f);
    }
}
