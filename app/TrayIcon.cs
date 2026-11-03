using System.Drawing;
using System.Windows;
using OpenEVision.Effects;
using Forms = System.Windows.Forms;

namespace OpenEVision;

/// <summary>Notification-area icon: open the window, pick/stop a software effect, quit.</summary>
public sealed class TrayIcon : IDisposable
{
    readonly Forms.NotifyIcon _icon;
    readonly Forms.ToolStripMenuItem _effects = new("Effect");

    public TrayIcon(Action open, Action quit)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => open());
        menu.Items.Add(_effects);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => quit());
        menu.Opening += (_, _) => FillEffects();

        _icon = new Forms.NotifyIcon { Icon = MakeIcon(), Text = "OpenEVision", Visible = true, ContextMenuStrip = menu };
        _icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) open(); };
    }

    void FillEffects()
    {
        _effects.DropDownItems.Clear();
        var engine = EffectEngine.Instance;
        foreach (var info in EffectEngine.All)
        {
            var item = new Forms.ToolStripMenuItem(info.Label) { Checked = engine.Running == info };
            item.Click += async (_, _) =>
            {
                try { await engine.StartAsync(info, EffectSettings.ParamsFor(info), EffectSettings.Brightness); }
                catch (Exception e) { _icon.ShowBalloonTip(4000, "Could not start the effect", e.Message, Forms.ToolTipIcon.Error); }
            };
            _effects.DropDownItems.Add(item);
        }
        var custom = Effects.Custom.CustomEffectStore.All();
        if (custom.Count > 0) _effects.DropDownItems.Add(new Forms.ToolStripSeparator());
        foreach (var def in custom)
        {
            var item = new Forms.ToolStripMenuItem(def.Name) { Checked = engine.Running?.Name == EffectEngine.CustomPrefix + def.Id };
            item.Click += async (_, _) =>
            {
                try { await engine.StartCustomAsync(def, EffectSettings.Brightness); }
                catch (Exception e) { _icon.ShowBalloonTip(4000, "Could not start the effect", e.Message, Forms.ToolTipIcon.Error); }
            };
            _effects.DropDownItems.Add(item);
        }
        _effects.DropDownItems.Add(new Forms.ToolStripSeparator());
        var stop = new Forms.ToolStripMenuItem("Stop (back to hardware mode)") { Enabled = engine.Running != null };
        stop.Click += async (_, _) => await engine.StopAsync();
        _effects.DropDownItems.Add(stop);
    }

    public void Notify(string title, string text) => _icon.ShowBalloonTip(3000, title, text, Forms.ToolTipIcon.Info);

    static Icon MakeIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);
            using var body = new SolidBrush(System.Drawing.Color.FromArgb(40, 40, 40));
            using var edge = new Pen(System.Drawing.Color.FromArgb(210, 210, 210), 1.5f);
            g.FillRectangle(body, 1, 7, 30, 18);
            g.DrawRectangle(edge, 1, 7, 30, 18);
            System.Drawing.Color[] cols = [System.Drawing.Color.FromArgb(255, 0, 64), System.Drawing.Color.FromArgb(255, 160, 0),
                System.Drawing.Color.FromArgb(0, 220, 80), System.Drawing.Color.FromArgb(0, 160, 255), System.Drawing.Color.FromArgb(180, 0, 255)];
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 5; c++)
                {
                    using var b = new SolidBrush(cols[(c + r) % 5]);
                    g.FillRectangle(b, 4 + c * 5, 10 + r * 5, 4, 3);
                }
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
