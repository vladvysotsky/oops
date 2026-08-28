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

        var miFeedback = new ToolStripMenuItem("Сообщить о проблеме…");
        miFeedback.Click += (_, _) => FeedbackForm.ShowDialogFor();

        var miAbout = new ToolStripMenuItem("О программе");
        miAbout.Click += (_, _) => Notice.Info(null, $"oops {UpdateService.CurrentVersion}",
            "Правит раскладку и регистр набранного текста. Границу задаёте вы: "
            + "первое нажатие хоткея берёт последнее слово, второе — весь набранный текст.",
            "Настройки — двойной клик по иконке в трее.");

        var miExit = new ToolStripMenuItem("Выход");
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

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "oops",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => ShowSettings();

        _ = ScheduleStartupUpdateCheckAsync();
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
            using var form = new SettingsForm(_app.Settings);
            if (form.ShowDialog() == DialogResult.OK)
            {
                // Автозапуск форма записывает в реестр сама: держать его копию в
                // settings.json нельзя — она затирала галочку, поставленную в
                // инсталляторе, при первом же сохранении настроек.
                var error = _app.Settings.Save();
                _app.ApplySettings();

                if (error != null)
                    Notice.Error(null, "Настройки не сохранились",
                        "Изменения действуют прямо сейчас, но после перезапуска "
                        + "программа вернётся к прежним.",
                        $"Проверьте, доступна ли для записи папка:\n{AppSettings.Location}",
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
                    Notice.Warn(null, "Не удалось проверить обновления",
                        "GitHub не ответил. Обычно это интернет или временный сбой на их стороне.",
                        "Программа продолжает работать; можно скачать новую версию вручную "
                        + $"со страницы релизов: {UpdateService.ReleasesPageUrl}");
                return;
            }

            if (check.Unavailable)
            {
                if (!silent)
                    Notice.Warn(null, "Репозиторий недоступен",
                        "GitHub отвечает, что такого репозитория нет. Обычно это значит, "
                        + "что он закрыт (private) или переименован.",
                        "Пока это так, обновления проверяться не будут ни у кого — "
                        + $"как и скачивание по ссылке {UpdateService.ReleasesPageUrl}");
                return;
            }

            if (check.NoReleases)
            {
                if (!silent)
                    Notice.Info(null, "Обновлений нет",
                        "В репозитории пока не опубликовано ни одного релиза.");
                return;
            }

            var release = check.Release!;
            if (!UpdateService.IsNewer(release))
            {
                if (!silent)
                    Notice.Info(null, "Установлена последняя версия",
                        $"У вас {UpdateService.CurrentVersion} — новее пока нет.");
                return;
            }

            using var dlg = new UpdateDialog(release);
            dlg.ShowDialog();
        }
        catch (Exception ex)
        {
            // Проверка обновлений не должна мешать работе приложения.
            if (!silent)
                Notice.Error(null, "Не удалось проверить обновления",
                    "Что-то пошло не так при обращении к GitHub.",
                    $"Скачать новую версию вручную можно здесь: {UpdateService.ReleasesPageUrl}",
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
