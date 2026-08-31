using System.Drawing;
using System.Windows.Forms;
using Oops.Core;

namespace Oops.UI;

/// <summary>
/// Окно «доступно обновление»: версия, описание релиза и кнопка установки.
/// Во время загрузки показывает прогресс вместо кнопок.
/// </summary>
public sealed class UpdateDialog : ThemedForm
{
    private const int ContentWidth = 460;

    private readonly ReleaseInfo _release;
    private readonly FlatButton _install;
    private readonly FlatButton _later;
    private readonly ProgressBar _progress = new();
    private readonly Label _status = new();
    private CancellationTokenSource? _cts;

    public UpdateDialog(ReleaseInfo release)
    {
        _release = release;

        Text = L10n.T("update.window.title");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        // Фон, шрифт, DPI и тёмный заголовок окна приходят из ThemedForm.

        _install = new FlatButton { Text = L10n.T("update.install"), Primary = true, AutoSize = true, MinimumSize = new Size(124, 34) };
        _later = new FlatButton { Text = L10n.T("update.later"), AutoSize = true, MinimumSize = new Size(104, 34), DialogResult = DialogResult.Cancel };
        _install.Click += OnInstallClick;

        BuildLayout();

        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        CancelButton = _later;
        // Стартовый фокус — на главной кнопке, а не на первом попавшемся контроле.
        ActiveControl = _install;
    }

    private void BuildLayout()
    {
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

        AddRow(root, new Label
        {
            Text = L10n.T("update.availableVersion", _release.Version),
            Font = Theme.Title,
            ForeColor = Theme.Text,
            AutoSize = true,
            MaximumSize = new Size(ContentWidth, 0),
            Margin = new Padding(0, 0, 0, Theme.S1),
            BackColor = Color.Transparent,
        });

        AddRow(root, new Label
        {
            Text = L10n.T("update.installedVersion", UpdateService.CurrentVersion),
            Font = Theme.Caption,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, Theme.S3),
            BackColor = Color.Transparent,
        });

        var pretty = PrettifyNotes(_release.Notes);
        if (pretty.Length > 0)
        {
            var card = new Card
            {
                Width = ContentWidth,
                Height = 150,
                Margin = new Padding(0, 0, 0, Theme.S3),
            };
            var notes = new TextBox
            {
                Text = pretty.Replace("\n", Environment.NewLine),
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Theme.Surface,
                ForeColor = Theme.TextMuted,
                Font = Theme.Caption,
                Dock = DockStyle.Fill,
                // Не отдавать полю фокус при открытии: это первый контрол формы,
                // WinForms фокусирует его автоматически, а TextBox под фокусом
                // выделяет весь текст — окно открывалось с «синей простынёй».
                // Выделять и копировать мышью по-прежнему можно.
                TabStop = false,
            };
            // TabStop страхует не от всего: у единственного TabStop-контрола
            // WinForms всё равно может оказаться выделение. Снимаем его явно.
            notes.GotFocus += (_, _) => notes.SelectionLength = 0;
            card.Controls.Add(notes);
            AddRow(root, card);
        }

        _progress.Style = ProgressBarStyle.Continuous;
        _progress.Width = ContentWidth;
        _progress.Height = 6;
        _progress.Visible = false;
        _progress.Margin = new Padding(0, 0, 0, Theme.S2);
        AddRow(root, _progress);

        _status.Text = string.Empty;
        _status.Font = Theme.Caption;
        _status.ForeColor = Theme.TextMuted;
        _status.AutoSize = true;
        _status.MaximumSize = new Size(ContentWidth, 0);
        _status.Margin = new Padding(0);
        _status.BackColor = Color.Transparent;
        AddRow(root, _status);

        AddRow(root, ButtonBar.Create(
            new Padding(0, Theme.S3, 0, 0), _install, _later));

        Controls.Add(root);
    }

    private static void AddRow(TableLayoutPanel host, Control child)
    {
        host.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        host.Controls.Add(child, 0, host.RowCount);
        host.RowCount++;
    }

    /// <summary>
    /// Превращает markdown автосгенерированных заметок GitHub в читаемый текст.
    /// TextBox разметку не понимает, и человек видел «## What's Changed»,
    /// «* … by @user in https://…» как есть. Полноценный markdown здесь не
    /// нужен — достаточно убрать синтаксис и служебный шум генератора.
    /// </summary>
    public static string PrettifyNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return string.Empty;

        var result = new List<string>();
        foreach (var raw in notes.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();

            // Ссылка «полный список изменений» — служебная строка генератора;
            // в маленьком окне длинный URL только мешает.
            if (line.Contains("Full Changelog", StringComparison.OrdinalIgnoreCase)) continue;

            // Заголовки: «## What's Changed» → «What's Changed».
            line = System.Text.RegularExpressions.Regex.Replace(line, @"^#{1,6}\s+", "");

            // Пункты списка: «* …» / «- …» → «• …».
            line = System.Text.RegularExpressions.Regex.Replace(line, @"^(\s*)[*-]\s+", "$1• ");

            // «by @user in https://…/pull/123» → «(#123)»: авторство в личном
            // репозитории очевидно, а ссылку человек всё равно не кликнет.
            line = System.Text.RegularExpressions.Regex.Replace(line,
                @"\s+by @[\w-]+ in \S+/pull/(\d+)", " (#$1)");

            // [текст](url) → текст, затем жирный/курсив/код — просто убираем.
            line = System.Text.RegularExpressions.Regex.Replace(line, @"\[([^\]]+)\]\([^)]*\)", "$1");
            line = line.Replace("**", "").Replace("`", "");

            result.Add(line);
        }

        // Схлопываем пустые строки, оставшиеся от выброшенных.
        var text = string.Join('\n', result);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }

    private async void OnInstallClick(object? sender, EventArgs e)
    {
        // Файла в релизе может не быть — тогда просто открываем страницу.
        if (string.IsNullOrEmpty(_release.InstallerUrl))
        {
            UpdateService.OpenReleasesPage(_release.PageUrl);
            DialogResult = DialogResult.Cancel;
            return;
        }

        _install.Enabled = false;
        _later.Enabled = false;
        _progress.Visible = true;
        _status.Text = L10n.T("update.downloading");

        _cts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<int>(p => _progress.Value = Math.Clamp(p, 0, 100));
            var path = await UpdateService.DownloadInstallerAsync(_release, progress, _cts.Token);

            _status.Text = L10n.T("update.launching");
            UpdateService.LaunchInstaller(path);

            // Инсталлятор закроет нашу копию сам, но выходим явно, чтобы он
            // наверняка смог заменить файлы.
            DialogResult = DialogResult.OK;
            Application.Exit();
        }
        catch (Exception ex)
        {
            _progress.Visible = false;
            _status.Text = L10n.T("update.installFailed", ex.Message);
            _status.ForeColor = Theme.Danger;   // ошибка обязана отличаться от подсказки цветом
            _install.Enabled = true;
            _later.Enabled = true;
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        base.OnFormClosed(e);
    }
}
