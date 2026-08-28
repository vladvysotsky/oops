using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Oops.UI;

/// <summary>
/// Дизайн-система приложения: палитра, типографика, сетка отступов, радиусы.
///
/// Палитра — свойства, а не константы: она подстраивается под систему.
///   - тёмная тема Windows (AppsUseLightTheme = 0) даёт тёмный набор цветов;
///   - режим высокой контрастности отдаёт цвета системе целиком, иначе наши
///     мягкие серые превращают окно в нечитаемое пятно ровно для тех, кому
///     контраст и нужен.
/// Значения читаются один раз при старте: WinForms всё равно не перерисует
/// уже созданные контролы, а смена темы на лету — не тот случай, ради которого
/// стоит городить перерисовку всего дерева.
///
/// Контраст текста к фону — не ниже WCAG AA (4.5:1) в обоих наборах.
/// </summary>
internal static class Theme
{
    private static readonly bool Dark = DetectDarkMode();
    private static readonly bool Contrast = SystemInformation.HighContrast;

    /// <summary>Приложение рисует себя в тёмном наборе цветов.</summary>
    public static bool IsDark => Dark && !Contrast;

    private static bool DetectDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    /// <summary>Выбор цвета: высокая контрастность → системный, иначе наш светлый/тёмный.</summary>
    private static Color Pick(Color light, Color dark, Color system) =>
        Contrast ? system : (Dark ? dark : light);

    // --- Палитра ---------------------------------------------------------
    /// <summary>Фон окна.</summary>
    public static Color Canvas => Pick(
        Color.FromArgb(0xF6, 0xF7, 0xF9), Color.FromArgb(0x16, 0x18, 0x1D), SystemColors.Control);
    /// <summary>Фон карточки поверх Canvas.</summary>
    public static Color Surface => Pick(
        Color.White, Color.FromArgb(0x1E, 0x21, 0x28), SystemColors.Window);
    /// <summary>Граница карточек и полей.</summary>
    public static Color Border => Pick(
        Color.FromArgb(0xE3, 0xE6, 0xEB), Color.FromArgb(0x2E, 0x32, 0x3B), SystemColors.WindowFrame);
    /// <summary>Основной текст.</summary>
    public static Color Text => Pick(
        Color.FromArgb(0x1A, 0x1D, 0x23), Color.FromArgb(0xE8, 0xEA, 0xED), SystemColors.WindowText);
    /// <summary>Второстепенный текст.</summary>
    public static Color TextMuted => Pick(
        Color.FromArgb(0x66, 0x6D, 0x7A), Color.FromArgb(0x9B, 0xA3, 0xAF), SystemColors.GrayText);
    /// <summary>Акцент — из иконки. В тёмной теме светлее: тот же синий на тёмном фоне не читается.</summary>
    public static Color Accent => Pick(
        Color.FromArgb(0x2D, 0x6C, 0xDF), Color.FromArgb(0x4C, 0x8D, 0xFF), SystemColors.Highlight);
    public static Color AccentHover => Pick(
        Color.FromArgb(0x24, 0x5B, 0xC2), Color.FromArgb(0x6B, 0xA1, 0xFF), SystemColors.Highlight);
    public static Color AccentPressed => Pick(
        Color.FromArgb(0x1C, 0x4A, 0xA3), Color.FromArgb(0x3D, 0x77, 0xE0), SystemColors.Highlight);
    /// <summary>Текст на заливке акцентом.</summary>
    public static Color OnAccent => Contrast ? SystemColors.HighlightText : Color.White;
    /// <summary>
    /// Ошибка и предупреждение. Отдельный цвет обязателен: раньше сообщения об
    /// ошибке красились в AccentPressed — тёмно-синий читается как обычный текст,
    /// и «не удалось обновить» ничем не отличалось от подсказки.
    /// </summary>
    public static Color Danger => Pick(
        Color.FromArgb(0xC0, 0x32, 0x26), Color.FromArgb(0xFF, 0x7B, 0x6E), SystemColors.WindowText);
    /// <summary>Предупреждение — не отказ, но требует внимания.</summary>
    public static Color Warning => Pick(
        Color.FromArgb(0xB5, 0x6B, 0x00), Color.FromArgb(0xF0, 0xA8, 0x3C), SystemColors.WindowText);
    /// <summary>Заливка «клавиши» в поле хоткея.</summary>
    public static Color KeyCapFill => Pick(
        Color.FromArgb(0xF1, 0xF3, 0xF7), Color.FromArgb(0x26, 0x2A, 0x32), SystemColors.Control);
    public static Color KeyCapBorder => Pick(
        Color.FromArgb(0xD3, 0xD8, 0xE0), Color.FromArgb(0x3A, 0x3F, 0x4A), SystemColors.WindowFrame);

    /// <summary>Цвет тени карточек. В тёмной теме гуще — иначе её просто не видно.</summary>
    public static Color Shadow => Dark
        ? Color.FromArgb(0x00, 0x00, 0x00)
        : Color.FromArgb(0x0F, 0x17, 0x2A);
    private static int ShadowAlpha => Contrast ? 0 : (Dark ? 90 : 26);

    // --- Типографика -----------------------------------------------------
    // Системный шрифт платформы намеренно: у него уже настроены оптические
    // размеры, трекинг и хинтинг — своя гарнитура должна была бы это чем-то
    // окупать. Иерархия строится размером И начертанием, а не размером одним.
    private const string Family = "Segoe UI";
    public static readonly Font Title = new(Family, 13.5f, FontStyle.Regular);
    public static readonly Font SectionLabel = new(Family, 8.25f, FontStyle.Bold);
    public static readonly Font Body = new(Family, 9.75f, FontStyle.Regular);
    public static readonly Font BodyStrong = new(Family, 9.75f, FontStyle.Bold);
    public static readonly Font Caption = new(Family, 8.5f, FontStyle.Regular);
    public static readonly Font KeyCap = new("Consolas", 10f, FontStyle.Bold);
    /// <summary>Моноширинный для примеров «до/после»: там важно выравнивание колонок.</summary>
    public static readonly Font Mono = new("Consolas", 9f, FontStyle.Regular);

    // --- Сетка (8px) -----------------------------------------------------
    public const int S1 = 4;
    public const int S2 = 8;
    public const int S3 = 16;
    public const int S4 = 24;
    public const int S5 = 32;

    public const int Radius = 8;

    /// <summary>
    /// Минимальная высота кликабельной строки. Галочка 20×20 — это меньше, чем
    /// человек целится мышью; строку целиком делаем зоной попадания.
    /// </summary>
    public const int MinHitHeight = 32;

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

    /// <summary>
    /// Рисует мягкую тень под скруглённым прямоугольником: несколько контуров
    /// с убывающей альфой. Сплошная рамка — самый частый способ «обозначить
    /// карточку», и самый плоский; полупрозрачная тень даёт слой, а не линию.
    /// </summary>
    public static void DrawSoftShadow(Graphics g, Rectangle body, int spread)
    {
        if (ShadowAlpha == 0) return;
        for (int i = spread; i >= 1; i--)
        {
            var r = new Rectangle(body.X, body.Y + i, body.Width, body.Height);
            using var path = RoundedRect(r, Radius);
            using var pen = new Pen(Color.FromArgb(ShadowAlpha / i, Shadow));
            g.DrawPath(pen, path);
        }
    }

    // --- Оформление окна -------------------------------------------------

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>
    /// Красит заголовок окна в тёмный. Без этого в тёмной теме Windows окно
    /// выглядит склеенным из двух половин: светлая рамка над тёмным содержимым.
    /// Атрибут 20 — DWMWA_USE_IMMERSIVE_DARK_MODE (Windows 10 2004+); на более
    /// старых сборках вызов просто вернёт ошибку, и мы её игнорируем.
    /// </summary>
    public static void ApplyWindowChrome(Form form)
    {
        if (!IsDark) return;
        try
        {
            int on = 1;
            DwmSetWindowAttribute(form.Handle, 20, ref on, sizeof(int));
        }
        catch { }
    }

    /// <summary>
    /// Одевает меню трея в тему приложения. Меню — самая заметная поверхность
    /// программы, и светлое меню в тёмной Windows выдаёт приложение сильнее,
    /// чем что-либо ещё в окне настроек.
    /// </summary>
    public static void ApplyMenuChrome(ToolStrip menu)
    {
        if (!IsDark) return;
        menu.RenderMode = ToolStripRenderMode.Professional;
        menu.Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors());
        menu.BackColor = Surface;
        menu.ForeColor = Text;
        foreach (ToolStripItem item in menu.Items)
        {
            item.BackColor = Surface;
            item.ForeColor = item is ToolStripSeparator ? Border : Text;
        }
    }

    private sealed class DarkMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Surface;
        public override Color MenuItemSelected => KeyCapFill;
        public override Color MenuItemSelectedGradientBegin => KeyCapFill;
        public override Color MenuItemSelectedGradientEnd => KeyCapFill;
        public override Color MenuItemBorder => Border;
        public override Color MenuBorder => Border;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Border;
        public override Color CheckBackground => Accent;
        public override Color CheckSelectedBackground => Accent;
    }
}

/// <summary>
/// Базовая форма приложения: фон, шрифт и тёмный заголовок окна в одном месте.
/// Публичная намеренно — от неё наследуются публичные окна.
/// </summary>
public class ThemedForm : Form
{
    public ThemedForm()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Theme.Canvas;
        ForeColor = Theme.Text;
        Font = Theme.Body;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyWindowChrome(this);
    }
}

/// <summary>Карточка: поверхность со скруглением, тонкой границей и мягкой тенью.</summary>
internal sealed class Card : Panel
{
    /// <summary>Сколько пикселей внизу занимает тень — на них уменьшается тело карточки.</summary>
    public const int ShadowRoom = 3;

    public Card()
    {
        BackColor = Theme.Surface;
        DoubleBuffered = true;
        Padding = new Padding(Theme.S3, Theme.S3, Theme.S3, Theme.S3 + ShadowRoom);
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Canvas);
        Theme.EnableSmoothing(g);

        var body = new Rectangle(0, 0, Width - 1, Height - ShadowRoom - 1);
        Theme.DrawSoftShadow(g, body, ShadowRoom);

        using var path = Theme.RoundedRect(body, Theme.Radius);
        using var fill = new SolidBrush(Theme.Surface);
        using var pen = new Pen(Theme.Border);
        g.FillPath(fill, path);
        g.DrawPath(pen, path);
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
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    /// <summary>
    /// Кнопка сама знает свою ширину: текст + горизонтальные поля. Фиксированный
    /// размер, заданный снаружи, при чуть более длинной надписи прижимал текст
    /// к рамке фокуса — выглядело как «не помещается». Используется при
    /// AutoSize = true; MinimumSize держит одинаковую высоту и минимальную
    /// ширину коротких кнопок.
    /// </summary>
    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = TextRenderer.MeasureText(Text, Font);
        return new Size(text.Width + Theme.S3 * 2, Math.Max(34, text.Height + Theme.S2 * 2));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Canvas);
        Theme.EnableSmoothing(g);

        // Нажатие сдвигает кнопку на пиксель вниз. Отклик обязан быть на
        // прижатии, а не на отпускании, и одного изменения цвета мало —
        // смещение читается как физическое «продавили».
        int drop = _pressed ? 1 : 0;
        var r = new Rectangle(0, drop, Width - 1, Height - 1 - drop);
        using var path = Theme.RoundedRect(r, 6);

        Color fore;
        if (Primary)
        {
            var back = _pressed ? Theme.AccentPressed : _hover ? Theme.AccentHover : Theme.Accent;
            fore = Theme.OnAccent;
            using var fill = new SolidBrush(back);
            g.FillPath(fill, path);
        }
        else
        {
            var back = _pressed ? Theme.Border : _hover ? Theme.KeyCapFill : Theme.Surface;
            fore = Theme.Text;
            using var fill = new SolidBrush(back);
            using var pen = new Pen(Theme.Border);
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }

        // Фокус с клавиатуры обязан быть виден: UserPaint отключает системную
        // рамку фокуса, и без своей окно нельзя пройти табом.
        // Сплошное кольцо, не пунктир: системный пунктир прижимался к тексту
        // и выглядел как грязная рамка «текст не помещается».
        if (Focused && ShowFocusCues)
        {
            var ring = Rectangle.Inflate(r, -2, -2);
            using var ringPath = Theme.RoundedRect(ring, 5);
            using var ringPen = new Pen(
                Primary ? Color.FromArgb(0xA0, Theme.OnAccent) : Theme.Accent, 1f);
            g.DrawPath(ringPen, ringPath);
        }

        TextRenderer.DrawText(g, Text, Font, r, fore,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>
/// Числовое поле «− значение +» вместо системного NumericUpDown: тот рисует
/// белое поле с крошечными системными стрелками и не перекрашивается — в
/// тёмной теме выпадал из дизайна, а в стрелки 8×8 ещё и попасть трудно.
/// Кнопки-зоны здесь во всю высоту контрола.
/// </summary>
internal sealed class Stepper : Control
{
    public int Minimum { get; set; }
    public int Maximum { get; set; } = 100;

    private int _value;
    public int Value
    {
        get => _value;
        set { _value = Math.Clamp(value, Minimum, Maximum); Invalidate(); }
    }

    private int _hoverZone; // -1 минус, +1 плюс, 0 — никакая

    public Stepper()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
               | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Theme.Surface;
        TabStop = true;
        Size = new Size(108, 30);
    }

    private int ZoneWidth => Height;   // квадратные зоны по краям

    private int ZoneAt(int x) => x < ZoneWidth ? -1 : (x >= Width - ZoneWidth ? +1 : 0);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var z = ZoneAt(e.X);
        if (z != _hoverZone) { _hoverZone = z; Invalidate(); }
        Cursor = z == 0 ? Cursors.Default : Cursors.Hand;
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (_hoverZone != 0) { _hoverZone = 0; Invalidate(); }
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        var z = ZoneAt(e.X);
        if (z != 0) Value += z;
        base.OnMouseDown(e);
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Up or Keys.Down or Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Up or Keys.Right) { Value += 1; e.Handled = true; }
        else if (e.KeyCode is Keys.Down or Keys.Left) { Value -= 1; e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        Theme.EnableSmoothing(g);

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Theme.RoundedRect(r, 6))
        using (var fill = new SolidBrush(Theme.Surface))
        using (var pen = new Pen(Focused ? Theme.Accent : Theme.Border))
        {
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }

        var minus = new Rectangle(0, 0, ZoneWidth, Height);
        var plus = new Rectangle(Width - ZoneWidth, 0, ZoneWidth, Height);

        DrawZone(g, minus, "−", _hoverZone == -1, Value > Minimum);
        DrawZone(g, plus, "+", _hoverZone == +1, Value < Maximum);

        var mid = new Rectangle(ZoneWidth, 0, Width - ZoneWidth * 2, Height);
        TextRenderer.DrawText(g, Value.ToString(), Theme.BodyStrong, mid, Theme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private static void DrawZone(Graphics g, Rectangle zone, string glyph, bool hover, bool enabled)
    {
        if (hover && enabled)
        {
            var pad = Rectangle.Inflate(zone, -3, -3);
            using var path = Theme.RoundedRect(pad, 4);
            using var fill = new SolidBrush(Theme.KeyCapFill);
            g.FillPath(fill, path);
        }
        TextRenderer.DrawText(g, glyph, Theme.Body, zone,
            enabled ? Theme.Text : Theme.TextMuted,
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
    private bool _hover;

    /// <summary>
    /// Поле само открывает запись по клику. Контрол, который влияет на что-то,
    /// должен быть рядом с тем, на что влияет — а лучше им и быть: тыкать в
    /// показанные клавиши естественнее, чем искать кнопку «Изменить».
    /// </summary>
    public bool Interactive { get; init; }

    public HotkeyDisplay()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (Interactive) Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        if (Interactive) { _hover = true; Invalidate(); }
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (_hover) { _hover = false; Invalidate(); }
        base.OnMouseLeave(e);
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
            using (var pen = new Pen(_hover ? Theme.Accent : Theme.KeyCapBorder))
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
