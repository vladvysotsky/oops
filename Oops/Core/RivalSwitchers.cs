using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Oops.Core;

/// <summary>
/// Не работает ли рядом другой переключатель раскладки.
///
/// Две программы с низкоуровневым перехватом клавиатуры мешают друг другу, и
/// ни одна об этом не говорит: соседский хук глотает часть наших нажатий или
/// наших же вставок, сочетание не срабатывает, исправленный текст приходит с
/// пропусками. Снаружи это неотличимо от сломанной программы. У keyboop ровно
/// так было на живой машине: три диктовки из 34 пришли порванными, пока рядом
/// работал второй переключатель.
///
/// <see cref="HotkeyConflicts"/> этого не видит: он ловит только тех, кто
/// регистрирует сочетания через RegisterHotKey, а переключатели почти все
/// работают хуком, как и мы.
///
/// Узнаём по описанию исполняемого файла, а не по имени процесса. Имена
/// ненадёжны: у Punto Switcher процесс зовётся общим «ps.exe», который может
/// оказаться чем угодно, а у Caramba Switcher в имени файла стоит дата версии.
/// Название продукта в ресурсах exe таких проблем не имеет.
/// </summary>
public static class RivalSwitchers
{
    /// <summary>
    /// Что ищем в названии продукта или описании файла. Это же название и
    /// показывается человеку. Список короткий намеренно: каждая строка здесь —
    /// предупреждение кому-то на экране, и ложное хуже пропущенного.
    /// </summary>
    private static readonly string[] Known =
    {
        "Punto Switcher",
        "Caramba Switcher",
        "Orfo Switcher",
        "Arum Switcher",
        "Keyboard Ninja",
        "Mahou",
    };

    /// <summary>
    /// Чистая часть проверки: какой из известных переключателей описывают эти
    /// строки ресурсов exe, или null. Вынесена отдельно, чтобы проверять тестом
    /// без живых процессов.
    /// </summary>
    public static string? Match(string? productName, string? fileDescription)
    {
        foreach (var name in Known)
        {
            if (productName?.Contains(name, StringComparison.OrdinalIgnoreCase) == true
                || fileDescription?.Contains(name, StringComparison.OrdinalIgnoreCase) == true)
                return name;
        }
        return null;
    }

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags,
        StringBuilder path, ref int size);

    /// <summary>
    /// Запущенные сейчас переключатели, без повторов.
    ///
    /// Путь к exe берём через QueryFullProcessImageName с правом
    /// PROCESS_QUERY_LIMITED_INFORMATION, а не через Process.MainModule: тому
    /// нужно право читать память процесса, и на чужих процессах он почти всегда
    /// падает с отказом в доступе. Ограниченного права хватает и на процессы,
    /// запущенные от администратора.
    ///
    /// Перебор сотен процессов с чтением ресурсов — десятки миллисекунд, поэтому
    /// вызывать не из UI-потока и не на горячем пути старта.
    /// </summary>
    public static IReadOnlyList<string> FindRunning()
    {
        var found = new List<string>();
        int self = Environment.ProcessId;

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id == self) continue;
                try
                {
                    var path = ImagePath(process.Id);
                    if (path == null) continue;

                    var info = FileVersionInfo.GetVersionInfo(path);
                    var name = Match(info.ProductName, info.FileDescription);
                    if (name != null && !found.Contains(name)) found.Add(name);
                }
                catch
                {
                    // Процесс успел завершиться или файл недоступен — просто
                    // не наш случай, проверка не должна из-за этого падать.
                }
            }
        }
        return found;
    }

    private static string? ImagePath(int pid)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(handle, 0, sb, ref size) ? sb.ToString(0, size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }
}
