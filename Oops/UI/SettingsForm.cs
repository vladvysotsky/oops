using System.Drawing;
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

    private readonly CheckBox _cbEnabled = new();
    private readonly CheckBox _cbAutostart = new();
    private readonly CheckBox _cbAutoUpdate = new();
    private readonly HotkeyDisplay _convertKeys = new() { Interactive = true };
    private readonly HotkeyDisplay _caseKeys = new() { Interactive = true };
    private readonly NumericUpDown _nudIdle = new();
    private readonly NumericUpDown _nudExpand = new();

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
    private const int ContentWidth = 520;

    /// <summary>Ширина внутренностей карточки (за вычетом её padding).</summary>
    private const int CardInnerWidth = ContentWidth - Theme.S3 * 2;

    // Сколько места резервирует правый контрол в строке. Нужно, чтобы ограничить
    // ширину подписей: без ограничения AutoSize-лейбл требует свою полную ширину
    // и выдавливает правую колонку за границу карточки.
    private const int ReservedCheck = 24;
    /// <summary>Ширина поля с клавишами. Хватает на три: «Ctrl+Alt+Shift».</summary>
    private const int HotkeyWidth = 190;
    private const int ReservedHotkey = HotkeyWidth + Theme.S2 + 92;  // + отступ + кнопка
    private const int ReservedNumber = 104;   // поле 64 + отступ 8 + подпись

    private void BuildLayout()
    {
        var content = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Canvas,
            Padding = new Padding(Theme.S4, Theme.S4, Theme.S4, Theme.S2),
            Margin = new Padding(0),
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ContentWidth));

        AddAutoRow(content, Header());
        AddAutoRow(content, SectionLabel("ОБЩИЕ"));
        AddAutoRow(content, GeneralCard());
        AddAutoRow(content, SectionLabel("ГОРЯЧИЕ КЛАВИШИ"));
        AddAutoRow(content, HotkeysCard());
        AddAutoRow(content, SectionLabel("ПОВЕДЕНИЕ"));
        AddAutoRow(content, BehaviourCard());
        AddAutoRow(content, SectionLabel("ПРОВЕРКА"));
        AddAutoRow(content, ProbeCard());
        AddAutoRow(content, Footer());

        Controls.Add(content);
    }

    private static void AddAutoRow(TableLayoutPanel host, Control child)
    {
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
            Text = "Правит раскладку и регистр набранного текста. Границу задаёте вы: "
                 + "каждое следующее нажатие хоткея захватывает ещё одно слово.",
            Font = Theme.Caption,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            MaximumSize = new Size(ContentWidth, 0),  // перенос по ширине колонки
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
        AddAutoRow(rows, CheckRow(_cbEnabled, "Включено",
            "Глобально включает и выключает горячие клавиши"));
        AddAutoRow(rows, Divider());
        AddAutoRow(rows, CheckRow(_cbAutostart, "Запускать при входе в Windows",
            "Иначе после перезагрузки придётся открывать вручную"));
        AddAutoRow(rows, Divider());
        AddAutoRow(rows, CheckRow(_cbAutoUpdate, "Проверять обновления",
            "Раз в сутки; о новой версии сообщим, ставить или нет — решаете вы"));
        return card;
    }

    private Control HotkeysCard()
    {
        var card = NewCard(out var rows);
        AddAutoRow(rows, HotkeyRow(
            "Раскладка", "Меняет RU ↔ EN",
            _convertKeys, () => RecordInto(ref _convertHotkey, _convertKeys)));
        AddAutoRow(rows, Divider());
        AddAutoRow(rows, HotkeyRow(
            "Регистр", "ВЕРХНИЙ ↔ нижний",
            _caseKeys, () => RecordInto(ref _caseHotkey, _caseKeys)));
        return card;
    }

    private Control BehaviourCard()
    {
        var card = NewCard(out var rows);

        _nudExpand.Minimum = 1; _nudExpand.Maximum = 10;
        AddAutoRow(rows, NumberRow(_nudExpand, "Второе нажатие засчитывается", "сек",
            "Успели нажать повторно — правится весь текст, не успели — снова последнее слово"));
        AddAutoRow(rows, Divider());

        _nudIdle.Minimum = 5; _nudIdle.Maximum = 600;
        AddAutoRow(rows, NumberRow(_nudIdle, "Забывать набранное через", "сек",
            "После паузы в наборе хоткей будет работать с новым текстом, а не с прежним"));
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

        _probeKeys.Size = new Size(ReservedHotkey, 30);
        _probeKeys.SetCombo(string.Empty);
        AddAutoRow(rows, Row(
            "Нажмите сочетание",
            "Здесь появится то, что дошло до oops",
            _probeKeys, ReservedHotkey));

        _probeStatus.Text = "Ждём нажатия…";
        _probeStatus.Font = Theme.Caption;
        _probeStatus.ForeColor = Theme.TextMuted;
        _probeStatus.AutoSize = true;
        _probeStatus.MaximumSize = new Size(CardInnerWidth, 0);
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
        catch { _probeStatus.Text = "Не удалось перехватить клавиатуру."; }
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
            _probeStatus.Text = "Совпадает с хоткеем раскладки — сработает.";
            _probeStatus.ForeColor = Theme.Accent;
        }
        else if (isCase)
        {
            _probeStatus.Text = "Совпадает с хоткеем регистра — сработает.";
            _probeStatus.ForeColor = Theme.Accent;
        }
        else
        {
            _probeStatus.Text = "Не совпадает ни с одним из назначенных сочетаний.";
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
        var save = new FlatButton { Text = "Сохранить", Primary = true, Size = new Size(124, 34), DialogResult = DialogResult.OK };
        var cancel = new FlatButton { Text = "Отмена", Size = new Size(104, 34), DialogResult = DialogResult.Cancel };
        // Button.OnClick выставляет DialogResult формы ДО вызова наших обработчиков,
        // поэтому вернуть None — штатный способ отменить закрытие окна.
        save.Click += (_, _) => { if (!ApplyToSettings()) DialogResult = DialogResult.None; };

        var flow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Right,
            BackColor = Theme.Canvas,
            Margin = new Padding(0, Theme.S4, 0, Theme.S2),
        };
        save.Margin = new Padding(Theme.S2, 0, 0, 0);
        cancel.Margin = new Padding(Theme.S2, 0, 0, 0);
        flow.Controls.Add(save);
        flow.Controls.Add(cancel);

        AcceptButton = save;
        CancelButton = cancel;
        return flow;
    }

    // ------------------------------------------------------------- building blocks

    /// <summary>Вертикальный стек с авторазмером — базовый строительный блок разметки.</summary>
    private static TableLayoutPanel Stack() => new()
    {
        ColumnCount = 1,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        BackColor = Color.Transparent,
        Margin = new Padding(0),
    };

    private static Card NewCard(out TableLayoutPanel rows)
    {
        var card = new Card
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0),
            Width = ContentWidth,
        };
        rows = Stack();
        rows.Width = ContentWidth - Theme.S3 * 2;
        card.Controls.Add(rows);
        return card;
    }

    private static Control Divider() => new Panel
    {
        Height = 1,
        Width = ContentWidth - Theme.S3 * 2,
        BackColor = Theme.Border,
        Margin = new Padding(0, Theme.S2, 0, Theme.S2),
    };

    /// <summary>
    /// Строка «заголовок + пояснение» слева, контрол справа.
    /// <paramref name="reservedRight"/> — сколько места занимает правый контрол;
    /// на эту величину сужается допустимая ширина подписей. Без такого лимита
    /// AutoSize-лейбл требует свою полную ширину и выталкивает контрол за границу
    /// карточки (текст не переносится, а строка становится шире карточки).
    ///
    /// <paramref name="onActivate"/> — что делает клик по самой строке. Галочка
    /// 20×20 — цель меньше, чем человек целится мышью; когда подпись объясняет
    /// контрол, она обязана и работать как этот контрол.
    /// </summary>
    private static TableLayoutPanel Row(string title, string hint, Control right, int reservedRight,
        Action? onActivate = null)
    {
        int textWidth = CardInnerWidth - reservedRight - Theme.S3;

        var row = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, Theme.MinHitHeight),
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Width = CardInnerWidth,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, textWidth + Theme.S3));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, reservedRight));
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var text = Stack();
        text.Anchor = AnchorStyles.Left;
        var titleLabel = new Label
        {
            Text = title,
            Font = Theme.BodyStrong,
            ForeColor = Theme.Text,
            AutoSize = true,
            MaximumSize = new Size(textWidth, 0),
            Margin = new Padding(0, 0, 0, 2),
            BackColor = Color.Transparent,
        };
        var hintLabel = new Label
        {
            Text = hint,
            Font = Theme.Caption,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            MaximumSize = new Size(textWidth, 0),
            Margin = new Padding(0),
            BackColor = Color.Transparent,
        };
        AddAutoRow(text, titleLabel);
        AddAutoRow(text, hintLabel);

        if (onActivate != null)
        {
            foreach (var c in new Control[] { titleLabel, hintLabel })
            {
                c.Cursor = Cursors.Hand;
                c.Click += (_, _) => onActivate();
            }
        }

        right.Anchor = AnchorStyles.Right;
        right.Margin = new Padding(0);

        row.Controls.Add(text, 0, 0);
        row.Controls.Add(right, 1, 0);
        return row;
    }

    private static Control CheckRow(CheckBox box, string title, string hint)
    {
        box.Text = string.Empty;
        box.AutoSize = false;
        box.Size = new Size(20, 20);
        box.BackColor = Color.Transparent;
        box.ForeColor = Theme.Text;
        box.Cursor = Cursors.Hand;
        return Row(title, hint, box, ReservedCheck, () => box.Checked = !box.Checked);
    }

    private static Control HotkeyRow(string title, string hint, HotkeyDisplay display, Action record)
    {
        display.Size = new Size(HotkeyWidth, 30);
        display.Margin = new Padding(0, 0, Theme.S2, 0);
        display.Click += (_, _) => record();   // сами клавиши и есть кнопка «изменить»

        var btn = new FlatButton { Text = "Изменить", Size = new Size(92, 30), Margin = new Padding(0) };
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

        return Row(title, hint, group, ReservedHotkey);
    }

    private static Control NumberRow(NumericUpDown nud, string title, string unit, string hint)
    {
        nud.Size = new Size(64, 26);
        nud.Font = Theme.Body;
        nud.BorderStyle = BorderStyle.FixedSingle;
        nud.TextAlign = HorizontalAlignment.Center;
        nud.Margin = new Padding(0, 2, Theme.S2, 0);
        // NumericUpDown не наследует цвета формы — в тёмной теме остался бы
        // белым прямоугольником с чёрным текстом посреди тёмной карточки.
        nud.BackColor = Theme.Surface;
        nud.ForeColor = Theme.Text;

        var group = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };
        group.Controls.Add(nud);
        group.Controls.Add(new Label
        {
            Text = unit,
            Font = Theme.Caption,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0),
            BackColor = Color.Transparent,
        });

        return Row(title, hint, group, ReservedNumber);
    }

    // ------------------------------------------------------------------ data

    private void Populate()
    {
        _cbEnabled.Checked = _settings.Enabled;
        // Автозапуск живёт в реестре, а не в settings.json: галочку могли поставить
        // в инсталляторе, и окно настроек обязано показывать её фактическое состояние.
        _cbAutostart.Checked = Autostart.IsEnabled();
        _cbAutoUpdate.Checked = _settings.AutoCheckUpdates;
        _nudIdle.Value = Math.Clamp(_settings.BufferIdleTimeoutSeconds, (int)_nudIdle.Minimum, (int)_nudIdle.Maximum);
        _nudExpand.Value = Math.Clamp(_settings.ExpandWindowSeconds, (int)_nudExpand.Minimum, (int)_nudExpand.Maximum);
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
            MessageBox.Show(this,
                "Раскладка и регистр не могут висеть на одном сочетании — "
                + "сработает только первое. Назначьте разные.",
                "oops", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        _settings.Enabled = _cbEnabled.Checked;
        _settings.AutoCheckUpdates = _cbAutoUpdate.Checked;
        _settings.BufferIdleTimeoutSeconds = (int)_nudIdle.Value;
        _settings.ExpandWindowSeconds = (int)_nudExpand.Value;
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
        Text = "Новое сочетание";
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

        _hint.Text = "Нажмите сочетание и отпустите клавиши. Можно из одних "
                   + "модификаторов (Ctrl + Alt + Win) или с обычной клавишей "
                   + "(Ctrl + Alt + X). Esc — отмена.";
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
            _hint.Text = "Не удалось перехватить клавиатуру. Закройте окно и попробуйте снова.";
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
            Fail("Alt+Shift занят Windows (смена раскладки). Выберите другое.");
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
