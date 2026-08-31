using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Oops.Core;

namespace Oops.UI;

/// <summary>
/// «Сообщить о проблеме или предложить идею» — по инициативе пользователя,
/// из меню трея.
///
/// Отправка НЕ идёт напрямую в API: для этого пришлось бы вшивать в клиент
/// токен (его вытащит кто угодно) или принимать анонимный текст на свой
/// сервер (его зальют спамом). Вместо этого текст дописывается в issue на
/// GitHub, уже заполненный: человек только жмёт «Submit» в браузере. Авторство
/// и защита от спама достаются от GitHub бесплатно.
/// </summary>
internal sealed class FeedbackForm : ThemedForm
{
    private static readonly int ContentWidth = Theme.Px(460);
    private static readonly int CardInnerWidth = ContentWidth - Theme.S3 * 2;

    private readonly RadioButton _rbProblem = new();
    private readonly RadioButton _rbIdea = new();
    private readonly TextBox _text = new();
    private readonly Label _hint = new();

    private FeedbackForm()
    {
        Text = L10n.T("feedback.window.title");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
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
            Text = L10n.T("feedback.title"),
            Font = Theme.Title,
            ForeColor = Theme.Text,
            AutoSize = true,
            MaximumSize = new Size(ContentWidth, 0),
            Margin = new Padding(0, 0, 0, Theme.S3),
            BackColor = Color.Transparent,
        });

        var card = new Card
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Width = ContentWidth,
            Margin = new Padding(0),
        };
        var rows = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Width = CardInnerWidth,
        };
        card.Controls.Add(rows);

        Style(_rbProblem, L10n.T("feedback.problem"));
        Style(_rbIdea, L10n.T("feedback.idea"));
        _rbProblem.Checked = true;
        Add(rows, _rbProblem);
        Add(rows, _rbIdea);

        _text.Multiline = true;
        _text.Height = Theme.Px(120);
        _text.Width = CardInnerWidth;
        _text.BorderStyle = BorderStyle.FixedSingle;
        _text.BackColor = Theme.Surface;
        _text.ForeColor = Theme.Text;
        _text.Font = Theme.Body;
        _text.ScrollBars = ScrollBars.Vertical;
        _text.Margin = new Padding(0, Theme.S2, 0, 0);
        _text.TextChanged += (_, _) => UpdateHint();
        Add(rows, _text);
        Add(root, card);

        _hint.Font = Theme.Caption;
        _hint.ForeColor = Theme.TextMuted;
        _hint.AutoSize = true;
        _hint.MaximumSize = new Size(ContentWidth, 0);
        _hint.Margin = new Padding(0, Theme.S2, 0, 0);
        _hint.BackColor = Color.Transparent;
        Add(root, _hint);
        UpdateHint();

        var send = new FlatButton
        {
            Text = L10n.T("feedback.submit"),
            Primary = true,
            AutoSize = true,
            MinimumSize = new Size(Theme.Px(124), 0),
        };
        send.Click += (_, _) => Send();

        var cancel = new FlatButton
        {
            Text = L10n.T("common.cancel"),
            AutoSize = true,
            MinimumSize = new Size(Theme.Px(104), 0),
            DialogResult = DialogResult.Cancel,
        };

        Add(root, ButtonBar.Create(ContentWidth, new Padding(0, Theme.S4, 0, 0), send, cancel));

        Controls.Add(root);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        CancelButton = cancel;
        ActiveControl = _text;
    }

    public static void ShowDialogFor()
    {
        using var form = new FeedbackForm();
        form.ShowDialog();
    }

    private static void Style(RadioButton rb, string text)
    {
        rb.Text = text;
        rb.Font = Theme.Body;
        rb.ForeColor = Theme.Text;
        rb.BackColor = Color.Transparent;
        rb.AutoSize = true;
        rb.Cursor = Cursors.Hand;
        rb.Margin = new Padding(0, 0, 0, Theme.S1);
    }

    private void UpdateHint()
    {
        _hint.Text = _text.Text.Trim().Length == 0
            ? L10n.T("feedback.hint.empty")
            : L10n.T("feedback.hint.ready");
    }

    private void Send()
    {
        var text = _text.Text.Trim();
        if (text.Length == 0)
        {
            _hint.Text = L10n.T("feedback.hint.required");
            _hint.ForeColor = Theme.Danger;
            return;
        }

        bool problem = _rbProblem.Checked;

        // Первая строка текста — в заголовок issue, чтобы список читался.
        var firstLine = text.Split('\n')[0].Trim();
        if (firstLine.Length > 80) firstLine = firstLine[..80] + "…";

        var body = text + "\n\n---\n"
                 + $"**Версия:** {UpdateService.CurrentVersion}\n"
                 + $"**Windows:** {Environment.OSVersion.VersionString}\n";

        var url = UpdateService.NewIssueUrl(
            firstLine, body, labels: problem ? "bug" : "enhancement");

        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            DialogResult = DialogResult.OK;
            Close();
        }
        catch
        {
            _hint.Text = L10n.T("feedback.browserFailed",
                UpdateService.ReleasesPageUrl.Replace("/releases", "/issues"));
            _hint.ForeColor = Theme.Danger;
        }
    }

    private static void Add(TableLayoutPanel host, Control child)
    {
        host.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        host.Controls.Add(child, 0, host.RowCount);
        host.RowCount++;
    }
}
