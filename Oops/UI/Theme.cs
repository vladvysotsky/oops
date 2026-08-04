using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Oops.UI;

/// <summary>
/// Дизайн-система приложения: палитра, типографика, сетка отступов, радиусы.
/// Значения выведены из иконки (синий → фиолетовый градиент) и держатся
/// на 8-пиксельной сетке. Контраст текста к фону — не ниже WCAG AA (4.5:1).
/// </summary>
internal static class Theme
{
    // --- Палитра ---------------------------------------------------------
    /// <summary>Фон окна.</summary>
    public static readonly Color Canvas = Color.FromArgb(0xF6, 0xF7, 0xF9);
    /// <summary>Фон карточки поверх Canvas.</summary>
    public static readonly Color Surface = Color.White;
    /// <summary>Граница карточек и полей.</summary>
    public static readonly Color Border = Color.FromArgb(0xE3, 0xE6, 0xEB);
    /// <summary>Основной текст. Контраст к Surface ≈ 15:1.</summary>
    public static readonly Color Text = Color.FromArgb(0x1A, 0x1D, 0x23);
    /// <summary>Второстепенный текст. Контраст к Surface ≈ 5.4:1.</summary>
    public static readonly Color TextMuted = Color.FromArgb(0x66, 0x6D, 0x7A);
    /// <summary>Акцент — из иконки.</summary>
    public static readonly Color Accent = Color.FromArgb(0x2D, 0x6C, 0xDF);
    public static readonly Color AccentHover = Color.FromArgb(0x24, 0x5B, 0xC2);
    public static readonly Color AccentPressed = Color.FromArgb(0x1C, 0x4A, 0xA3);
    /// <summary>Заливка «клавиши» в поле хоткея.</summary>
    public static readonly Color KeyCapFill = Color.FromArgb(0xF1, 0xF3, 0xF7);
    public static readonly Color KeyCapBorder = Color.FromArgb(0xD3, 0xD8, 0xE0);

    // --- Типографика -----------------------------------------------------
    private const string Family = "Segoe UI";
    public static readonly Font Title = new(Family, 13.5f, FontStyle.Regular);
    public static readonly Font SectionLabel = new(Family, 8.25f, FontStyle.Bold);
    public static readonly Font Body = new(Family, 9.75f, FontStyle.Regular);
    public static readonly Font BodyStrong = new(Family, 9.75f, FontStyle.Bold);
    public static readonly Font Caption = new(Family, 8.5f, FontStyle.Regular);
    public static readonly Font KeyCap = new("Consolas", 10f, FontStyle.Bold);

    // --- Сетка (8px) -----------------------------------------------------
    public const int S1 = 4;
    public const int S2 = 8;
    public const int S3 = 16;
    public const int S4 = 24;
    public const int S5 = 32;

    public const int Radius = 8;

    /// <summary>Скруглённый прямоугольник для отрисовки карточек и кнопок.</summary>
    public static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        if (d <= 0) { path.AddRectangle(r); return path; }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void EnableSmoothing(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
    }
}

/// <summary>Карточка: белая поверхность со скруглением и тонкой границей.</summary>
internal sealed class Card : Panel
{
    public Card()
    {
        BackColor = Theme.Surface;
        DoubleBuffered = true;
        Padding = new Padding(Theme.S3);
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Theme.Canvas);
        Theme.EnableSmoothing(e.Graphics);
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.RoundedRect(r, Theme.Radius);
        using var fill = new SolidBrush(Theme.Surface);
        using var pen = new Pen(Theme.Border);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(pen, path);
    }
}

/// <summary>Кнопка в стиле дизайн-системы: заливка акцентом либо «тихий» вариант.</summary>
internal sealed class FlatButton : Button
{
    private bool _hover, _pressed;

    public bool Primary { get; init; }

    public FlatButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        Font = Theme.Body;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Canvas);
        Theme.EnableSmoothing(g);

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.RoundedRect(r, 6);

        Color back, fore;
        if (Primary)
        {
            back = _pressed ? Theme.AccentPressed : _hover ? Theme.AccentHover : Theme.Accent;
            fore = Color.White;
            using var fill = new SolidBrush(back);
            g.FillPath(fill, path);
        }
        else
        {
            back = _pressed ? Theme.Border : _hover ? Theme.KeyCapFill : Theme.Surface;
            fore = Theme.Text;
            using var fill = new SolidBrush(back);
            using var pen = new Pen(Theme.Border);
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }

        TextRenderer.DrawText(g, Text, Font, r, fore,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>
/// Поле хоткея: рисует комбинацию «клавишами» вместо строки текста —
/// пользователь видит сочетание так же, как оно выглядит на клавиатуре.
/// </summary>
internal sealed class HotkeyDisplay : Control
{
    private string[] _keys = Array.Empty<string>();

    public HotkeyDisplay()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
    }

    /// <summary>Комбинация вида "Ctrl+Win" — разбирается на отдельные клавиши.</summary>
    public void SetCombo(string combo)
    {
        _keys = string.IsNullOrWhiteSpace(combo)
            ? Array.Empty<string>()
            : combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Surface);
        Theme.EnableSmoothing(g);

        if (_keys.Length == 0)
        {
            TextRenderer.DrawText(g, "не задан", Theme.Body, ClientRectangle, Theme.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            return;
        }

        int x = 0;
        for (int i = 0; i < _keys.Length; i++)
        {
            var size = TextRenderer.MeasureText(g, _keys[i], Theme.KeyCap);
            int w = size.Width + Theme.S2 + Theme.S1;
            int h = Height - Theme.S1 * 2;
            var cap = new Rectangle(x, Theme.S1, w, h);

            using (var path = Theme.RoundedRect(cap, 5))
            using (var fill = new SolidBrush(Theme.KeyCapFill))
            using (var pen = new Pen(Theme.KeyCapBorder))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, _keys[i], Theme.KeyCap, cap, Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            x += w;
            if (i < _keys.Length - 1)
            {
                var plus = new Rectangle(x, 0, Theme.S3, Height);
                TextRenderer.DrawText(g, "+", Theme.Body, plus, Theme.TextMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                x += Theme.S3;
            }
        }
    }
}
