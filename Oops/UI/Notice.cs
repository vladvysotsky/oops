using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Oops.Core;

namespace Oops.UI;

/// <summary>Что за сообщение — от этого зависит цвет метки и её знак.</summary>
internal enum NoticeKind { Info, Warning, Error }

/// <summary>
/// Окно сообщения в дизайн-системе приложения — вместо системного MessageBox.
///
/// Дело не только во внешнем виде. MessageBox не умеет трёх вещей, без которых
/// сообщение об ошибке бесполезно:
///   - сказать, ЧТО ДЕЛАТЬ, отдельно от того, что случилось;
///   - спрятать техническую подробность так, чтобы её можно было скопировать,
///     но она не пугала того, кому не нужна;
///   - увести в issue с уже заполненным текстом, а не оставить человека
///     пересказывать ошибку своими словами.
///
/// Он к тому же игнорирует тёмную тему и рисуется системным светлым окном
/// посреди тёмного интерфейса.
/// </summary>
internal sealed class Notice : ThemedForm
{
    private const int ContentWidth = 420;

    private readonly string? _details;
    private Control? _detailsBox;

    private Notice(NoticeKind kind, string title, string message, string? hint,
                   string? details, string? reportContext)
    {
        _details = details;

        Text = "oops";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

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

        Add(root, HeaderRow(kind, title));
        Add(root, Text(message, Theme.Body, Theme.Text, new Padding(0, Theme.S2, 0, 0)));

        // «Что делать» отделено от «что случилось» намеренно: человеку нужно
        // действие, а не диагноз. Без этой строки любое сообщение об ошибке
        // оставляет в тупике.
        if (!string.IsNullOrWhiteSpace(hint))
            Add(root, Text(hint, Theme.Caption, Theme.TextMuted, new Padding(0, Theme.S2, 0, 0)));

        if (!string.IsNullOrWhiteSpace(details))
        {
            var toggle = new LinkLabel
            {
                Text = "Подробности",
                Font = Theme.Caption,
                LinkColor = Theme.Accent,
                ActiveLinkColor = Theme.AccentPressed,
                LinkBehavior = LinkBehavior.HoverUnderline,
                AutoSize = true,
                Margin = new Padding(0, Theme.S3, 0, 0),
                BackColor = Color.Transparent,
            };

            _detailsBox = DetailsBox(details!);
            _detailsBox.Visible = false;
            toggle.LinkClicked += (_, _) =>
            {
                _detailsBox.Visible = !_detailsBox.Visible;
                toggle.Text = _detailsBox.Visible ? "Скрыть подробности" : "Подробности";
            };

            Add(root, toggle);
            Add(root, _detailsBox);
        }

        Add(root, Buttons(reportContext, details));

        Controls.Add(root);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
    }

    // ------------------------------------------------------------------ API

    public static void Info(IWin32Window? owner, string title, string message, string? hint = null)
        => Show(owner, NoticeKind.Info, title, message, hint, null, null);

    public static void Warn(IWin32Window? owner, string title, string message, string? hint = null)
        => Show(owner, NoticeKind.Warning, title, message, hint, null, null);

    /// <param name="details">Техническая подробность — прячется под «Подробности».</param>
    /// <param name="reportContext">Если задан, появляется кнопка «Сообщить об ошибке».</param>
    public static void Error(IWin32Window? owner, string title, string message,
        string? hint = null, string? details = null, string? reportContext = null)
        => Show(owner, NoticeKind.Error, title, message, hint, details, reportContext);

    /// <summary>
    /// Необработанное исключение. Показывается вместо системного окна .NET со
    /// стеком: то сообщает пользователю ровно ничего и не даёт нам ничего взамен.
    /// </summary>
    public static void Crash(Exception ex)
    {
        Show(null, NoticeKind.Error,
            "oops неожиданно сломался",
            "Произошла ошибка, которую программа не предусмотрела. "
            + "Горячие клавиши могли перестать работать — перезапустите программу.",
            "Если это повторяется, отправьте отчёт: он придёт с описанием ошибки, "
            + "нам не придётся угадывать.",
            ex.ToString(),
            reportContext: "Необработанное исключение");
    }

    private static void Show(IWin32Window? owner, NoticeKind kind, string title,
        string message, string? hint, string? details, string? reportContext)
    {
        using var form = new Notice(kind, title, message, hint, details, reportContext);
        if (owner != null) form.ShowDialog(owner);
        else { form.StartPosition = FormStartPosition.CenterScreen; form.ShowDialog(); }
    }

    // -------------------------------------------------------------- вёрстка

    private static void Add(TableLayoutPanel host, Control child)
    {
        host.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        host.Controls.Add(child, 0, host.RowCount);
        host.RowCount++;
    }

    private static Control HeaderRow(NoticeKind kind, string title)
    {
        var row = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Width = ContentWidth,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ContentWidth - 36));

        row.Controls.Add(new Badge(kind) { Size = new Size(24, 24), Margin = new Padding(0, 2, Theme.S2, 0) }, 0, 0);
        row.Controls.Add(new Label
        {
            Text = title,
            Font = Theme.Title,
            ForeColor = Theme.Text,
            AutoSize = true,
            MaximumSize = new Size(ContentWidth - 36, 0),
            Margin = new Padding(0),
            BackColor = Color.Transparent,
        }, 1, 0);
        return row;
    }

    private static Control Text(string text, Font font, Color color, Padding margin) => new Label
    {
        Text = text,
        Font = font,
        ForeColor = color,
        AutoSize = true,
        MaximumSize = new Size(ContentWidth, 0),
        Margin = margin,
        BackColor = Color.Transparent,
    };

    private static Control DetailsBox(string details) => new TextBox
    {
        Text = details.Replace("\r\n", "\n").Replace("\n", Environment.NewLine),
        Multiline = true,
        ReadOnly = true,
        WordWrap = false,
        ScrollBars = ScrollBars.Both,
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = Theme.Surface,
        ForeColor = Theme.TextMuted,
        Font = Theme.Mono,
        Width = ContentWidth,
        Height = 140,
        Margin = new Padding(0, Theme.S2, 0, 0),
    };

    private Control Buttons(string? reportContext, string? details)
    {
        var flow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Right,
            BackColor = Theme.Canvas,
            Margin = new Padding(0, Theme.S4, 0, 0),
        };

        var ok = new FlatButton
        {
            Text = "Понятно",
            Primary = true,
            Size = new Size(112, 34),
            Margin = new Padding(Theme.S2, 0, 0, 0),
            DialogResult = DialogResult.OK,
        };
        flow.Controls.Add(ok);
        AcceptButton = ok;
        CancelButton = ok;

        if (reportContext != null)
        {
            var report = new FlatButton
            {
                Text = "Сообщить об ошибке",
                Size = new Size(168, 34),
                Margin = new Padding(Theme.S2, 0, 0, 0),
            };
            report.Click += (_, _) => OpenIssue(reportContext, details);
            flow.Controls.Add(report);
        }

        if (details != null)
        {
            var copy = new FlatButton
            {
                Text = "Скопировать",
                Size = new Size(124, 34),
                Margin = new Padding(Theme.S2, 0, 0, 0),
            };
            copy.Click += (_, _) =>
            {
                // Единственное место, кроме чтения выделения, где мы пишем в буфер
                // обмена — и только по явному нажатию кнопки самим пользователем.
                try { Clipboard.SetText(details); copy.Text = "Скопировано"; }
                catch { copy.Text = "Не вышло"; }
            };
            flow.Controls.Add(copy);
        }

        return flow;
    }

    private static void OpenIssue(string context, string? details)
    {
        var body = $"**Версия:** {UpdateService.CurrentVersion}\n"
                 + $"**Windows:** {Environment.OSVersion.VersionString}\n\n"
                 + "**Что я делал:**\n\n\n";
        if (!string.IsNullOrEmpty(details))
            body += "**Подробности:**\n```\n" + Truncate(details, 4000) + "\n```\n";

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = UpdateService.NewIssueUrl(context, body),
                UseShellExecute = true,
            });
        }
        catch { }
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "\n… обрезано";

    /// <summary>Круглая метка со знаком: цвет несёт смысл, знак дублирует его формой.</summary>
    private sealed class Badge : Control
    {
        private readonly NoticeKind _kind;

        public Badge(NoticeKind kind)
        {
            _kind = kind;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Canvas);
            Theme.EnableSmoothing(g);

            var color = _kind switch
            {
                NoticeKind.Error => Theme.Danger,
                NoticeKind.Warning => Theme.Warning,
                _ => Theme.Accent,
            };
            // Знак дублирует цвет: на цвет полагаться нельзя — дальтонизм,
            // высокая контрастность, чёрно-белый скриншот в переписке.
            var glyph = _kind == NoticeKind.Info ? "i" : "!";

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var fill = new SolidBrush(color)) g.FillEllipse(fill, r);
            TextRenderer.DrawText(g, glyph, Theme.BodyStrong, r, Theme.OnAccent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
