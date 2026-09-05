using System.Drawing;
using System.Windows.Forms;
using Oops.Core;

namespace Oops.UI;

/// <summary>
/// Плашка «идёт запись» внизу экрана: единственный видимый признак того, что
/// программа слушает, и заодно живая расшифровка по ходу речи.
///
/// Подсказки у иконки в трее оказалось мало: её не видно, пока не наведёшь
/// мышь, а во время диктовки мышь в другом месте. Человек нажимал хоткей и не
/// понимал, началось что-нибудь или нет.
///
/// Окно НЕ ЗАБИРАЕТ ФОКУС — иначе печатать было бы некуда: текст уходит в то
/// поле, которое было активным до нажатия хоткея. Отсюда ShowWithoutActivation
/// и WS_EX_NOACTIVATE; WS_EX_TOOLWINDOW убирает плашку из Alt+Tab.
/// </summary>
internal sealed class VoiceOverlay : Form
{
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;

    private static VoiceOverlay? _instance;

    private readonly Label _state = new();
    private readonly Label _text = new();

    private VoiceOverlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.Surface;
        Width = Theme.Px(560);

        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Surface,
            Padding = new Padding(Theme.S3),
            Dock = DockStyle.Fill,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Width - Theme.S3 * 2));

        _state.Font = Theme.BodyStrong;
        _state.ForeColor = Theme.Danger;      // красный = «идёт запись», как везде
        _state.AutoSize = true;
        _state.Margin = new Padding(0, 0, 0, Theme.S1);
        _state.BackColor = Color.Transparent;

        _text.Font = Theme.Body;
        _text.ForeColor = Theme.Text;
        _text.AutoSize = true;
        _text.MaximumSize = new Size(Width - Theme.S3 * 2, 0);
        _text.Margin = new Padding(0);
        _text.BackColor = Color.Transparent;

        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(_state, 0, 0);
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(_text, 0, 1);
        Controls.Add(root);

        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WsExNoActivate | WsExToolWindow;
            return cp;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    // ------------------------------------------------------------------ API

    /// <summary>Показывает плашку «слушаю». Вызывать только из UI-потока.</summary>
    public static void Listening()
    {
        var form = Ensure();
        form._state.Text = L10n.T("voice.overlay.listening");
        form._state.ForeColor = Theme.Danger;
        form._text.Text = L10n.T("voice.overlay.hint");
        form._text.ForeColor = Theme.TextMuted;
        form.Reposition();
        if (!form.Visible) form.Show();
    }

    /// <summary>Живая расшифровка по ходу речи.</summary>
    public static void Partial(string text)
    {
        if (_instance is not { Visible: true } form) return;
        form._text.Text = string.IsNullOrWhiteSpace(text) ? L10n.T("voice.overlay.hint") : text;
        form._text.ForeColor = string.IsNullOrWhiteSpace(text) ? Theme.TextMuted : Theme.Text;
        form.Reposition();
    }

    /// <summary>Речь закончилась, идёт последний проход.</summary>
    public static void Recognising()
    {
        if (_instance is not { Visible: true } form) return;
        form._state.Text = L10n.T("voice.overlay.recognising");
        form._state.ForeColor = Theme.TextMuted;
        form.Reposition();
    }

    public static void Hide()
    {
        if (_instance is { Visible: true } form) form.Visible = false;
    }

    public static void Close()
    {
        _instance?.Dispose();
        _instance = null;
    }

    private static VoiceOverlay Ensure() => _instance ??= new VoiceOverlay();

    /// <summary>
    /// Внизу по центру рабочей области. Высота плашки меняется вместе с длиной
    /// расшифровки, поэтому позицию пересчитываем на каждое обновление — иначе
    /// растущий текст уезжал бы под панель задач.
    /// </summary>
    private void Reposition()
    {
        var work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        Location = new Point(
            work.Left + (work.Width - Width) / 2,
            work.Bottom - Height - Theme.S5);
    }
}
