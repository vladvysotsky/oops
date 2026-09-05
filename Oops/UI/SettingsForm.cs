using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Oops.Core;
using Oops.Hooks;
using Oops.Settings;

namespace Oops.UI;

/// <summary>
/// Окно настроек по дизайн-системе из <see cref="Theme"/>.
///
/// Вся разметка построена на вложенных TableLayoutPanel с AutoSize. Фиксированные
/// высоты строк и карточек намеренно не используются: при DPI-масштабировании
/// (125%/150%) и переносе строк контент в них не помещается и обрезается.
/// </summary>
public sealed class SettingsForm : ThemedForm
{
    private readonly AppSettings _settings;

    private readonly CheckBox _cbEnabled = new ToggleBox();
    private readonly CheckBox _cbAutostart = new ToggleBox();
    private readonly CheckBox _cbAutoUpdate = new ToggleBox();
    private readonly CheckBox _cbCharByChar = new ToggleBox();
    private readonly CheckBox _cbVoiceLive = new ToggleBox();
    private readonly SegmentedControl _language = new();
    private readonly HotkeyDisplay _convertKeys = new() { Interactive = true };
    private readonly HotkeyDisplay _caseKeys = new() { Interactive = true };
    private readonly HotkeyDisplay _translateKeys = new() { Interactive = true };
    private readonly HotkeyDisplay _voiceKeys = new() { Interactive = true };
    private readonly Stepper _nudIdle = new();
    private readonly Stepper _nudExpand = new();

    // Живая проверка: показывает, что из нажатого реально доходит до программы.
    // Молчащий хоткей ничем не отличается от неработающей программы, и без
    // такого окошка отличить «Windows забрал сочетание себе» от «мы его не
    // узнали» можно было только гаданием.
    private readonly KeyboardHook _probe = new();
    private readonly HotkeyDisplay _probeKeys = new();
    private readonly Label _probeStatus = new();

    private HotkeyConfig _convertHotkey;
    private HotkeyConfig _caseHotkey;
    private HotkeyConfig _translateHotkey;
    private HotkeyConfig _voiceHotkey;

    /// <summary>
    /// Выбранный язык — отдельно от настроек. В settings.Language он попадает
    /// только по «Сохранить», а Populate после пересборки окна читал именно
    /// настройки и возвращал переключатель к прежнему значению: язык менялся и
    /// тут же откатывался, окно моргало впустую.
    /// </summary>
    private string _languagePref;

    /// <summary>Вызывается, когда язык сменили: трей пересобирает своё меню.</summary>
    private readonly Action? _onLanguageChanged;

    public SettingsForm(AppSettings settings, Action? onLanguageChanged = null)
    {
        _settings = settings;
        _onLanguageChanged = onLanguageChanged;
        _convertHotkey = Clone(settings.ConvertHotkey);
        _caseHotkey = Clone(settings.ChangeCaseHotkey);
        _translateHotkey = Clone(settings.TranslateHotkey);
        _voiceHotkey = Clone(settings.VoiceHotkey);
        _languagePref = settings.Language;

        Text = "oops";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        // Фон, шрифт, DPI и тёмный заголовок окна приходят из ThemedForm.

        BuildLayout();
        Populate();

        // Окно по содержимому — но высота страницы уже посчитана по самой
        // высокой вкладке и зафиксирована, поэтому окно не прыгает при
        // переключении и не растёт выше экрана, как было до вкладок.
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
    }

    // ---------------------------------------------------------------- layout

    /// <summary>Ширина колонки контента (карточки, заголовки). Масштабируется системой по DPI.</summary>
    private static readonly int ContentWidth = Theme.Px(580);

    // ЕДИНСТВЕННЫЙ источник ширины — колонка контента. Всё внутри растягивается
    // якорями (Left|Right) и доками, правые контролы сидят в AutoSize-колонках.
    // Никаких больше «зарезервированных» ширин: у прежней разметки было два
    // источника правды — абсолютные колонки таблиц и Size контролов, — и любое
    // их расхождение (DPI, чуть более длинный текст) резало кнопки и рвало
    // правый край. AutoSize-колонка не может обрезать свой контрол по построению.

    /// <summary>
    /// Ширина поля с клавишами: «Ctrl+Shift+Win» помещается с запасом. Шире не
    /// надо — лишняя ширина отбирает место у подписи слева и заставляет её
    /// переноситься.
    /// </summary>
    private static readonly int HotkeyWidth = Theme.Px(220);

    /// <summary>Ширина внутри карточки: колонка контента минус её отступы.</summary>
    private static readonly int CardInnerWidth = ContentWidth - Theme.S3 * 2;

    /// <summary>Вкладки: порядок совпадает с порядком страниц в BuildPage.</summary>
    private readonly SegmentedControl _tabs = new();
    private readonly Panel _page = new();

    private void BuildLayout()
    {
        // Корень — таблица, а не Dock.Fill + Dock.Bottom: порядок докинга в
        // WinForms зависит от z-order и ломается при правках (см. CLAUDE.md).
        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 4,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Canvas,
            Padding = new Padding(Theme.S4, Theme.S4, Theme.S4, Theme.S3),
            Margin = new Padding(0),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ContentWidth));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // шапка
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // вкладки
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // страница
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // кнопки

        var header = Header();
        header.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        root.Controls.Add(header, 0, 0);

        _tabs.SetItems(
            L10n.T("settings.section.general"),
            L10n.T("settings.section.hotkeys"),
            L10n.T("settings.section.behaviour"));
        _tabs.Margin = new Padding(0, Theme.S2, 0, Theme.S3);
        _tabs.SelectedIndexChanged += (_, _) => ShowPage(_tabs.SelectedIndex);
        root.Controls.Add(_tabs, 0, 1);

        // Все вкладки строятся сразу и живут одновременно — видна одна.
        // Строить их заново на каждое переключение нельзя: контролы (галочки,
        // степперы, поля хоткеев) — общие поля формы, и они принадлежат ровно
        // одной вкладке каждый.
        _page.AutoScroll = false;
        _page.BackColor = Theme.Canvas;
        _page.Margin = new Padding(0);
        _page.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        // При пересборке (смена языка) _page — то же самое поле формы, и его
        // прежние страницы остаются внутри: Controls.Clear() у ФОРМЫ снимает
        // только корень. Без этой строки старые вкладки копились и рисовались
        // под новыми — на экране была русская карточка, а под ней английская.
        _page.Controls.Clear();

        for (int i = 0; i < TabCount; i++)
        {
            var page = BuildPage(i);
            page.Dock = DockStyle.Top;
            page.Visible = false;
            _pages[i] = page;
            _page.Controls.Add(page);
        }

        // Высоту страницы здесь НЕ считаем: до раскладки переносы строк ещё
        // не известны, и любое число тут — выдумка. Настоящий замер идёт в
        // FitPages() из OnLoad, когда ширина уже фактическая.
        _page.Height = Theme.Px(200);   // временная величина до первого замера
        root.Controls.Add(_page, 0, 2);

        root.Controls.Add(Footer(), 0, 3);

        Controls.Add(root);
        ShowPage(0);
    }

    private const int TabCount = 3;
    private readonly TableLayoutPanel[] _pages = new TableLayoutPanel[TabCount];

    /// <summary>Содержимое одной вкладки.</summary>
    private TableLayoutPanel BuildPage(int index)
    {
        var stack = Stack();
        switch (index)
        {
            case 0:
                AddAutoRow(stack, GeneralCard());
                break;
            case 1:
                // Проверка живёт рядом с хоткеями: она про них и нужна ровно в
                // тот момент, когда сочетание молчит и его меняют.
                AddAutoRow(stack, HotkeysCard());
                AddAutoRow(stack, Note(L10n.T("welcome.note.altShift")));
                AddAutoRow(stack, SectionLabel(L10n.T("settings.section.probe")));
                AddAutoRow(stack, ProbeCard());
                break;
            default:
                AddAutoRow(stack, BehaviourCard());
                AddAutoRow(stack, SectionLabel(L10n.T("settings.translation.title")));
                AddAutoRow(stack, ModelCard(_translation, "settings.translation.body",
                    "settings.translation",
                    () => Translator.IsReady,
                    (p, ct) => Translator.EnsureModelsAsync(p, ct),
                    Translator.RemoveModels));
                AddAutoRow(stack, SectionLabel(L10n.T("settings.voice.title")));
                AddAutoRow(stack, ModelCard(_voice, "settings.voice.body",
                    "settings.voice",
                    () => VoiceInput.IsReady,
                    (p, ct) => VoiceInput.EnsureModelAsync(p, ct),
                    VoiceInput.RemoveModel));
                break;
        }
        return stack;
    }

    /// <summary>Переключает вкладку — только видимостью, ничего не пересоздаём.</summary>
    private void ShowPage(int index)
    {
        for (int i = 0; i < TabCount; i++)
            if (_pages[i] != null) _pages[i].Visible = i == index;
        _tabIndex = index;
    }

    /// <summary>Заголовок подраздела внутри вкладки.</summary>
    private static Control SectionLabel(string text) => new Label
    {
        Text = text,
        Font = Theme.SectionLabel,
        ForeColor = Theme.TextMuted,
        AutoSize = true,
        Margin = new Padding(Theme.S1, Theme.S3, 0, Theme.S2),
        BackColor = Color.Transparent,
    };

    /// <summary>Пояснение под карточкой — мелким второстепенным текстом.</summary>
    private static Control Note(string text) => new Label
    {
        Text = text,
        Font = Theme.Caption,
        ForeColor = Theme.TextMuted,
        AutoSize = true,
        Margin = new Padding(Theme.S1, Theme.S2, Theme.S1, 0),
        BackColor = Color.Transparent,
    };

    private static void AddAutoRow(TableLayoutPanel host, Control child)
    {
        // Ребёнок с дефолтным якорем растягивается на ширину колонки: ширину
        // диктует колонка, а не контрол. Явно выставленный якорь (футер с
        // Right) не трогаем.
        if (child.Anchor == (AnchorStyles.Top | AnchorStyles.Left))
            child.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        host.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        host.Controls.Add(child, 0, host.RowCount);
        host.RowCount++;
    }

    private Control Header()
    {
        var stack = Stack();
        stack.Margin = new Padding(0, 0, 0, Theme.S2);

        AddAutoRow(stack, new Label
        {
            Text = "oops",
            Font = Theme.Title,
            ForeColor = Theme.Text,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, Theme.S1),
            BackColor = Color.Transparent,
        });
        AddAutoRow(stack, new Label
        {
            Text = L10n.T("settings.subtitle"),
            Font = Theme.Caption,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            Margin = new Padding(0),
            BackColor = Color.Transparent,
        });
        return stack;
    }


    private Control GeneralCard()
    {
        var card = NewCard(out var rows);
        AddAutoRow(rows, CheckRow(_cbEnabled, L10n.T("settings.enabled"),
            L10n.T("settings.enabled.hint")));
        AddAutoRow(rows, Divider());
        AddAutoRow(rows, CheckRow(_cbAutostart, L10n.T("settings.autostart"),
            L10n.T("settings.autostart.hint")));
        AddAutoRow(rows, Divider());
        AddAutoRow(rows, CheckRow(_cbAutoUpdate, L10n.T("settings.autoupdate"),
            L10n.T("settings.autoupdate.hint")));
        AddAutoRow(rows, Divider());
        AddAutoRow(rows, LanguageRow());
        return card;
    }

    /// <summary>
    /// Выбор языка интерфейса. Сегментированный переключатель, а не системный
    /// ComboBox: тот рисует своё системно-синее выделение и выпадающий список
    /// чужой темы, то есть выпадал из дизайн-системы. Вариантов три — все видны
    /// сразу, раскрывать нечего.
    /// </summary>
    private Control LanguageRow()
    {
        // Названия языков — на самих языках, так принято: человек, открывший
        // чужую локаль, всё равно найдёт свою строку. «Авто» короткое, чтобы
        // три сегмента не растянули строку на всю карточку.
        _language.SetItems(L10n.T("settings.language.auto"), "Русский", "English");
        return Row(L10n.T("settings.language"), L10n.T("settings.language.hint"), _language);
    }

    /// <summary>Порядок пунктов списка языков — он же порядок значений настройки.</summary>
    private static readonly string[] LanguageValues = { L10n.Auto, L10n.Russian, L10n.English };

    private Control HotkeysCard()
    {
        var card = NewCard(out var rows);
        AddAutoRow(rows, HotkeyRow(
            L10n.T("hotkey.layout"), L10n.T("hotkey.layout.hint"),
            _convertKeys, () => RecordInto(ref _convertHotkey, _convertKeys)));
        AddAutoRow(rows, Divider());
        AddAutoRow(rows, HotkeyRow(
            L10n.T("hotkey.case"), L10n.T("hotkey.case.hint"),
            _caseKeys, () => RecordInto(ref _caseHotkey, _caseKeys)));
        AddAutoRow(rows, Divider());
        AddAutoRow(rows, HotkeyRow(
            L10n.T("hotkey.translate"), L10n.T("hotkey.translate.hint"),
            _translateKeys, () => RecordInto(ref _translateHotkey, _translateKeys)));
        AddAutoRow(rows, Divider());
        AddAutoRow(rows, HotkeyRow(
            L10n.T("hotkey.voice"), L10n.T("hotkey.voice.hint"),
            _voiceKeys, () => RecordInto(ref _voiceHotkey, _voiceKeys)));
        return card;
    }

    private Control BehaviourCard()
    {
        var card = NewCard(out var rows);

        _nudExpand.Minimum = 1; _nudExpand.Maximum = 10;
        AddAutoRow(rows, NumberRow(_nudExpand, L10n.T("settings.expand"), L10n.T("unit.sec"),
            L10n.T("settings.expand.hint")));
        AddAutoRow(rows, Divider());

        _nudIdle.Minimum = 5; _nudIdle.Maximum = 600;
        AddAutoRow(rows, NumberRow(_nudIdle, L10n.T("settings.forget"), L10n.T("unit.sec"),
            L10n.T("settings.forget.hint")));
        AddAutoRow(rows, Divider());

        AddAutoRow(rows, CheckRow(_cbCharByChar, L10n.T("settings.slowTyping"),
            L10n.T("settings.slowTyping.hint")));
        return card;
    }

    /// <summary>
    /// Одна карточка моделей: перевод и голосовой ввод устроены одинаково —
    /// текст состояния, одна кнопка и своя отменяемая загрузка.
    /// </summary>
    private sealed class ModelSection
    {
        public readonly Label Status = new();
        public readonly FlatButton Action = new();
        public CancellationTokenSource? Cts;
    }

    private readonly ModelSection _translation = new();
    private readonly ModelSection _voice = new();

    /// <summary>
    /// Карточка моделей. Модели весят от полусотни мегабайт до полугигабайта и
    /// в установщик не кладутся — их скачивают отсюда, по явному нажатию. Пока
    /// их нет, соответствующий хоткей не молчит, а приводит человека сюда же.
    /// </summary>
    private Control ModelCard(ModelSection section, string bodyKey, string prefix,
        Func<bool> isReady,
        Func<IProgress<ModelProgress>, CancellationToken, Task> ensure,
        Action remove)
    {
        var card = NewCard(out var rows);

        AddAutoRow(rows, new Label
        {
            Text = L10n.T(bodyKey),
            Font = Theme.Caption,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            MaximumSize = new Size(CardInnerWidth, 0),
            Margin = new Padding(0, 0, 0, Theme.S2),
            BackColor = Color.Transparent,
        });

        section.Status.Font = Theme.Body;
        section.Status.ForeColor = Theme.Text;
        section.Status.AutoSize = true;
        section.Status.MaximumSize = new Size(CardInnerWidth, 0);
        section.Status.Margin = new Padding(0, 0, 0, Theme.S2);
        section.Status.BackColor = Color.Transparent;
        AddAutoRow(rows, section.Status);

        section.Action.AutoSize = true;
        section.Action.MinimumSize = new Size(Theme.Px(160), 0);
        section.Action.Click += async (_, _) => await ToggleModels(section, prefix, isReady, ensure, remove);
        AddAutoRow(rows, ButtonBar.Create(CardInnerWidth, new Padding(0), section.Action));

        if (prefix == "settings.voice")
        {
            AddAutoRow(rows, Divider());
            AddAutoRow(rows, CheckRow(_cbVoiceLive, L10n.T("settings.voice.live"),
                L10n.T("settings.voice.live.hint")));
        }

        RefreshModelCard(section, prefix, isReady);
        return card;
    }

    /// <summary>Показывает, что сейчас с моделями: скачаны или их нет.</summary>
    private static void RefreshModelCard(ModelSection section, string prefix, Func<bool> isReady)
    {
        // Во время загрузки текст ведёт обработчик прогресса — не перебиваем.
        if (section.Cts != null) return;

        bool ready = isReady();
        section.Status.Text = ready
            ? L10n.T(prefix + ".ready")
            : L10n.T(prefix + ".absent", Megabytes(prefix));
        section.Action.Text = L10n.T(ready ? prefix + ".remove" : prefix + ".download");
        section.Action.Primary = !ready;
    }

    private static int Megabytes(string prefix) => prefix == "settings.voice"
        ? ModelCatalog.VoiceMegabytes
        : ModelCatalog.TranslationMegabytes;

    private async Task ToggleModels(ModelSection section, string prefix,
        Func<bool> isReady,
        Func<IProgress<ModelProgress>, CancellationToken, Task> ensure,
        Action remove)
    {
        // Идёт загрузка — эта же кнопка её отменяет: недокачанное остаётся
        // в .part и в следующий раз досылается, а не качается заново.
        if (section.Cts != null) { section.Cts.Cancel(); return; }

        if (isReady())
        {
            remove();
            RefreshModelCard(section, prefix, isReady);
            return;
        }

        section.Cts = new CancellationTokenSource();
        section.Action.Text = L10n.T("common.cancel");
        section.Action.Primary = false;
        try
        {
            var progress = new Progress<ModelProgress>(p =>
                section.Status.Text = L10n.T("settings.models.progress",
                    p.FileIndex, p.FileCount, p.Percent));
            await ensure(progress, section.Cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Отмена — не ошибка: человек сам нажал.
        }
        catch (Exception ex)
        {
            Notice.Error(this, L10n.T("models.failed.title"),
                L10n.T("models.failed.body"), L10n.T("models.failed.hint"),
                ex.ToString(), reportContext: "Не удалось скачать модели");
        }
        finally
        {
            section.Cts.Dispose();
            section.Cts = null;
            RefreshModelCard(section, prefix, isReady);
        }
    }

    /// <summary>
    /// Карточка живой проверки: сюда попадает то, что видит клавиатурный хук.
    /// Если сочетание нажали, а здесь пусто — его забрала себе Windows и до
    /// программы оно не доходит; если показано, но написано «не назначено» —
    /// значит настройки хранят другое сочетание.
    /// </summary>
    private Control ProbeCard()
    {
        var card = NewCard(out var rows);

        // Вертикально, а не «подпись слева — контрол справа»: длинному сочетанию
        // из четырёх клавиш нужна вся ширина карточки, а двухколоночная вёрстка
        // ломала заголовок переносом.
        AddAutoRow(rows, new Label
        {
            Text = L10n.T("probe.prompt"),
            Font = Theme.Caption,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, Theme.S2),
            BackColor = Color.Transparent,
        });

        _probeKeys.Height = Theme.KeyRowHeight;
        _probeKeys.Margin = new Padding(0);
        _probeKeys.SetCombo(string.Empty);
        AddAutoRow(rows, _probeKeys);

        _probeStatus.Text = L10n.T("probe.waiting");
        _probeStatus.Font = Theme.Caption;
        _probeStatus.ForeColor = Theme.TextMuted;
        _probeStatus.AutoSize = true;
        _probeStatus.Margin = new Padding(0, Theme.S2, 0, 0);
        _probeStatus.BackColor = Color.Transparent;
        AddAutoRow(rows, _probeStatus);

        return card;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        FitPages();
        _probe.KeyDown += OnProbeKey;
        try { _probe.Install(); }
        catch { _probeStatus.Text = L10n.T("probe.hookFailed"); }
    }

    /// <summary>
    /// Подгоняет высоту области вкладок под самую высокую из них — и окно под
    /// неё же. Прокрутки нет намеренно: она прятала бы часть настроек ровно на
    /// крупном шрифте, где видеть всё сразу нужнее всего.
    ///
    /// Меряем ЗДЕСЬ, а не при сборке: до раскладки ширина карточек ещё не
    /// известна, абзацы переносятся не там, где будут, и высота выходит
    /// выдуманной. Ровно из-за этого окно однажды растянулось вдвое выше
    /// экрана, а между карточками зияли дыры.
    ///
    /// Невидимую вкладку WinForms не раскладывает вовсе, поэтому каждую на миг
    /// показываем — до первой отрисовки окна этого никто не увидит.
    /// </summary>
    private void FitPages()
    {
        int width = _page.ClientSize.Width;
        if (width <= 0) width = ContentWidth;

        int tallest = 0;
        SuspendLayout();
        foreach (var page in _pages)
        {
            if (page == null) continue;
            bool wasVisible = page.Visible;
            page.Visible = true;
            page.Width = width;
            page.PerformLayout();
            tallest = Math.Max(tallest, page.PreferredSize.Height);
            page.Visible = wasVisible;
        }
        ResumeLayout(true);

        if (tallest <= 0) return;

        // Всё, что в окне кроме страницы: шапка, вкладки, кнопки, отступы.
        // Считаем ДО правки высоты страницы, иначе величина уедет вместе с ней.
        int chrome = Height - ClientSize.Height;                 // рамка и заголовок
        int aroundPage = ClientSize.Height - _page.Height;

        // Окно следует за содержимым в обе стороны. AutoSize формы выключаем:
        // иначе следующий проход раскладки вернёт её к прежнему размеру, и
        // правка не доживёт до отрисовки.
        AutoSize = false;

        int maxClient = Screen.FromControl(this).WorkingArea.Height - chrome;
        int pageHeight = tallest;

        // Потолок — рабочая область экрана. Прокрутка здесь нежелательна, но
        // окно выше экрана хуже неё во всех отношениях: кнопки «Сохранить»
        // не видно, и мышью до неё не добраться. Упёрлись — прокручиваем
        // именно страницу, шапка и кнопки остаются на месте.
        if (aroundPage + pageHeight > maxClient)
        {
            pageHeight = Math.Max(Theme.Px(200), maxClient - aroundPage);
            _page.AutoScroll = true;
        }

        _page.Height = pageHeight;
        ClientSize = new Size(ClientSize.Width, aroundPage + pageHeight);

        // Высоту окна мы только что изменили, а позицию WinForms посчитала до
        // этого — без ручного центрирования окно оказывается смещённым вниз
        // ровно на половину прибавки.
        CenterOnWorkArea();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _probe.KeyDown -= OnProbeKey;
        _probe.Dispose();
        base.OnFormClosed(e);
    }

    private void OnProbeKey(object? sender, KeyboardHook.KeyEvent e)
    {
        if (e.IsRepeat) return;

        bool isConvert = _convertHotkey.Matches(e.VirtualKey, e.Ctrl, e.Shift, e.Alt, e.Win);
        bool isCase = !isConvert && _caseHotkey.Matches(e.VirtualKey, e.Ctrl, e.Shift, e.Alt, e.Win);
        bool isTranslate = !isConvert && !isCase
            && _translateHotkey.Matches(e.VirtualKey, e.Ctrl, e.Shift, e.Alt, e.Win);
        bool isVoice = !isConvert && !isCase && !isTranslate
            && _voiceHotkey.Matches(e.VirtualKey, e.Ctrl, e.Shift, e.Alt, e.Win);

        // Совпавшее сочетание глотаем: иначе Win откроет «Пуск» прямо из окна
        // настроек. Всё остальное пропускаем — окном надо пользоваться.
        if (isConvert || isCase || isTranslate || isVoice) e.Handled = true;

        var seen = new HotkeyConfig
        {
            Ctrl = e.Ctrl, Shift = e.Shift, Alt = e.Alt, Win = e.Win,
            Key = IsModifierKey(e.VirtualKey) ? 0 : (int)e.VirtualKey,
        };
        _probeKeys.SetCombo(seen.ToString());

        if (isConvert)
        {
            _probeStatus.Text = L10n.T("probe.matchLayout");
            _probeStatus.ForeColor = Theme.Accent;
        }
        else if (isCase)
        {
            _probeStatus.Text = L10n.T("probe.matchCase");
            _probeStatus.ForeColor = Theme.Accent;
        }
        else if (isTranslate)
        {
            _probeStatus.Text = L10n.T("probe.matchTranslate");
            _probeStatus.ForeColor = Theme.Accent;
        }
        else if (isVoice)
        {
            _probeStatus.Text = L10n.T("probe.matchVoice");
            _probeStatus.ForeColor = Theme.Accent;
        }
        else
        {
            _probeStatus.Text = L10n.T("probe.noMatch");
            _probeStatus.ForeColor = Theme.TextMuted;
        }
    }

    private static bool IsModifierKey(Keys k) =>
        k is Keys.ControlKey or Keys.LControlKey or Keys.RControlKey
          or Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey
          or Keys.Menu or Keys.LMenu or Keys.RMenu
          or Keys.LWin or Keys.RWin;

    private Control Footer()
    {
        var save = new FlatButton { Text = L10n.T("common.save"), Primary = true, AutoSize = true, MinimumSize = new Size(Theme.Px(124), 0), DialogResult = DialogResult.OK };
        var cancel = new FlatButton { Text = L10n.T("common.cancel"), AutoSize = true, MinimumSize = new Size(Theme.Px(104), 0), DialogResult = DialogResult.Cancel };
        // Button.OnClick выставляет DialogResult формы ДО вызова наших обработчиков,
        // поэтому вернуть None — штатный способ отменить закрытие окна.
        save.Click += (_, _) => { if (!ApplyToSettings()) DialogResult = DialogResult.None; };

        AcceptButton = save;
        CancelButton = cancel;
        return ButtonBar.Create(ContentWidth, new Padding(0, Theme.S4, 0, Theme.S2), save, cancel);
    }

    // ------------------------------------------------------------- building blocks

    /// <summary>Вертикальный стек: одна колонка на всю доступную ширину.</summary>
    private static TableLayoutPanel Stack()
    {
        var stack = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        return stack;
    }

    private static Card NewCard(out TableLayoutPanel rows)
    {
        // Ширины у карточки нет: её растягивает колонка контента (якоря ставит
        // AddAutoRow). Dock ряда уважает Padding карточки — паддинги одинаковые
        // с обеих сторон по построению, а не по расчёту.
        //
        // Высота — НЕ AutoSize: он измеряет содержимое неограниченной шириной,
        // то есть до переноса строк, и карточка выходила ниже фактического
        // контента — подписи обрезало нижним краем. Берём фактическую высоту
        // рядов после раскладки.
        //
        // Ширину задаём СРАЗУ, хотя дальше её всё равно диктует колонка. До
        // первой раскладки у карточки ширина по умолчанию (200 px), ряды
        // докнуты в неё, и абзацы переносятся по двадцать раз — карточка
        // «вырастает» в несколько экранов. Высоту вкладок мы меряем именно в
        // этот момент (TallestPageHeight), и окно фиксировалось по этой
        // выдуманной высоте: содержимое потом сжималось до нормального, а под
        // ним оставалась пустота во весь экран.
        var card = new Card { Margin = new Padding(0), Width = ContentWidth };
        var r = Stack();
        r.Dock = DockStyle.Top;
        card.Controls.Add(r);
        r.SizeChanged += (_, _) => card.Height = r.Height + card.Padding.Vertical;
        // Подписка идёт после Add, и первое изменение размера рядов (само
        // добавление) обработчик уже не увидит. Высоту выставляем руками —
        // иначе карточка остаётся с дефолтной, пока ряды не изменятся ещё раз.
        card.Height = r.Height + card.Padding.Vertical;
        rows = r;
        return card;
    }

    private static Control Divider() => new Panel
    {
        Height = 1,
        BackColor = Theme.Border,
        Margin = new Padding(0, Theme.S2, 0, Theme.S2),
        // Ширина — от колонки, якоря поставит AddAutoRow.
    };

    /// <summary>
    /// Строка «заголовок + пояснение» слева, контрол справа.
    ///
    /// Левая колонка — процентная: подписи переносятся по фактически доступной
    /// ширине. Правая — AutoSize: колонка подстраивается под контрол, и обрезать
    /// его не может по построению. Прежняя разметка резервировала правой колонке
    /// абсолютную ширину, и чуть более широкий контрол срезало границей.
    ///
    /// <paramref name="onActivate"/> — что делает клик по самой строке. Галочка
    /// 20×20 — цель меньше, чем человек целится мышью; когда подпись объясняет
    /// контрол, она обязана и работать как этот контрол.
    /// </summary>
    private static TableLayoutPanel Row(string title, string hint, Control right,
        Action? onActivate = null)
    {
        var row = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, Theme.MinHitHeight),
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var text = Stack();
        var titleLabel = new Label
        {
            Text = title,
            Font = Theme.BodyStrong,
            ForeColor = Theme.Text,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 2),
            BackColor = Color.Transparent,
        };
        var hintLabel = new Label
        {
            Text = hint,
            Font = Theme.Caption,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            Margin = new Padding(0),
            BackColor = Color.Transparent,
        };
        AddAutoRow(text, titleLabel);
        AddAutoRow(text, hintLabel);
        text.Anchor = AnchorStyles.Left | AnchorStyles.Right;

        if (onActivate != null)
        {
            foreach (var c in new Control[] { titleLabel, hintLabel })
            {
                c.Cursor = Cursors.Hand;
                c.Click += (_, _) => onActivate();
            }
        }

        // Дистанция до подписи — отступом контрола, а не расчётом колонок.
        right.Anchor = AnchorStyles.Right;
        right.Margin = new Padding(Theme.S3, 0, 0, 0);

        row.Controls.Add(text, 0, 0);
        row.Controls.Add(right, 1, 0);
        return row;
    }

    private static Control CheckRow(CheckBox box, string title, string hint)
    {
        // ToggleBox рисует себя сам во весь свой прямоугольник — контрол 20×20,
        // прижатый к правому краю колонки, и есть галочка, без системных полей.
        box.Text = string.Empty;
        box.AutoSize = false;
        box.Size = new Size(20, 20);
        return Row(title, hint, box, () => box.Checked = !box.Checked);
    }

    private static Control HotkeyRow(string title, string hint, HotkeyDisplay display, Action record)
    {
        display.Size = new Size(HotkeyWidth, Theme.KeyRowHeight);
        display.Margin = new Padding(0, 0, Theme.S2, 0);
        display.Click += (_, _) => record();   // сами клавиши и есть кнопка «изменить»

        var btn = new FlatButton
        {
            Text = L10n.T("hotkey.change"),
            AutoSize = true,                       // ширину диктует текст, не константа
            MinimumSize = new Size(Theme.Px(92), 0),
            Margin = new Padding(0),
        };
        btn.Click += (_, _) => record();

        var group = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };
        group.Controls.Add(display);
        group.Controls.Add(btn);

        return Row(title, hint, group);
    }

    private static Control NumberRow(Stepper nud, string title, string unit, string hint)
    {
        // Единица измерения — внутри степпера («30 сек»), а не подписью рядом:
        // внешняя подпись сдвигала контрол влево, и правый край рядов в карточке
        // становился рваным — галочки прижаты, степперы нет.
        // Размер степпер считает сам от шрифта — см. Stepper.GetPreferredSize.
        nud.Suffix = unit;
        return Row(title, hint, nud);
    }

    // ------------------------------------------------------------------ data

    /// <summary>
    /// Смена языка применяется сразу: перезагружаем словарь и пересобираем окно,
    /// а не ждём сохранения и перезапуска. Настройка при этом всё равно
    /// записывается только по «Сохранить», как и остальные.
    /// </summary>
    private void OnLanguagePicked()
    {
        var picked = LanguageValues[_language.SelectedIndex];
        if (picked == _languagePref) return;
        _languagePref = picked;

        // «Авто» и явный язык могут разрешаться в один и тот же — тогда
        // перерисовывать нечего, но выбор всё равно надо запомнить.
        if (L10n.Resolve(picked) == L10n.Language) return;

        L10n.Init(picked);
        _onLanguageChanged?.Invoke();

        // Тексты сидят в уже созданных контролах, поэтому окно строим заново.
        // Значения полей переносим через Populate — они хранятся в самих
        // контролах, а хоткеи в полях формы.
        //
        // Controls.Clear() снимает старое дерево с формы, но НЕ уничтожает его:
        // Dispose здесь недопустим, потому что вместе с панелями он уничтожил
        // бы общие контролы формы (галочки, степперы, поля хоткеев), и окно
        // упало бы при следующем показе. Отвязанные панели соберёт GC.
        int tab = _tabIndex;
        SuspendLayout();
        Controls.Clear();
        BuildLayout();
        Populate();
        _tabs.SelectedIndex = tab;
        ShowPage(tab);
        ResumeLayout(true);
    }

    private int _tabIndex;

    private void Populate()
    {
        _cbEnabled.Checked = _settings.Enabled;
        // Автозапуск живёт в реестре, а не в settings.json: галочку могли поставить
        // в инсталляторе, и окно настроек обязано показывать её фактическое состояние.
        _cbAutostart.Checked = Autostart.IsEnabled();
        _cbAutoUpdate.Checked = _settings.AutoCheckUpdates;
        _cbCharByChar.Checked = _settings.CharByCharTyping;
        _cbVoiceLive.Checked = _settings.VoiceLiveText;
        // Отписываемся ДО присвоения: иначе Populate сам вызовет обработчик и
        // запустит пересборку окна по кругу.
        _language.SelectedIndexChanged -= LanguageChangedHandler;
        _language.SelectedIndex = Math.Max(0, Array.IndexOf(LanguageValues, _languagePref));
        _language.SelectedIndexChanged += LanguageChangedHandler;
        _nudIdle.Value = _settings.BufferIdleTimeoutSeconds;    // Stepper сам ограничит диапазоном
        _nudExpand.Value = _settings.ExpandWindowSeconds;
        _convertKeys.SetCombo(_convertHotkey.ToString());
        _caseKeys.SetCombo(_caseHotkey.ToString());
        _translateKeys.SetCombo(_translateHotkey.ToString());
        _voiceKeys.SetCombo(_voiceHotkey.ToString());
    }

    private void RecordInto(ref HotkeyConfig target, HotkeyDisplay display)
    {
        // Проверочный хук на время записи снимаем: два хука на одну клавиатуру
        // мешали бы друг другу — проверка глотала бы совпавшее сочетание раньше,
        // чем диалог успел бы его записать.
        _probe.Uninstall();
        try
        {
            using var dlg = new HotkeyRecordDialog();
            if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Result != null)
            {
                target = dlg.Result;
                display.SetCombo(target.ToString());
            }
        }
        finally
        {
            try { _probe.Install(); } catch { }
        }
    }

    private void LanguageChangedHandler(object? sender, EventArgs e) => OnLanguagePicked();

    private static HotkeyConfig Clone(HotkeyConfig h) => new()
    {
        Ctrl = h.Ctrl, Shift = h.Shift, Alt = h.Alt, Win = h.Win, Key = h.Key,
    };

    /// <summary>Переносит значения в настройки. false — сохранять нельзя, окно не закрываем.</summary>
    private bool ApplyToSettings()
    {
        // Одинаковые сочетания недопустимы: App проверяет раскладку первой, и
        // второй хоткей просто перестал бы отвечать — без единого признака.
        if (_convertHotkey.SameCombo(_caseHotkey)
            || _translateHotkey.SameCombo(_convertHotkey)
            || _translateHotkey.SameCombo(_caseHotkey)
            || _voiceHotkey.SameCombo(_convertHotkey)
            || _voiceHotkey.SameCombo(_caseHotkey)
            || _voiceHotkey.SameCombo(_translateHotkey))
        {
            Notice.Warn(this, L10n.T("hotkey.clash.title"),
                L10n.T("hotkey.clash.body"), L10n.T("hotkey.clash.hint"));
            return false;
        }

        _settings.Enabled = _cbEnabled.Checked;
        _settings.AutoCheckUpdates = _cbAutoUpdate.Checked;
        _settings.CharByCharTyping = _cbCharByChar.Checked;
        _settings.VoiceLiveText = _cbVoiceLive.Checked;
        _settings.Language = _languagePref;
        _settings.BufferIdleTimeoutSeconds = _nudIdle.Value;
        _settings.ExpandWindowSeconds = _nudExpand.Value;
        _settings.ConvertHotkey = _convertHotkey;
        _settings.ChangeCaseHotkey = _caseHotkey;
        _settings.TranslateHotkey = _translateHotkey;
        _settings.VoiceHotkey = _voiceHotkey;
        _settings.TranslationEnabled = Translator.IsReady;

        // Реестр — единственный источник правды для автозапуска.
        Autostart.Set(_cbAutostart.Checked);
        return true;
    }
}

/// <summary>
/// Диалог записи хоткея.
///
/// Слушает НАШ ЖЕ низкоуровневый хук, а не события WinForms. Это не
/// оптимизация, а единственный рабочий вариант: клавишу Win Windows форме не
/// отдаёт вовсе (её забирает оболочка), поэтому сочетание, заканчивающееся на
/// Win, через KeyDown записать нельзя — в лучшем случае получался огрызок без
/// Win, который потом молча не совпадал ни с одним нажатием.
///
/// Побочная выгода: записывается ровно то, что увидит HotkeyConfig.Matches —
/// тот же vk и те же флаги модификаторов из одного источника.
/// </summary>
public sealed class HotkeyRecordDialog : ThemedForm
{
    public HotkeyConfig? Result { get; private set; }

    private readonly HotkeyDisplay _preview = new();
    private readonly Label _hint = new();
    private readonly KeyboardHook _hook = new();

    /// <summary>
    /// Зажатые сейчас клавиши, посчитанные нами по нажатиям и отпусканиям.
    /// Спрашивать GetAsyncKeyState здесь нельзя: мы глотаем нажатия, а
    /// проглоченное хуком событие не обновляет состояние клавиш в системе —
    /// она отвечает «всё отпущено» сразу после первой же клавиши, и диалог
    /// закрывался, запомнив только её.
    /// </summary>
    private readonly HashSet<int> _down = new();

    private bool _ctrl, _shift, _alt, _win;
    private int _key;
    private bool _anyPressed;

    public HotkeyRecordDialog()
    {
        Text = L10n.T("record.title");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        var card = new Card
        {
            Width = Theme.Px(340),
            Height = Theme.Px(66),
            Margin = new Padding(0, 0, 0, Theme.S3),
        };
        _preview.Size = new Size(Theme.Px(340) - Theme.S3 * 2, Theme.KeyRowHeight);
        _preview.Location = new Point(Theme.S3, Theme.S3);
        _preview.SetCombo(string.Empty);
        card.Controls.Add(_preview);

        _hint.Text = L10n.T("record.hint");
        _hint.Font = Theme.Caption;
        _hint.ForeColor = Theme.TextMuted;
        _hint.AutoSize = true;
        _hint.MaximumSize = new Size(340, 0);
        _hint.Margin = new Padding(0);
        _hint.BackColor = Color.Transparent;

        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Canvas,
            Padding = new Padding(Theme.S4),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.Px(340)));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(card, 0, 0);
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(_hint, 0, 1);
        root.RowCount = 2;

        Controls.Add(root);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        _hook.KeyDown += OnHookKeyDown;
        _hook.KeyUp += OnHookKeyUp;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        try { _hook.Install(); }
        catch
        {
            _hint.Text = L10n.T("record.hookFailed");
            _hint.ForeColor = Theme.Danger;
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _hook.KeyDown -= OnHookKeyDown;
        _hook.KeyUp -= OnHookKeyUp;
        _hook.Dispose();
        base.OnFormClosed(e);
    }

    private static bool IsModifier(Keys k) =>
        k is Keys.ControlKey or Keys.LControlKey or Keys.RControlKey
          or Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey
          or Keys.Menu or Keys.LMenu or Keys.RMenu
          or Keys.LWin or Keys.RWin;

    private void OnHookKeyDown(object? sender, KeyboardHook.KeyEvent e)
    {
        // Пока окно записи открыто, наружу не уходит ничего: иначе тап Win
        // откроет меню «Пуск», а Alt уведёт фокус в строку меню.
        e.Handled = true;
        if (e.IsRepeat) return;

        // Esc без модификаторов — отмена, а не записываемое сочетание.
        if (e.VirtualKey == Keys.Escape && !e.Ctrl && !e.Shift && !e.Alt && !e.Win)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        _down.Add((int)e.VirtualKey);
        _anyPressed = true;
        if (e.Ctrl) _ctrl = true;
        if (e.Shift) _shift = true;
        if (e.Alt) _alt = true;
        if (e.Win) _win = true;
        if (!IsModifier(e.VirtualKey)) _key = (int)e.VirtualKey;

        _preview.SetCombo(Current().ToString());
    }

    private void OnHookKeyUp(object? sender, KeyboardHook.KeyEvent e)
    {
        // Отпускания тоже глотаем: нажатие мы уже съели, и «висячее» отпускание
        // приложение всё равно поймёт неправильно.
        e.Handled = true;
        _down.Remove((int)e.VirtualKey);
        TryCommit();
    }

    /// <summary>Фиксирует сочетание, когда пользователь отпустил все клавиши.</summary>
    private void TryCommit()
    {
        if (!_anyPressed || _down.Count > 0) return;
        if (!_ctrl && !_shift && !_alt && !_win && _key == 0) { Reset(); return; }

        // Alt+Shift — системный шорткат смены раскладки Windows: до нас он не дойдёт.
        if (_alt && _shift && !_ctrl && !_win)
        {
            Fail(L10n.T("record.altShift"));
            return;
        }

        Result = Current();
        DialogResult = DialogResult.OK;
        Close();
    }

    private void Fail(string message)
    {
        _hint.Text = message;
        _hint.ForeColor = Theme.Danger;   // ошибка обязана отличаться от подсказки цветом
        Reset();
    }

    private void Reset()
    {
        _ctrl = _shift = _alt = _win = false;
        _key = 0;
        _anyPressed = false;
        _down.Clear();
        _preview.SetCombo(string.Empty);
    }

    private HotkeyConfig Current() =>
        new() { Ctrl = _ctrl, Shift = _shift, Alt = _alt, Win = _win, Key = _key };
}
