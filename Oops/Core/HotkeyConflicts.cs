using System.Runtime.InteropServices;

namespace Oops.Core;

/// <summary>
/// Проверка, не занял ли сочетание кто-то ещё.
///
/// Молчащий хоткей неотличим от сломанной программы — это уже стоило нам
/// нескольких заходов вслепую. Если сочетание держит другое приложение, лучше
/// сказать об этом при запуске, чем ждать, пока человек сам догадается.
///
/// Ловится не всё. `RegisterHotKey` видит только тех, кто регистрируется тем
/// же способом: Windows, Punto Switcher в части режимов, менеджеры окон.
/// Программы с собственным низкоуровневым хуком (а это второй популярный
/// способ, и наш собственный тоже) так не обнаруживаются — там конфликт
/// решается порядком установки хуков, и узнать о нём заранее нельзя вообще
/// никак. Поэтому отсутствие конфликта здесь — не гарантия, а его наличие —
/// точный ответ.
/// </summary>
public static class HotkeyConflicts
{
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;

    private const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    /// <summary>
    /// Занято ли сочетание кем-то ещё. Для сочетаний «только модификаторы»
    /// всегда false: их через RegisterHotKey не регистрируют в принципе.
    /// </summary>
    public static bool IsTaken(HotkeyConfig hotkey)
    {
        if (hotkey.Key == 0) return false;

        uint mods = 0;
        if (hotkey.Ctrl) mods |= MOD_CONTROL;
        if (hotkey.Alt) mods |= MOD_ALT;
        if (hotkey.Shift) mods |= MOD_SHIFT;
        if (hotkey.Win) mods |= MOD_WIN;

        // Идентификатор свой на каждую проверку: чужой id мы бы отняли у себя же.
        int id = 0x4F00 + (hotkey.Key & 0xFF);
        try
        {
            if (RegisterHotKey(IntPtr.Zero, id, mods, (uint)hotkey.Key))
            {
                // Заняли — тут же отпускаем, иначе сами станем тем, кто мешает.
                UnregisterHotKey(IntPtr.Zero, id);
                return false;
            }
            return Marshal.GetLastWin32Error() == ERROR_HOTKEY_ALREADY_REGISTERED;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Имена тех сочетаний из списка, которые заняты.</summary>
    public static List<string> Find(IEnumerable<(string Name, HotkeyConfig Hotkey)> hotkeys)
    {
        var taken = new List<string>();
        foreach (var (name, hotkey) in hotkeys)
        {
            bool busy = IsTaken(hotkey);
            Log.Write($"проверка сочетания «{name}» ({hotkey}): "
                      + (busy ? "занято другой программой" : "свободно"));
            if (busy) taken.Add($"{name} — {hotkey}");
        }
        return taken;
    }
}
