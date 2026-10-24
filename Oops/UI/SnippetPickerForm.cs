using System.Drawing;
using System.Windows.Forms;
using Oops.Core;

namespace Oops.UI;

/// <summary>
/// Список сниппетов по хоткею: выбрать и вставить, не помня сокращения.
///
/// Сокращение хорошо, когда сниппетов три. Когда их двадцать, половину
/// забываешь — и тогда нужен список с поиском. Поиск идёт и по сокращению,
/// и по тексту; сокращение сравнивается так же, как при наборе, — по
/// клавишам, так что «!уу» найдёт «!ee».
///
/// Управление целиком с клавиатуры: печатаешь — список сужается, стрелки
/// двигают выбор, Enter вставляет, Esc закрывает. Руки с клавиатуры снимать
/// не нужно, ради этого хоткей и вызывался.
/// </summary>
internal sealed class SnippetPickerForm : ThemedForm
{
    private static readonly int ContentWidth = Theme.Px(480);

    private readonly IReadOnlyList<Snippet> _all;
    private readonly TextBox _search = new();
    private readonly ListBox _list = new();
    private List<Snippet> _shown = new();
    private string? _chosen;

    private SnippetPickerForm(IReadOnlyList<Snippet> snippets)
    {
        _all = snippets;

        Text = L10n.T("picker.window.title");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

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

        _search.Width = ContentWidth;
        _search.BorderStyle = BorderStyle.FixedSingle;
        _search.BackColor = Theme.Surface;
        _search.ForeColor = Theme.Text;
        _search.Font = Theme.Body;
        _search.PlaceholderText = L10n.T("picker.search");
        _search.Margin = new Padding(0, 0, 0, Theme.S2);
        _search.TextChanged += (_, _) => Filter();
        Add(root, _search);

        _list.Width = ContentWidth;
        _list.Height = Theme.Px(260);
        _list.IntegralHeight = false;
        _list.BorderStyle = BorderStyle.FixedSingle;
        _list.BackColor = Theme.Surface;
        _list.ForeColor = Theme.Text;
        _list.Font = Theme.Body;
        _list.Margin = new Padding(0);
        _list.DrawMode = DrawMode.OwnerDrawFixed;
        _list.ItemHeight = TextRenderer.MeasureText("Ag", Theme.Body).Height + Theme.S2;
        _list.DrawItem += DrawItem;
        _list.DoubleClick += (_, _) => Accept();
        _list.HandleCreated += (_, _) => Theme.ApplyScrollbarChrome(_list);
        Add(root, _list);

        Add(root, new Label
        {
            Text = L10n.T("picker.hint"),
            Font = Theme.Caption,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            MaximumSize = new Size(ContentWidth, 0),
            Margin = new Padding(Theme.S1, Theme.S2, 0, 0),
            BackColor = Color.Transparent,
        });

        Controls.Add(root);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        ActiveControl = _search;

        Filter();
    }

    /// <summary>
    /// Показывает список. Возвращает текст выбранного сниппета или null, если
    /// человек передумал.
    /// </summary>
    public static string? Pick(IReadOnlyList<Snippet> snippets)
    {
        using var form = new SnippetPickerForm(snippets);
        form.ShowDialog();
        return form._chosen;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Открыт хоткеем из чужого окна: без явной активации фокус может
        // остаться там, и набранное ушло бы мимо поиска.
        Activate();
        _search.Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Escape:
                Close();
                e.Handled = true;
                break;
            case Keys.Enter:
                Accept();
                e.Handled = e.SuppressKeyPress = true;
                break;
            // SuppressKeyPress, а не только Handled: иначе список, когда фокус
            // на нём, сдвинул бы выбор ещё раз сам.
            case Keys.Down:
                Move(+1);
                e.Handled = e.SuppressKeyPress = true;
                break;
            case Keys.Up:
                Move(-1);
                e.Handled = e.SuppressKeyPress = true;
                break;
        }
        base.OnKeyDown(e);
    }

    private void Move(int delta)
    {
        if (_shown.Count == 0) return;
        _list.SelectedIndex = Math.Clamp(_list.SelectedIndex + delta, 0, _shown.Count - 1);
    }

    private void Accept()
    {
        if (_list.SelectedIndex < 0 || _list.SelectedIndex >= _shown.Count) return;
        _chosen = _shown[_list.SelectedIndex].Text;
        Close();
    }

    private void Filter()
    {
        _shown = _all.Where(s => SnippetMatcher.Fits(s, _search.Text, LayoutConverter.Pair)).ToList();
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var s in _shown) _list.Items.Add(s.Trigger);
        _list.EndUpdate();
        if (_shown.Count > 0) _list.SelectedIndex = 0;
    }

    /// <summary>Строка списка: сокращение моноширинным, рядом начало текста.</summary>
    private void DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _shown.Count) return;
        var s = _shown[e.Index];
        bool selected = (e.State & DrawItemState.Selected) != 0;

        using (var bg = new SolidBrush(selected ? Theme.Accent : Theme.Surface))
            e.Graphics.FillRectangle(bg, e.Bounds);

        var fore = selected ? Color.White : Theme.Text;
        var muted = selected ? Color.White : Theme.TextMuted;
        int pad = Theme.S2;
        int triggerWidth = Theme.Px(120);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                    | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;

        var triggerBox = new Rectangle(e.Bounds.X + pad, e.Bounds.Y, triggerWidth, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, s.Trigger, Theme.Mono, triggerBox, fore, flags);

        // Только первая строка текста: список — чтобы узнать сниппет, а не прочитать.
        var preview = s.Text.Replace("\r", "").Split('\n')[0];
        var textBox = new Rectangle(triggerBox.Right + pad, e.Bounds.Y,
            e.Bounds.Right - triggerBox.Right - pad * 2, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, preview, Theme.Body, textBox, muted, flags);
    }

    private static void Add(TableLayoutPanel host, Control child)
    {
        host.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        host.Controls.Add(child, 0, host.RowCount);
        host.RowCount++;
    }
}
