using System.Windows.Forms;

namespace KeyLangSwitcher.Core;

/// <summary>
/// Конвертирует выделенный текст: Ctrl+C → читаем clipboard → конвертируем → Ctrl+V.
/// Исходное содержимое буфера обмена восстанавливается (только текстовое содержимое,
/// чтобы избежать COM-крахов от просроченного IDataObject).
/// Вызывать только из UI-потока (Clipboard требует STA).
///
/// В этом режиме конвертация ВСЕГДА безусловная и применяется ко всему выделению —
/// если пользователь явно что-то выделил и нажал хоткей, его намерение однозначно.
/// </summary>
public static class SelectionConverter
{
    public static bool TryConvertSelection()
    {
        string? originalText = null;
        try
        {
            if (Clipboard.ContainsText()) originalText = Clipboard.GetText();
        }
        catch { }

        try { Clipboard.Clear(); } catch { }

        Sender.SendCtrlKey('C');

        string? text = null;
        var deadline = DateTime.UtcNow.AddMilliseconds(250);
        while (DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            System.Threading.Thread.Sleep(10);
            try
            {
                if (Clipboard.ContainsText())
                {
                    text = Clipboard.GetText();
                    if (!string.IsNullOrEmpty(text)) break;
                }
            }
            catch { }
        }

        if (string.IsNullOrEmpty(text))
        {
            RestoreText(originalText);
            return false;
        }

        // Безусловная конвертация всего выделения в доминантную раскладку —
        // пользователь сам выделил и явно хочет переключить всё это.
        var (converted, dir) = LayoutConverter.AutoConvertWithDirection(text);
        ClipboardSafe.SetText(converted);
        Sender.SendCtrlKey('V');

        if (dir == LayoutConverter.Direction.ToRu) LayoutSwitcher.SwitchToRussian();
        else if (dir == LayoutConverter.Direction.ToEn) LayoutSwitcher.SwitchToEnglish();

        System.Threading.Thread.Sleep(150);
        RestoreText(originalText);
        return true;
    }

    private static void RestoreText(string? snapshot)
    {
        if (snapshot == null) return;
        try { ClipboardSafe.SetText(snapshot); } catch { }
    }
}
