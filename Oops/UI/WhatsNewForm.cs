using System.Drawing;
using System.Windows.Forms;
using Oops.Core;
using Oops.Settings;

namespace Oops.UI;

/// <summary>
/// «Что нового» — после обновления и по требованию из меню трея.
///
/// Программа живёт в трее и обновляется молча: без такого окна человек узнаёт
/// о новой функции только случайно, а хоткей, о котором он не знает, ничем не
/// отличается от отсутствующего.
///
/// Слева — все версии от первой, справа — что появилось в выбранной. Каждый
/// пункт отвечает не «что сделано», а «как этим пользоваться», и рядом стоит
/// ФАКТИЧЕСКОЕ сочетание из настроек: человек мог его поменять, и показывать
/// ему чужое было бы худшим способом объяснить функцию.
/// </summary>
internal sealed class WhatsNewForm : ThemedForm
{
    private static readonly int SidebarWidth = Theme.Px(150);
    private static readonly int DetailWidth = Theme.Px(520);
    private static readonly int ContentWidth = SidebarWidth + Theme.S3 + DetailWidth;

    /// <summary>
    /// Высота рабочей области окна. Фиксированная НАМЕРЕННО: у версий разное
    /// число пунктов, и подгонка под выбранную заставляла бы окно прыгать при
    /// каждом клике по списку. Не поместилось — прокручивается, как в любом
    /// читалке списка изменений.
    /// </summary>
    private static readonly int BodyHeight = Theme.Px(420);

    private readonly AppSettings _settings;
    private readonly Panel _detail = new();
    private readonly List<FlatButton> _tabs = new();
    private readonly IReadOnlyList<ChangeSet> _sets;

    private WhatsNewForm(AppSettings settings, bool afterUpdate)
    {
        _settings = settings;
        _sets = Changelog.All;

        Text = L10n.T("news.window.title");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;      // окно самостоятельное, а не диалог поверх другого
        StartPosition = FormStartPosition.CenterScreen;

        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Canvas,
            Padding = new Padding(Theme.S4),
            Margin = new Padding(0),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ContentWidth));

        Add(root, new Label
        {
            Text = afterUpdate
                ? L10n.T("news.header.updated", UpdateService.CurrentVersion.ToString(3))
                : L10n.T("news.header.plain"),
            Font = Theme.Title,
            ForeColor = Theme.Text,
            AutoSize = true,
            MaximumSize = new Size(ContentWidth, 0),
            Margin = new Padding(0, 0, 0, Theme.S1),
            BackColor = Color.Transparent,
        });

        Add(root, new Label
        {
            Text = L10n.T("news.header.hint"),
            Font = Theme.Caption,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            MaximumSize = new Size(ContentWidth, 0),
            Margin = new Padding(0, 0, 0, Theme.S3),
            BackColor = Color.Transparent,
        });

        Add(root, Body());

        var ok = new FlatButton
        {
            Text = L10n.T("notice.ok"),
            Primary = true,
            AutoSize = true,
            MinimumSize = new Size(Theme.Px(120), 0),
            DialogResult = DialogResult.OK,
        };
        Add(root, ButtonBar.Create(ContentWidth, new Padding(0, Theme.S4, 0, 0), ok));

        Controls.Add(root);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        AcceptButton = ok;
        CancelButton = ok;

        Select(0);      // свежая версия сверху и открыта по умолчанию
    }

    // ------------------------------------------------------------------ API

    /// <summary>Открывает окно по требованию — из меню трея.</summary>
    public static void ShowAll(AppSettings settings)
    {
        using var form = new WhatsNewForm(settings, afterUpdate: false);
        form.ShowDialog();
    }

    /// <summary>
    /// Показывает окно, если программу обновили с прошлого запуска, и
    /// запоминает текущую версию.
    ///
    /// На свежей установке НЕ показывает: там своё дело делает мастер первого
    /// запуска, а вывалить на нового человека историю версий — верный способ
    /// заставить его закрыть оба окна не читая.
    /// </summary>
    public static void ShowIfUpdated(AppSettings settings)
    {
        var current = UpdateService.CurrentVersion;
        var seen = Version.TryParse(settings.LastSeenVersion, out var v) ? v : null;

        // Версию запоминаем в любом случае — иначе окно всплывало бы при
        // каждом запуске, пока настройки не сохранят по другому поводу.
        settings.LastSeenVersion = current.ToString();
        settings.Save();

        if (seen == null) return;                        // первая установка
        if (Changelog.Since(seen).Count == 0) return;    // обновления не было

        using var form = new WhatsNewForm(settings, afterUpdate: true);
        form.ShowDialog();
    }

    // -------------------------------------------------------------- вёрстка

    private Control Body()
    {
        var body = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = false,
            Width = ContentWidth,
            Height = BodyHeight,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, SidebarWidth + Theme.S3));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, DetailWidth));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        body.Controls.Add(Sidebar(), 0, 0);

        _detail.AutoScroll = true;
        _detail.HandleCreated += (_, _) => Theme.ApplyScrollbarChrome(_detail);
        _detail.BackColor = Theme.Canvas;
        _detail.Dock = DockStyle.Fill;
        _detail.Margin = new Padding(0);
        body.Controls.Add(_detail, 1, 0);

        return body;
    }

    private Control Sidebar()
    {
        var host = new Panel
        {
            AutoScroll = true,
            BackColor = Theme.Canvas,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, Theme.S3, 0),
        };
        host.HandleCreated += (_, _) => Theme.ApplyScrollbarChrome(host);

        // Ширина ЗА ВЫЧЕТОМ вертикальной полосы. Версий в списке всегда больше,
        // чем влезает, поэтому полоса есть всегда и забирает свои пиксели у
        // клиентской области: список во всю ширину переставал помещаться, и
        // снизу вылезала вторая, горизонтальная — она обрезала нижнюю версию.
        // В правой панели этот вычет был с самого начала, в левой забыли.
        int inner = SidebarWidth - SystemInformation.VerticalScrollBarWidth;

        var stack = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Canvas,
            Margin = new Padding(0),
            Width = inner,
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        for (int i = 0; i < _sets.Count; i++)
        {
            int index = i;
            // Кнопка, а не свой контрол списка: выделение уже умеет рисовать
            // FlatButton заливкой акцентом, и проверять новый контрол на всех
            // масштабах экрана пришлось бы заново.
            var tab = new FlatButton
            {
                Text = _sets[i].Version.ToString(3),
                AutoSize = false,
                Width = inner,
                Height = Theme.TextRowHeight,
                Margin = new Padding(0, 0, 0, Theme.S1),
            };
            tab.Click += (_, _) => Select(index);
            _tabs.Add(tab);
            Add(stack, tab);
        }

        host.Controls.Add(stack);
        return host;
    }

    /// <summary>Показывает выбранную версию и подсвечивает её в списке.</summary>
    private void Select(int index)
    {
        for (int i = 0; i < _tabs.Count; i++) _tabs[i].Primary = i == index;

        _detail.SuspendLayout();
        // Прежнее содержимое именно уничтожаем: оставленные контролы копились
        // бы при каждом переключении и рисовались друг под другом — ровно то,
        // на чём уже погорели вкладки настроек.
        foreach (Control old in _detail.Controls.Cast<Control>().ToList()) old.Dispose();
        _detail.Controls.Clear();

        int inner = DetailWidth - SystemInformation.VerticalScrollBarWidth;
        var stack = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Canvas,
            Margin = new Padding(0),
            Width = inner,
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        foreach (var entry in _sets[index].Entries) Add(stack, EntryCard(entry, inner));

        _detail.Controls.Add(stack);
        _detail.AutoScrollPosition = Point.Empty;   // новая версия читается с начала
        _detail.ResumeLayout(true);
    }

    private Control EntryCard(ChangeEntry entry, int width)
    {
        var card = new Card { Margin = new Padding(0, 0, 0, Theme.S2), Width = width };
        var rows = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Dock = DockStyle.Top,
        };
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        card.Controls.Add(rows);
        rows.SizeChanged += (_, _) => card.Height = rows.Height + card.Padding.Vertical;
        card.Height = rows.Height + card.Padding.Vertical;

        int inner = width - Theme.S3 * 2;

        Add(rows, new Label
        {
            Text = L10n.T(entry.TitleKey),
            Font = Theme.BodyStrong,
            ForeColor = Theme.Text,
            AutoSize = true,
            MaximumSize = new Size(inner, 0),
            Margin = new Padding(0, 0, 0, Theme.S1),
            BackColor = Color.Transparent,
        });

        var combo = Combo(entry.Hotkey);
        if (combo != null)
        {
            var keys = new HotkeyDisplay
            {
                Size = new Size(Theme.Px(220), Theme.KeyRowHeight),
                Margin = new Padding(0, 0, 0, Theme.S1),
            };
            keys.SetCombo(combo);
            Add(rows, keys, stretch: false);
        }

        Add(rows, new Label
        {
            Text = L10n.T(entry.BodyKey),
            Font = Theme.Caption,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            MaximumSize = new Size(inner, 0),
            Margin = new Padding(0),
            BackColor = Color.Transparent,
        });
        return card;
    }

    /// <summary>Фактическое сочетание из настроек, а не умолчание.</summary>
    private string? Combo(HotkeyRef which) => which switch
    {
        HotkeyRef.Convert => _settings.ConvertHotkey.ToString(),
        HotkeyRef.Case => _settings.ChangeCaseHotkey.ToString(),
        HotkeyRef.Translate => _settings.TranslateHotkey.ToString(),
        HotkeyRef.Voice => _settings.VoiceHotkey.ToString(),
        HotkeyRef.Replace => _settings.ReplaceHotkey.ToString(),
        _ => null,
    };

    /// <param name="stretch">
    /// Растянуть на ширину колонки. Для «клавиш» хоткея — нет: у них своя
    /// ширина, и растянутые они превращаются в полосу во всю карточку.
    /// </param>
    private static void Add(TableLayoutPanel host, Control child, bool stretch = true)
    {
        if (stretch && child.Anchor == (AnchorStyles.Top | AnchorStyles.Left))
            child.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        host.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        host.Controls.Add(child, 0, host.RowCount);
        host.RowCount++;
    }
}
