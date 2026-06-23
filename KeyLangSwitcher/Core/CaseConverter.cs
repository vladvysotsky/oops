using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace KeyLangSwitcher.Core;

/// <summary>
/// Меняет регистр ВЫДЕЛЕННОГО текста: целиком в нижний или в верхний.
/// Поведение: если в выделении есть хоть одна заглавная — приводим к нижнему,
/// иначе к верхнему. Так одно нажатие хоткея переключает регистр туда-сюда.
/// </summary>
public static class CaseConverter
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int nVirtKey);
    private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C;

    public static bool TryToggleSelectionCase()
    {
        // Ждём пока пользователь отпустит все модификаторы хоткея (особенно Win/Alt) —
        // иначе наша эмуляция Ctrl+C столкнётся с зажатыми клавишами, Windows может
        // интерпретировать Win-up как tap (открыть Start menu) и т.п.
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
            RestoreText(originalText);
            return false;
        }

        var toggled = Toggle(selection);
        ClipboardSafe.SetText(toggled);
        Sender.SendCtrlKey('V');
        System.Threading.Thread.Sleep(150);
        RestoreText(originalText);
        return true;
    }

    private static void WaitForModifiersReleased()
    {
        // Максимум 1 секунда ожидания, чтобы не зависнуть навсегда если у пользователя
        // что-то застряло.
        var deadline = DateTime.UtcNow.AddMilliseconds(1000);
        while (DateTime.UtcNow < deadline && AnyModifierDown())
        {
            Application.DoEvents();
            System.Threading.Thread.Sleep(20);
        }
        // Дополнительно — короткая пауза после отпускания, чтобы система переварила key-up.
        System.Threading.Thread.Sleep(50);
    }

    private static bool AnyModifierDown() =>
        (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0 ||
        (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0 ||
        (GetAsyncKeyState(VK_MENU) & 0x8000) != 0 ||
        (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 ||
        (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;

    /// <summary>
    /// Если есть заглавные → всё к нижнему, иначе всё к верхнему.
    /// </summary>
    public static string Toggle(string text)
    {
        bool hasUpper = false;
        foreach (var c in text) if (char.IsUpper(c)) { hasUpper = true; break; }
        return hasUpper ? text.ToLower() : text.ToUpper();
    }

    private static void RestoreText(string? snapshot)
    {
        if (snapshot == null) return;
        try { ClipboardSafe.SetText(snapshot); } catch { }
    }
}

