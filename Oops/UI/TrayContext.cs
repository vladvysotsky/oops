using System.Windows.Forms;
using Oops.Core;
using Oops.Settings;

namespace Oops.UI;

/// <summary>
/// Контекст приложения: иконка в трее, меню, владелец главных событий.
/// </summary>
public sealed class TrayContext : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly App _app;
    private ToolStripMenuItem _miUpdate = new();

    public TrayContext(App app)
    {
        _app = app;

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "oops",
            Visible = true,
        };
        BuildMenu();
        _icon.DoubleClick += (_, _) => ShowSettings();

        // Хоткей перевода без скачанных моделей обязан сказать об этом, а не
        // промолчать: молчащий хоткей неотличим от сломанной программы — на
        // этом мы уже обжигались с проверкой сочетаний.
        _app.TranslationModelsMissing += (_, _) => Notice.Info(null,
            L10n.T("translate.models.title"),
            L10n.T("translate.models.body", ModelCatalog.TranslationMegabytes),
            L10n.T("translate.models.hint"));

        // Идёт запись — единственный видимый признак: подсказка у иконки в трее.
        // Окна у программы нет, и без неё человек не знает, слушают его или нет.
        _app.VoiceRecordingChanged += (_, recording) =>
            _icon.Text = recording ? L10n.T("tray.recording") : "oops";

        _app.VoiceModelMissing += (_, _) => Notice.Info(null,
            L10n.T("voice.models.title"),
            L10n.T("voice.models.body", ModelCatalog.VoiceMegabytes),
            L10n.T("voice.models.hint"));

        _app.VoiceFailed += (_, ex) => Notice.Error(null,
            L10n.T("voice.failed.title"),
            L10n.T("voice.failed.body"),
            L10n.T("voice.failed.hint"),
            ex.ToString(), reportContext: "Ошибка голосового ввода");

        _app.TranslationFailed += (_, ex) => Notice.Error(null,
            L10n.T("translate.failed.title"),
            L10n.T("translate.failed.body"),
            L10n.T("translate.failed.hint"),
            ex.ToString(), reportContext: "Ошибка перевода");

        _ = ScheduleStartupUpdateCheckAsync();
    }

    /// <summary>
    /// Собирает меню заново. Вызывается при старте и после смены языка: тексты
    /// пунктов сидят в уже созданных ToolStripMenuItem, менять их по одному
    /// пришлось бы вручную и с риском что-нибудь забыть.
    /// </summary>
    private void BuildMenu()
    {
        var menu = new ContextMenuStrip();

        var miEnabled = new ToolStripMenuItem(L10n.T("tray.enabled"))
            { Checked = _app.Settings.Enabled, CheckOnClick = true };
        miEnabled.CheckedChanged += (_, _) =>
        {
            _app.Settings.Enabled = miEnabled.Checked;
            _app.Settings.Save();
        };

        var miSettings = new ToolStripMenuItem(L10n.T("tray.settings"));
        miSettings.Click += (_, _) => ShowSettings();

        _miUpdate = new ToolStripMenuItem(L10n.T("tray.update"));
        _miUpdate.Click += async (_, _) => await CheckForUpdatesAsync(silent: false);

        var miFeedback = new ToolStripMenuItem(L10n.T("tray.feedback"));
        miFeedback.Click += (_, _) => FeedbackForm.ShowDialogFor();

        var miAbout = new ToolStripMenuItem(L10n.T("tray.about"));
        miAbout.Click += (_, _) => Notice.Info(null,
            $"oops {UpdateService.CurrentVersion}",
            L10n.T("about.body"), L10n.T("about.hint"));

        var miExit = new ToolStripMenuItem(L10n.T("tray.exit"));
        miExit.Click += (_, _) => ExitThread();

        menu.Items.AddRange(new ToolStripItem[]
        {
            miEnabled,
            new ToolStripSeparator(),
            miSettings,
            _miUpdate,
            miFeedback,
            miAbout,
            new ToolStripSeparator(),
            miExit,
        });
        Theme.ApplyMenuChrome(menu);

        _icon.ContextMenuStrip?.Dispose();
        _icon.ContextMenuStrip = menu;
    }

    /// <summary>
    /// Берёт иконку из ресурсов сборки. Именно из сборки, а не из файла рядом с
    /// exe: при single-file публикации и установке отдельного файла на месте нет,
    /// и в трее оставалась системная заглушка.
    /// Размер запрашиваем под текущий DPI, иначе Windows масштабирует не ту грань.
    /// </summary>
    private static System.Drawing.Icon LoadIcon()
    {
        try
        {
            using var stream = typeof(TrayContext).Assembly
                .GetManifestResourceStream("Oops.Resources.icon.ico");
            if (stream != null)
                return new System.Drawing.Icon(stream, SystemInformation.SmallIconSize);
        }
        catch { }
        return System.Drawing.SystemIcons.Application;
    }

    private void ShowSettings()
    {
        // Хоткеи на время настроек выключаем: иначе диалог записи невозможно
        // использовать — нажатие текущего хоткея перехватил бы хук.
        _app.HotkeysSuspended = true;
        try
        {
            using var form = new SettingsForm(_app.Settings, onLanguageChanged: BuildMenu);
            if (form.ShowDialog() == DialogResult.OK)
            {
                // Автозапуск форма записывает в реестр сама: держать его копию в
                // settings.json нельзя — она затирала галочку, поставленную в
                // инсталляторе, при первом же сохранении настроек.
                var error = _app.Settings.Save();
                _app.ApplySettings();

                if (error != null)
                    Notice.Error(null, L10n.T("save.failed.title"),
                        L10n.T("save.failed.body"),
                        L10n.T("save.failed.hint", AppSettings.Location),
                        error, reportContext: "Не удалось сохранить настройки");
            }
        }
        finally
        {
            _app.HotkeysSuspended = false;
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
            var check = await UpdateService.FetchLatestAsync();

            _app.Settings.LastUpdateCheckUtc = DateTime.UtcNow;
            _app.Settings.Save();

            if (check.Failed)
            {
                if (!silent)
                    Notice.Warn(null, L10n.T("update.failed.title"),
                        L10n.T("update.failed.body"),
                        L10n.T("update.failed.hint", UpdateService.ReleasesPageUrl));
                return;
            }

            if (check.Unavailable)
            {
                if (!silent)
                    Notice.Warn(null, L10n.T("update.unavailable.title"),
                        L10n.T("update.unavailable.body"),
                        L10n.T("update.unavailable.hint", UpdateService.ReleasesPageUrl));
                return;
            }

            if (check.NoReleases)
            {
                if (!silent)
                    Notice.Info(null, L10n.T("update.none.title"),
                        L10n.T("update.none.body"));
                return;
            }

            var release = check.Release!;
            if (!UpdateService.IsNewer(release))
            {
                if (!silent)
                    Notice.Info(null, L10n.T("update.latest.title"),
                        L10n.T("update.latest.body", UpdateService.CurrentVersion));
                return;
            }

            using var dlg = new UpdateDialog(release);
            dlg.ShowDialog();
        }
        catch (Exception ex)
        {
            // Проверка обновлений не должна мешать работе приложения.
            if (!silent)
                Notice.Error(null, L10n.T("update.failed.title"),
                    L10n.T("update.error.body"),
                    L10n.T("update.error.hint", UpdateService.ReleasesPageUrl),
                    ex.ToString(), reportContext: "Ошибка проверки обновлений");
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
