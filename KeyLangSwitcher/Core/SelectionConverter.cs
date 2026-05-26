using System.Windows.Forms;

namespace KeyLangSwitcher.Core;

/// <summary>
/// Конвертирует выделенный текст: Ctrl+C -> читаем clipboard -> конвертируем -> Ctrl+V.
/// Исходное содержимое буфера обмена восстанавливается.
/// Вызывать только из UI-потока (Clipboard требует STA).
/// </summary>
public static class SelectionConverter
{
    public static bool TryConvertSelection()
    {
        // 1) Сохраняем clipboard
        IDataObject? original = null;
        try { original = Clipboard.GetDataObject(); } catch { }

        // 2) Очищаем, чтобы понять — пришёл ли новый текст
        try { Clipboard.Clear(); } catch { }

        // 3) Шлём Ctrl+C
        Sender.SendCtrlKey('C');

        // 4) Ждём появления текста
        string? text = null;
        var deadline = DateTime.UtcNow.AddMilliseconds(400);
        while (DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            System.Threading.Thread.Sleep(20);
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
            // ничего не выделено — восстанавливаем clipboard и выходим
            RestoreClipboard(original);
            return false;
        }

        // 5) Конвертация и вставка
        var converted = LayoutConverter.AutoConvert(text);
        try { Clipboard.SetText(converted); } catch { }
        Sender.SendCtrlKey('V');

        // 6) Возврат оригинального clipboard с небольшой задержкой, чтобы Ctrl+V успел сработать
        System.Threading.Thread.Sleep(150);
        RestoreClipboard(original);
        return true;
    }

    private static void RestoreClipboard(IDataObject? original)
    {
        if (original == null) return;
        try { Clipboard.SetDataObject(original, copy: true); } catch { }
    }
}
