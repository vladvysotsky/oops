using System.Text;

namespace Oops.Core;

/// <summary>
/// Подробный лог — только для разбора «программа вдруг перестала работать».
///
/// **Набранный текст в лог НЕ ПОПАДАЕТ НИКОГДА.** Клавиши пишутся кодами
/// (VK 0x41), а не символами: этого хватает, чтобы понять, доходит ли до нас
/// сочетание, и при этом лог не превращается в запись всего, что человек
/// печатал. Программа видит весь ввод — единственное, что делает её пригодной
/// к использованию, это то, что она его никуда не девает, и лог исключением
/// быть не может.
///
/// По умолчанию выключен. Включается в настройках, пишется в
/// %AppData%\Oops\logs, старые файлы удаляются.
/// </summary>
public static class Log
{
    private static readonly object Gate = new();
    private static StreamWriter? _writer;
    private static string? _path;

    /// <summary>Лог включён и пишется.</summary>
    public static bool Enabled { get; private set; }

    /// <summary>Папка с логами — её открывают кнопкой из настроек.</summary>
    public static string Directory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Oops", "logs");

    /// <summary>Файл текущей сессии, если лог включён.</summary>
    public static string? CurrentFile => _path;

    /// <summary>
    /// Потолок одного файла. Хук пишет строку на каждое нажатие: за рабочий
    /// день это десятки тысяч строк, и без потолка лог съедал бы диск ровно у
    /// того человека, который включил его, чтобы помочь.
    /// </summary>
    private const long MaxBytes = 8L * 1024 * 1024;

    /// <summary>Сколько файлов храним — остальные удаляются при старте.</summary>
    private const int KeepFiles = 5;

    public static void Start()
    {
        lock (Gate)
        {
            if (Enabled) return;
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                Cleanup();
                _path = Path.Combine(Directory,
                    $"oops-{DateTime.Now:yyyyMMdd-HHmmss}.log");
                _writer = new StreamWriter(_path, append: true, Encoding.UTF8) { AutoFlush = true };
                Enabled = true;
            }
            catch
            {
                // Не смогли писать лог — это не повод не запускаться.
                _writer = null;
                _path = null;
                Enabled = false;
                return;
            }
        }

        Write("=== лог включён, oops " + UpdateService.CurrentVersion);
        Write("Windows: " + Environment.OSVersion.VersionString
              + ", процессоров: " + Environment.ProcessorCount);
    }

    public static void Stop()
    {
        lock (Gate)
        {
            if (!Enabled) return;
            try { _writer?.WriteLine(Stamp() + "=== лог выключен"); _writer?.Dispose(); } catch { }
            _writer = null;
            Enabled = false;
        }
    }

    /// <summary>Одна строка в лог. Ничего не делает, когда лог выключен.</summary>
    public static void Write(string message)
    {
        if (!Enabled) return;
        lock (Gate)
        {
            if (_writer == null) return;
            try
            {
                _writer.WriteLine(Stamp() + message);
                if (_writer.BaseStream.Length > MaxBytes)
                {
                    _writer.WriteLine(Stamp() + "=== файл вырос до потолка, лог остановлен");
                    _writer.Dispose();
                    _writer = null;
                    Enabled = false;
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Клавиша — КОДОМ, без символа. Отдельный метод, чтобы «залогировать
    /// нажатие» нельзя было сделать иначе.
    /// </summary>
    public static void Key(string what, int vk, bool ctrl, bool alt, bool shift, bool win, bool repeat)
    {
        if (!Enabled) return;
        var mods = new StringBuilder();
        if (ctrl) mods.Append("Ctrl+");
        if (alt) mods.Append("Alt+");
        if (shift) mods.Append("Shift+");
        if (win) mods.Append("Win+");
        Write($"{what}: {mods}VK 0x{vk:X2}{(repeat ? " (автоповтор)" : "")}");
    }

    private static string Stamp() => DateTime.Now.ToString("HH:mm:ss.fff") + "  ";

    /// <summary>Оставляем последние файлы, остальные удаляем.</summary>
    private static void Cleanup()
    {
        try
        {
            var files = new DirectoryInfo(Directory)
                .GetFiles("oops-*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(KeepFiles - 1);
            foreach (var file in files) file.Delete();
        }
        catch { }
    }
}
