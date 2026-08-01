using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KeyLangSwitcher.Core;

/// <summary>
/// Всё, что делает программа: конвертирует ВЫДЕЛЕННЫЙ текст.
///
/// Схема (одинаковая для раскладки и регистра):
///   1) ждём отпускания модификаторов хоткея;
///   2) запоминаем текст clipboard и его системный счётчик;
///   3) Ctrl+C — читаем выделение (факт копирования детектим по счётчику);
///   4) преобразуем (раскладка 1-в-1 / смена регистра);
///   5) печатаем результат через SendUnicode — он ЗАМЕНЯЕТ выделение
///      (выделение ещё активно, ввод его перетирает). НИКАКОГО Ctrl+V;
///   6) для раскладки — переключаем системную раскладку;
///   7) возвращаем исходный текст clipboard.
///
/// Конвертированный текст НИКОГДА не кладётся в clipboard → история Win+V чистая.
/// Если выделения нет — clipboard вообще не трогаем.
/// </summary>
public static class SelectionConverter
{
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int nVirtKey);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C;

    /// <summary>Результат попытки прочитать выделение.</summary>
    private readonly record struct Grab(string Text, string? PreviousClipboardText, bool ClipboardChanged)
    {
        public bool HasText => !string.IsNullOrEmpty(Text);
    }

    /// <summary>Конвертация раскладки выделения (1-в-1) + переключение системной раскладки.</summary>
    public static void ConvertSelection()
    {
        var g = GrabSelection();
        if (!g.HasText) { RestoreClipboard(g); return; }

        var (converted, dir) = LayoutConverter.AutoConvertWithDirection(g.Text);
        Sender.SendUnicode(converted);          // заменяет выделение
        if (dir == LayoutConverter.Direction.ToRu) LayoutSwitcher.SwitchToRussian();
        else if (dir == LayoutConverter.Direction.ToEn) LayoutSwitcher.SwitchToEnglish();

        RestoreClipboard(g);
    }

    /// <summary>Смена регистра выделения: есть заглавная → всё lower, иначе всё upper.</summary>
    public static void ToggleSelectionCase()
    {
        var g = GrabSelection();
        if (!g.HasText) { RestoreClipboard(g); return; }

        Sender.SendUnicode(Toggle(g.Text));     // заменяет выделение
        RestoreClipboard(g);
    }

    public static string Toggle(string text)
    {
        bool hasUpper = false;
        foreach (var c in text) if (char.IsUpper(c)) { hasUpper = true; break; }
        return hasUpper ? text.ToLower() : text.ToUpper();
    }

    /// <summary>
    /// Копирует выделение через Ctrl+C и читает его текст.
    ///
    /// Факт копирования детектим по СИСТЕМНОМУ СЧЁТЧИКУ буфера обмена
    /// (`GetClipboardSequenceNumber`) — он растёт только когда кто-то реально
    /// записал в clipboard. Это надёжнее, чем чистить clipboard и смотреть
    /// «появился ли текст»: при медленном приложении такая проверка давала
    /// ложное «выделения нет», и хоткей молча ничего не делал.
    /// </summary>
    private static Grab GrabSelection()
    {
        WaitForModifiersReleased();

        string? previous = null;
        try { if (Clipboard.ContainsText()) previous = Clipboard.GetText(); } catch { }

        uint seqBefore = GetClipboardSequenceNumber();
        Sender.SendCtrlKey('C');

        var deadline = DateTime.UtcNow.AddMilliseconds(500);
        while (DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            System.Threading.Thread.Sleep(10);
            if (GetClipboardSequenceNumber() == seqBefore) continue;

            // Счётчик сдвинулся — clipboard перезаписан нашим Ctrl+C.
            // Даём владельцу мгновение дописать форматы.
            System.Threading.Thread.Sleep(20);
            try
            {
                if (Clipboard.ContainsText())
                {
                    var t = Clipboard.GetText();
                    if (!string.IsNullOrEmpty(t)) return new Grab(t, previous, true);
                }
            }
            catch { }
            return new Grab(string.Empty, previous, true); // скопировали не-текст
        }

        // Счётчик не изменился — выделения не было, clipboard не тронут.
        return new Grab(string.Empty, previous, false);
    }

    /// <summary>
    /// Возвращает исходный текст clipboard, но ТОЛЬКО если мы его перезаписали.
    /// Если выделения не было, clipboard не трогали — и трогать не будем
    /// (иначе можно затереть скопированную картинку/файл).
    /// </summary>
    private static void RestoreClipboard(Grab g)
    {
        if (!g.ClipboardChanged) return;
        try
        {
            if (g.PreviousClipboardText != null) ClipboardSafe.SetText(g.PreviousClipboardText);
            else Clipboard.Clear();
        }
        catch { }
    }

    private static void WaitForModifiersReleased()
    {
        // Если хоткей содержит Alt, пользователь ещё держит его. Одиночный тап Alt
        // активирует строку меню и уводит фокус — гасим это Ctrl-тапом ДО того,
        // как пользователь отпустит Alt.
        if ((GetAsyncKeyState(VK_MENU) & 0x8000) != 0)
            Sender.CancelAltMenuActivation();

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
