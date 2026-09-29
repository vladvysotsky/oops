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

    /// <summary>
    /// Как часто проверять обновления в фоне. Раньше проверка шла только при
    /// запуске программы — а oops живёт в трее неделями без перезапуска, и о
    /// новой версии человек не узнавал, пока не нажмёт «Проверить» сам.
    /// Шесть часов: новая версия находится в тот же день, а GitHub при этом
    /// спрашивают четыре раза в сутки — до лимита анонимного API (60 в час)
    /// далеко.
    /// </summary>
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(6);

    /// <summary>
    /// Раз в час смотрим, не пора ли проверить. Сам тик ничего не стоит: в сеть
    /// идём, только если с прошлой проверки прошло <see cref="UpdateCheckInterval"/>.
    /// Таймер WinForms — тик приходит в UI-поток, туда же, где живут окна.
    /// </summary>
    private readonly System.Windows.Forms.Timer _updateTimer = new() { Interval = 60 * 60 * 1000 };

    /// <summary>Идёт проверка — вторую поверх не запускаем (таймер + пункт меню).</summary>
    private bool _checkingUpdates;

    /// <summary>
    /// Найденное фоновой проверкой обновление, которое ещё не поставили. Пункт
    /// меню тогда превращается в «Установить обновление X» — уведомление можно
    /// пропустить, а меню остаётся.
    /// </summary>
    private ReleaseInfo? _pendingRelease;

    /// <summary>О какой версии уже сказали уведомлением — не повторяем каждые шесть часов.</summary>
    private Version? _announcedVersion;

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
        _icon.BalloonTipClicked += (_, _) => { if (_pendingRelease != null) OfferUpdate(_pendingRelease); };

        // Хоткей перевода без скачанных моделей обязан сказать об этом, а не
        // промолчать: молчащий хоткей неотличим от сломанной программы — на
        // этом мы уже обжигались с проверкой сочетаний.
        _app.TranslationModelsMissing += (_, _) => Notice.Info(null,
            L10n.T("translate.models.title"),
            L10n.T("translate.models.body", ModelCatalog.TranslationMegabytes),
            L10n.T("translate.models.hint"));

        // Признак записи нужен видимый: подсказку у иконки в трее видно только
        // под курсором, а во время диктовки мышь в другом месте. Плашка внизу
        // экрана показывает состояние и живую расшифровку, подсказка в трее
        // остаётся как второй признак.
        _app.VoiceRecordingChanged += (_, recording) =>
        {
            _icon.Text = recording ? L10n.T("tray.recording") : IdleTooltip();
            if (recording) VoiceOverlay.Listening();
        };
        _app.VoicePartial += (_, text) => VoiceOverlay.Partial(text);
        _app.VoiceRecognising += (_, _) => VoiceOverlay.Recognising();
        _app.VoiceFinished += (_, _) => VoiceOverlay.HidePanel();

        // Диалог замены открывает трей, а не App: App не знает про окна, и
        // это единственное, что удерживает его от превращения в UI-класс.
        _app.ReplaceRequested += (_, selection) =>
        {
            _app.HotkeysSuspended = true;
            try { _app.ApplyReplacement(ReplaceForm.Ask(selection)); }
            finally { _app.HotkeysSuspended = false; }
        };

        _app.ReplaceNeedsSelection += (_, _) => Notice.Info(null,
            L10n.T("replace.noSelection.title"),
            L10n.T("replace.noSelection.body"),
            L10n.T("replace.noSelection.hint"));

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

        _app.SelectionTooLarge += (_, n) => Notice.Warn(null,
            L10n.T("selection.toobig.title"),
            L10n.T("selection.toobig.body", n, App.MaxSelectionLength),
            L10n.T("selection.toobig.hint"));

        _ = ScheduleStartupUpdateCheckAsync();
        _updateTimer.Tick += async (_, _) => await CheckForUpdatesIfDueAsync();
        _updateTimer.Start();
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

        _miUpdate = new ToolStripMenuItem();
        _miUpdate.Click += async (_, _) =>
        {
            if (_pendingRelease != null) OfferUpdate(_pendingRelease);
            else await CheckForUpdatesAsync(silent: false);
        };
        // Меню пересобирается при смене языка — текст пункта берём из
        // состояния, а не из умолчания, иначе найденное обновление «забылось» бы.
        RefreshUpdateItem();

        var miFeedback = new ToolStripMenuItem(L10n.T("tray.feedback"));
        miFeedback.Click += (_, _) => FeedbackForm.ShowDialogFor();

        var miNews = new ToolStripMenuItem(L10n.T("tray.news"));
        miNews.Click += (_, _) => WhatsNewForm.ShowAll(_app.Settings);

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
            miNews,
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
            using var form = new SettingsForm(_app.Settings,
                onLanguageChanged: BuildMenu,
                onThemeChanged: BuildMenu);
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
        // Не лезем в сеть в первые секунды после старта — не мешаем входу в систему.
        await Task.Delay(TimeSpan.FromSeconds(20));
        await CheckForUpdatesIfDueAsync();
    }

    /// <summary>
    /// Фоновая проверка — если включена и подошёл срок. Вызывается при старте
    /// и по часовому таймеру.
    /// </summary>
    private async Task CheckForUpdatesIfDueAsync()
    {
        if (!_app.Settings.AutoCheckUpdates) return;
        if (_pendingRelease != null) return;   // уже нашли, ждём, когда поставят
        if (DateTime.UtcNow - _app.Settings.LastUpdateCheckUtc < UpdateCheckInterval) return;
        await CheckForUpdatesAsync(silent: true);
    }

    /// <summary>
    /// Фоновая проверка нашла новую версию. Модальное окно здесь НЕЛЬЗЯ: оно
    /// выскочило бы посреди печати и утащило фокус из поля ввода — ровно того,
    /// ради чего программа существует. Поэтому уведомление Windows, которое
    /// фокус не трогает, и пункт меню, который не пропадёт, если уведомление
    /// пропустили.
    /// </summary>
    private void AnnounceUpdate(ReleaseInfo release)
    {
        _pendingRelease = release;
        RefreshUpdateItem();
        _icon.Text = IdleTooltip();

        if (_announcedVersion == release.Version) return;
        _announcedVersion = release.Version;
        _icon.ShowBalloonTip(10_000,
            L10n.T("update.available.title", release.Version.ToString(3)),
            L10n.T("update.available.body"),
            ToolTipIcon.Info);
    }

    private void OfferUpdate(ReleaseInfo release)
    {
        using var dlg = new UpdateDialog(release);
        dlg.ShowDialog();
    }

    /// <summary>
    /// Подсказка у иконки, когда ничего не пишется: о найденном обновлении она
    /// напоминает, пока его не поставили. Конец диктовки возвращает именно её,
    /// а не голое «oops», иначе напоминание пропадало бы после первой фразы.
    /// </summary>
    private string IdleTooltip() => _pendingRelease != null
        ? L10n.T("update.available.tooltip", _pendingRelease.Version.ToString(3))
        : "oops";

    private void RefreshUpdateItem()
    {
        _miUpdate.Text = _pendingRelease != null
            ? L10n.T("tray.update.install", _pendingRelease.Version.ToString(3))
            : L10n.T("tray.update");
    }

    /// <summary>
    /// Проверяет обновления. В тихом режиме молчит, если обновлений нет или
    /// запрос не удался; в явном — сообщает результат в любом случае.
    /// </summary>
    private async Task CheckForUpdatesAsync(bool silent)
    {
        if (_checkingUpdates) return;
        _checkingUpdates = true;
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

            // Сам человек спросил — показываем сразу. Фоновая проверка окно
            // не открывает: она сообщает и ждёт (см. AnnounceUpdate).
            if (silent) AnnounceUpdate(release);
            else OfferUpdate(release);
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
            _checkingUpdates = false;
            if (!silent) _miUpdate.Enabled = true;
        }
    }

    protected override void ExitThreadCore()
    {
        VoiceOverlay.Shutdown();
        _updateTimer.Stop();
        _updateTimer.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
        _app.Dispose();
        base.ExitThreadCore();
    }
}
