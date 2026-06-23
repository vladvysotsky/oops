using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KeyLangSwitcher.Core;

/// <summary>
/// Меняет регистр текста. Приоритет:
///   1) Если в активном окне есть выделение — меняем его регистр через clipboard
///      (round-trip Ctrl+C → toggle → Ctrl+V, как ConvertSelection).
///   2) Иначе — меняем регистр накопленного буфера через Backspace+SendUnicode.
///
/// Если есть заглавная буква — всё к нижнему, иначе всё к верхнему.
/// </summary>
public static class CaseConverter
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int nVirtKey);
    private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C;

    /// <summary>
    /// Пробует поменять регистр выделенного текста через clipboard.
    /// Возвращает true если выделение было и обработано.
    /// </summary>
    public static bool TryToggleSelectionCase()
    {
        // Ждём пока пользователь отпустит все модификаторы хоткея — иначе наша
        // эмуляция Ctrl+C столкнётся с зажатыми Win/Alt и фокус/clipboard могут
        // вести себя непредсказуемо.
        WaitForModifiersReleased();

        string? originalText = null;
        try { if (Clipboard.ContainsText()) originalText = Clipboard.GetText(); } catch { }
        try { Clipboard.Clear(); } catch { }

        Sender.SendCtrlKey('C');

        string? selection = null;
        var deadline = DateTime.UtcNow.AddMilliseconds(300);
        while (DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            System.Threading.Thread.Sleep(10);
            try
            {
                if (Clipboard.ContainsText())
                {
                    selection = Clipboard.GetText();
                    if (!string.IsNullOrEmpty(selection)) break;
                }
            }
            catch { }
        }

        if (string.IsNullOrEmpty(selection))
        {
            RestoreClipboardLater(originalText);
            return false;
        }

        var toggled = Toggle(selection);
        ClipboardSafe.SetText(toggled);
        System.Threading.Thread.Sleep(40);
        Sender.SendCtrlKey('V');
        RestoreClipboardLater(originalText);
        return true;
    }

    /// <summary>
    /// Если есть заглавные → всё к нижнему, иначе всё к верхнему.
    /// </summary>
    public static string Toggle(string text)
    {
        bool hasUpper = false;
        foreach (var c in text) if (char.IsUpper(c)) { hasUpper = true; break; }
        return hasUpper ? text.ToLower() : text.ToUpper();
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

    /// <summary>Восстанавливает clipboard через секунду на UI-потоке (та же схема, что в ClipboardPaste).</summary>
    private static void RestoreClipboardLater(string? snapshot)
    {
        if (snapshot == null) return;
        var uiCtx = System.Threading.SynchronizationContext.Current;
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            await System.Threading.Tasks.Task.Delay(1000);
            uiCtx?.Post(_ => { try { ClipboardSafe.SetText(snapshot); } catch { } }, null);
        });
    }
}
