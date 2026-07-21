using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KeyLangSwitcher.Core;

/// <summary>
/// Всё, что делает программа: конвертирует ВЫДЕЛЕННЫЙ текст.
///
/// Схема (одинаковая для раскладки и регистра):
///   1) ждём отпускания модификаторов хоткея;
///   2) сохраняем текст clipboard (только текст, не IDataObject);
///   3) Ctrl+C — читаем выделение;
///   4) преобразуем (раскладка 1-в-1 / смена регистра);
///   5) печатаем результат через SendUnicode — он ЗАМЕНЯЕТ выделение
///      (выделение ещё активно, ввод его перетирает). НИКАКОГО Ctrl+V;
///   6) для раскладки — переключаем системную раскладку;
///   7) восстанавливаем исходный clipboard.
///
/// Конвертированный текст НИКОГДА не кладётся в clipboard → история Win+V чистая.
/// </summary>
public static class SelectionConverter
{
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int nVirtKey);
    private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C;

    /// <summary>Конвертация раскладки выделения (1-в-1) + переключение системной раскладки.</summary>
    public static void ConvertSelection()
    {
        var text = GrabSelection(out var original);
        if (string.IsNullOrEmpty(text)) { Restore(original); return; }

        var (converted, dir) = LayoutConverter.AutoConvertWithDirection(text);
        Sender.SendUnicode(converted);          // заменяет выделение
        if (dir == LayoutConverter.Direction.ToRu) LayoutSwitcher.SwitchToRussian();
        else if (dir == LayoutConverter.Direction.ToEn) LayoutSwitcher.SwitchToEnglish();

        Restore(original);
    }

    /// <summary>Смена регистра выделения: есть заглавная → всё lower, иначе всё upper.</summary>
    public static void ToggleSelectionCase()
    {
        var text = GrabSelection(out var original);
        if (string.IsNullOrEmpty(text)) { Restore(original); return; }

        Sender.SendUnicode(Toggle(text));       // заменяет выделение
        Restore(original);
    }

    public static string Toggle(string text)
    {
        bool hasUpper = false;
        foreach (var c in text) if (char.IsUpper(c)) { hasUpper = true; break; }
        return hasUpper ? text.ToLower() : text.ToUpper();
    }

    /// <summary>
    /// Копирует выделение в clipboard (Ctrl+C) и возвращает его текст.
    /// <paramref name="original"/> — прежний текст clipboard для восстановления.
    /// Пустая строка, если выделения нет.
    /// </summary>
    private static string GrabSelection(out string? original)
    {
        WaitForModifiersReleased();

        original = null;
        try { if (Clipboard.ContainsText()) original = Clipboard.GetText(); } catch { }
        try { Clipboard.Clear(); } catch { }

        Sender.SendCtrlKey('C');

        var deadline = DateTime.UtcNow.AddMilliseconds(120);
        while (DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            System.Threading.Thread.Sleep(8);
            try
            {
                if (Clipboard.ContainsText())
                {
                    var t = Clipboard.GetText();
                    if (!string.IsNullOrEmpty(t)) return t;
                }
            }
            catch { }
        }
        return string.Empty;
    }

    /// <summary>Возвращает исходный clipboard (текстом, с исключением из истории Win+V).</summary>
    private static void Restore(string? original)
    {
        try
        {
            if (string.IsNullOrEmpty(original)) Clipboard.Clear();
            else ClipboardSafe.SetText(original);
        }
        catch { }
    }

    private static void WaitForModifiersReleased()
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(1000);
        while (DateTime.UtcNow < deadline && AnyModifierDown())
        {
            Application.DoEvents();
            System.Threading.Thread.Sleep(20);
        }
        System.Threading.Thread.Sleep(50);
    }

    private static bool AnyModifierDown() =>
        (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0 ||
        (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0 ||
        (GetAsyncKeyState(VK_MENU) & 0x8000) != 0 ||
        (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 ||
        (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;
}
