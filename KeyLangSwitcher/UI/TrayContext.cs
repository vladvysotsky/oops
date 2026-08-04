using System.Windows.Forms;
using KeyLangSwitcher.Core;
using KeyLangSwitcher.Settings;

namespace KeyLangSwitcher.UI;

/// <summary>
/// Контекст приложения: иконка в трее, меню, владелец главных событий.
/// </summary>
public sealed class TrayContext : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly App _app;
    private readonly ToolStripMenuItem _miUpdate;

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

        var miSettings = new ToolStripMenuItem("Настройки…");
        miSettings.Click += (_, _) => ShowSettings();

        _miUpdate = new ToolStripMenuItem("Проверить обновления");
        _miUpdate.Click += async (_, _) => await CheckForUpdatesAsync(silent: false);

        var miAbout = new ToolStripMenuItem("О программе");
        miAbout.Click += (_, _) => MessageBox.Show(
            $"KeyLangSwitcher {UpdateService.CurrentVersion}\n\n"
            + "Правит раскладку и регистр набранного текста.\n"
            + "Границу задаёте вы: каждое следующее нажатие хоткея\n"
            + "захватывает ещё одно слово.",
            "О программе", MessageBoxButtons.OK, MessageBoxIcon.Information);

        var miExit = new ToolStripMenuItem("Выход");
        miExit.Click += (_, _) => ExitThread();

        menu.Items.AddRange(new ToolStripItem[]
        {
            miEnabled,
            new ToolStripSeparator(),
            miSettings,
            _miUpdate,
            miAbout,
            new ToolStripSeparator(),
            miExit,
        });

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "KeyLangSwitcher",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => ShowSettings();

        _ = ScheduleStartupUpdateCheckAsync();
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

    /// <summary>
    /// Фоновая проверка при запуске: не чаще раза в сутки и молча, если новой
    /// версии нет или сеть недоступна.
    /// </summary>
    private async Task ScheduleStartupUpdateCheckAsync()
    {
        if (!_app.Settings.AutoCheckUpdates) return;
        if (DateTime.UtcNow - _app.Settings.LastUpdateCheckUtc < TimeSpan.FromDays(1)) return;

        // Не лезем в сеть в первые секунды после старта — не мешаем входу в систему.
        await Task.Delay(TimeSpan.FromSeconds(20));
        await CheckForUpdatesAsync(silent: true);
    }

    /// <summary>
    /// Проверяет обновления. В тихом режиме молчит, если обновлений нет или
    /// запрос не удался; в явном — сообщает результат в любом случае.
    /// </summary>
    private async Task CheckForUpdatesAsync(bool silent)
    {
        if (!silent) _miUpdate.Enabled = false;
        try
        {
            var release = await UpdateService.FetchLatestAsync();

            _app.Settings.LastUpdateCheckUtc = DateTime.UtcNow;
            _app.Settings.Save();

            if (release == null)
            {
                if (!silent)
                    MessageBox.Show("Не удалось получить сведения о релизах.\nПроверьте подключение к интернету.",
                        "Обновление", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!UpdateService.IsNewer(release))
            {
                if (!silent)
                    MessageBox.Show($"Установлена последняя версия ({UpdateService.CurrentVersion}).",
                        "Обновление", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var dlg = new UpdateDialog(release);
            dlg.ShowDialog();
        }
        catch
        {
            // Проверка обновлений не должна мешать работе приложения.
            if (!silent)
                MessageBox.Show("Не удалось проверить обновления.",
                    "Обновление", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            if (!silent) _miUpdate.Enabled = true;
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
