using System.Windows.Forms;
using KeyLangSwitcher.Settings;

namespace KeyLangSwitcher.UI;

/// <summary>
/// Контекст приложения: иконка в трее, меню, владелец главных событий.
/// </summary>
public sealed class TrayContext : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly App _app;

    public TrayContext(App app)
    {
        _app = app;
        var menu = new ContextMenuStrip();
        var miEnabled = new ToolStripMenuItem("Включено") { Checked = app.Settings.Enabled, CheckOnClick = true };
        miEnabled.CheckedChanged += (_, _) =>
        {
            app.Settings.Enabled = miEnabled.Checked;
            app.Settings.Save();
        };

        var miSettings = new ToolStripMenuItem("Настройки...");
        miSettings.Click += (_, _) => ShowSettings();

        var miAbout = new ToolStripMenuItem("О программе");
        miAbout.Click += (_, _) => MessageBox.Show(
            "KeyLangSwitcher 0.1\nАналог PuntoSwitcher: конвертация набранного и выделенного текста между раскладками.",
            "О программе", MessageBoxButtons.OK, MessageBoxIcon.Information);

        var miExit = new ToolStripMenuItem("Выход");
        miExit.Click += (_, _) => ExitThread();

        menu.Items.AddRange(new ToolStripItem[]
        {
            miEnabled, new ToolStripSeparator(), miSettings, miAbout, new ToolStripSeparator(), miExit,
        });

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "KeyLangSwitcher",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => ShowSettings();

        // Связываем хук-события с визуальной индикацией.
        app.HotkeyFired += (_, info) =>
        {
            var msg = $"KLS {DateTime.Now:HH:mm:ss} {info}";
            _icon.Text = msg.Length > 63 ? msg.Substring(0, 63) : msg;
            try { _icon.ShowBalloonTip(800, "KeyLangSwitcher", $"Хоткей: {info}", ToolTipIcon.Info); } catch { }
        };
    }

    private static System.Drawing.Icon LoadIcon()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Resources", "icon.ico");
            if (File.Exists(path)) return new System.Drawing.Icon(path);
        }
        catch { }
        return System.Drawing.SystemIcons.Application;
    }

    private void ShowSettings()
    {
        using var form = new SettingsForm(_app.Settings);
        if (form.ShowDialog() == DialogResult.OK)
        {
            _app.Settings.Save();
            _app.ApplySettings();
            Autostart.Set(_app.Settings.Autostart);
        }
    }

    protected override void ExitThreadCore()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _app.Dispose();
        base.ExitThreadCore();
    }
}
