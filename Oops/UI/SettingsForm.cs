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
    private readonly ComboBox _cbLanguage = new();
    private readonly HotkeyDisplay _convertKeys = new() { Interactive = true };
    private readonly HotkeyDisplay _caseKeys = new() { Interactive = true };
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

    public SettingsForm(AppSettings settings)
    {
        _settings = settings;
        _convertHotkey = Clone(settings.ConvertHotkey);
        _caseHotkey = Clone(settings.ChangeCaseHotkey);

        Text = "oops";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        // Фон, шрифт, DPI и тёмный заголовок окна приходят из ThemedForm.

        BuildLayout();
        Populate();

        // Подгоняем окно под фактическую высоту содержимого, чтобы не появлялась прокрутка.
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
    }

    // ---------------------------------------------------------------- layout

    /// <summary>Ширина колонки контента (карточки, заголовки). Масштабируется системой по DPI.</summary>
    private const int ContentWidth = 580;

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
    private const int HotkeyWidth = 220;

    private void BuildLayout()
    {
        var content = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Canvas,
            // Снизу S3, а не S2: вместе с отступом футера получается 24 —
            // столько же, сколько сверху и по бокам. Раньше низ был тоньше.
            Padding = new Padding(Theme.S4, Theme.S4, Theme.S4, Theme.S3),
            Margin = new Padding(0),
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ContentWidth));

        AddAutoRow(content, Header());
        AddAutoRow(content, SectionLabel(L10n.T("settings.section.general")));
        AddAutoRow(content, GeneralCard());
        AddAutoRow(content, SectionLabel(L10n.T("settings.section.hotkeys")));
        AddAutoRow(content, HotkeysCard());
        AddAutoRow(content, SectionLabel(L10n.T("settings.section.behaviour")));
        AddAutoRow(content, BehaviourCard());
        AddAutoRow(content, SectionLabel(L10n.T("settings.section.probe")));
        AddAutoRow(content, ProbeCard());
        AddAutoRow(content, Footer());

        Controls.Add(content);
    }

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

    private static Control SectionLabel(string text) => new Label
    {
        Text = text,
        Font = Theme.SectionLabel,
        ForeColor = Theme.TextMuted,
        AutoSize = true,
        Margin = new Padding(Theme.S1, Theme.S3, 0, Theme.S2),
        BackColor = Color.Transparent,
    };

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
    /// Выбор языка интерфейса. Список короткий и фиксированный, поэтому
    /// системный ComboBox в DropDownList — он не даёт вводить произвольный
    /// текст и не требует своей отрисовки.
    /// </summary>
    private Control LanguageRow()
    {
        _cbLanguage.DropDownStyle = ComboBoxStyle.DropDownList;
        _cbLanguage.FlatStyle = FlatStyle.Flat;
        _cbLanguage.Font = Theme.Body;
        _cbLanguage.BackColor = Theme.Surface;
        _cbLanguage.ForeColor = Theme.Text;
        _cbLanguage.Items.Clear();
        var languages = new object[] { L10n.T("settings.language.auto"), "Русский", "English" };
        _cbLanguage.Items.AddRange(languages);

        // Ширина — по самому длинному пункту, а не константой: «Same as Windows»
        // в английской локали не влезал в прежние 160 и обрезался. Запас — на
        // стрелку списка и внутренние поля.
        int widest = languages.Max(item =>
            TextRenderer.MeasureText(item!.ToString(), Theme.Body).Width);
        _cbLanguage.Width = widest + Theme.S5 + Theme.S2;
        return Row(L10n.T("settings.language"), L10n.T("settings.language.hint"), _cbLanguage);
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

        _probeKeys.Height = 34;
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
        _probe.KeyDown += OnProbeKey;
        try { _probe.Install(); }
        catch { _probeStatus.Text = L10n.T("probe.hookFailed"); }
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

        // Совпавшее сочетание глотаем: иначе Win откроет «Пуск» прямо из окна
        // настроек. Всё остальное пропускаем — окном надо пользоваться.
        if (isConvert || isCase) e.Handled = true;

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
        var save = new FlatButton { Text = L10n.T("common.save"), Primary = true, AutoSize = true, MinimumSize = new Size(124, 34), DialogResult = DialogResult.OK };
        var cancel = new FlatButton { Text = L10n.T("common.cancel"), AutoSize = true, MinimumSize = new Size(104, 34), DialogResult = DialogResult.Cancel };
        // Button.OnClick выставляет DialogResult формы ДО вызова наших обработчиков,
        // поэтому вернуть None — штатный способ отменить закрытие окна.
        save.Click += (_, _) => { if (!ApplyToSettings()) DialogResult = DialogResult.None; };

        var flow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Canvas,
            Margin = new Padding(0),
        };
        save.Margin = new Padding(Theme.S2, 0, 0, 0);
        cancel.Margin = new Padding(Theme.S2, 0, 0, 0);
        flow.Controls.Add(save);
        flow.Controls.Add(cancel);

        // Подвал во всю ширину: пустая тянущаяся колонка слева, кнопки в
        // AutoSize-колонке справа. Прежний Anchor = Right позиционировал панель,
        // но не заставлял её родителя быть нужной ширины — правый край кнопок
        // уезжал за границу окна.
        var bar = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Canvas,
            Margin = new Padding(0, Theme.S4, 0, Theme.S2),
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        bar.Controls.Add(new Panel { Width = 0, Height = 0, Margin = new Padding(0) }, 0, 0);
        bar.Controls.Add(flow, 1, 0);

        AcceptButton = save;
        CancelButton = cancel;
        return bar;
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
        var card = new Card { Margin = new Padding(0) };
        var r = Stack();
        r.Dock = DockStyle.Top;
        card.Controls.Add(r);
        r.SizeChanged += (_, _) => card.Height = r.Height + card.Padding.Vertical;
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
        display.Size = new Size(HotkeyWidth, 30);
        display.Margin = new Padding(0, 0, Theme.S2, 0);
        display.Click += (_, _) => record();   // сами клавиши и есть кнопка «изменить»

        var btn = new FlatButton
        {
            Text = L10n.T("hotkey.change"),
            AutoSize = true,                       // ширину диктует текст, не константа
            MinimumSize = new Size(92, 30),
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
        nud.Suffix = unit;
        nud.Size = new Size(132, 30);
        return Row(title, hint, nud);
    }

    // ------------------------------------------------------------------ data

    private void Populate()
    {
        _cbEnabled.Checked = _settings.Enabled;
        // Автозапуск живёт в реестре, а не в settings.json: галочку могли поставить
        // в инсталляторе, и окно настроек обязано показывать её фактическое состояние.
        _cbAutostart.Checked = Autostart.IsEnabled();
        _cbAutoUpdate.Checked = _settings.AutoCheckUpdates;
        _cbCharByChar.Checked = _settings.CharByCharTyping;
        _cbLanguage.SelectedIndex = Math.Max(0, Array.IndexOf(LanguageValues, _settings.Language));
        _nudIdle.Value = _settings.BufferIdleTimeoutSeconds;    // Stepper сам ограничит диапазоном
        _nudExpand.Value = _settings.ExpandWindowSeconds;
        _convertKeys.SetCombo(_convertHotkey.ToString());
        _caseKeys.SetCombo(_caseHotkey.ToString());
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

    private static HotkeyConfig Clone(HotkeyConfig h) => new()
    {
        Ctrl = h.Ctrl, Shift = h.Shift, Alt = h.Alt, Win = h.Win, Key = h.Key,
    };

    /// <summary>Переносит значения в настройки. false — сохранять нельзя, окно не закрываем.</summary>
    private bool ApplyToSettings()
    {
        // Одинаковые сочетания недопустимы: App проверяет раскладку первой, и
        // второй хоткей просто перестал бы отвечать — без единого признака.
        if (_convertHotkey.SameCombo(_caseHotkey))
        {
            Notice.Warn(this, L10n.T("hotkey.clash.title"),
                L10n.T("hotkey.clash.body"), L10n.T("hotkey.clash.hint"));
            return false;
        }

        _settings.Enabled = _cbEnabled.Checked;
        _settings.AutoCheckUpdates = _cbAutoUpdate.Checked;
        _settings.CharByCharTyping = _cbCharByChar.Checked;
        if (_cbLanguage.SelectedIndex >= 0)
            _settings.Language = LanguageValues[_cbLanguage.SelectedIndex];
        _settings.BufferIdleTimeoutSeconds = _nudIdle.Value;
        _settings.ExpandWindowSeconds = _nudExpand.Value;
        _settings.ConvertHotkey = _convertHotkey;
        _settings.ChangeCaseHotkey = _caseHotkey;

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
            Width = 340,
            Height = 66,
            Margin = new Padding(0, 0, 0, Theme.S3),
        };
        _preview.Size = new Size(340 - Theme.S3 * 2, 34);
        _preview.Location = new Point(Theme.S3, 16);
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
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));
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
