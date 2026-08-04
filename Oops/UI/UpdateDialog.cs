using System.Drawing;
using System.Windows.Forms;
using Oops.Core;

namespace Oops.UI;

/// <summary>
/// Окно «доступно обновление»: версия, описание релиза и кнопка установки.
/// Во время загрузки показывает прогресс вместо кнопок.
/// </summary>
public sealed class UpdateDialog : Form
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

        Text = "Обновление";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Theme.Canvas;
        Font = Theme.Body;

        _install = new FlatButton { Text = "Обновить", Primary = true, Size = new Size(124, 34) };
        _later = new FlatButton { Text = "Позже", Size = new Size(104, 34), DialogResult = DialogResult.Cancel };
        _install.Click += OnInstallClick;

        BuildLayout();

        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        CancelButton = _later;
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
            Text = $"Доступна версия {_release.Version}",
            Font = Theme.Title,
            ForeColor = Theme.Text,
            AutoSize = true,
            MaximumSize = new Size(ContentWidth, 0),
            Margin = new Padding(0, 0, 0, Theme.S1),
            BackColor = Color.Transparent,
        });

        AddRow(root, new Label
        {
            Text = $"Установлена {UpdateService.CurrentVersion}",
            Font = Theme.Caption,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, Theme.S3),
            BackColor = Color.Transparent,
        });

        if (!string.IsNullOrWhiteSpace(_release.Notes))
        {
            var card = new Card
            {
                Width = ContentWidth,
                Height = 150,
                Margin = new Padding(0, 0, 0, Theme.S3),
            };
            var notes = new TextBox
            {
                Text = _release.Notes.Replace("\n", Environment.NewLine),
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Theme.Surface,
                ForeColor = Theme.TextMuted,
                Font = Theme.Caption,
                Dock = DockStyle.Fill,
            };
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

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Right,
            BackColor = Theme.Canvas,
            Margin = new Padding(0, Theme.S3, 0, 0),
        };
        _install.Margin = new Padding(Theme.S2, 0, 0, 0);
        _later.Margin = new Padding(Theme.S2, 0, 0, 0);
        buttons.Controls.Add(_install);
        buttons.Controls.Add(_later);
        AddRow(root, buttons);

        Controls.Add(root);
    }

    private static void AddRow(TableLayoutPanel host, Control child)
    {
        host.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        host.Controls.Add(child, 0, host.RowCount);
        host.RowCount++;
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
        _status.Text = "Загрузка…";

        _cts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<int>(p => _progress.Value = Math.Clamp(p, 0, 100));
            var path = await UpdateService.DownloadInstallerAsync(_release, progress, _cts.Token);

            _status.Text = "Запуск установщика…";
            UpdateService.LaunchInstaller(path);

            // Инсталлятор закроет нашу копию сам, но выходим явно, чтобы он
            // наверняка смог заменить файлы.
            DialogResult = DialogResult.OK;
            Application.Exit();
        }
        catch (Exception ex)
        {
            _progress.Visible = false;
            _status.Text = $"Не удалось обновить: {ex.Message}";
            _status.ForeColor = Theme.AccentPressed;
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
