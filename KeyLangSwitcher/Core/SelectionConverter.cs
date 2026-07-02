using System.Windows.Forms;

namespace KeyLangSwitcher.Core;

/// <summary>
/// Конвертирует выделенный текст: Ctrl+C → читаем clipboard → 1-в-1 конверсия по
/// таблице раскладок → Ctrl+V. Пунктуация (',' '.' и т.п.) маппится корректно,
/// никаких пословных догадок — что выделено, то и переворачивается целиком.
/// Вызывать только из UI-потока (Clipboard требует STA).
/// </summary>
public static class SelectionConverter
{
    /// <summary>
    /// Возвращает true, если было выделение и оно сконвертировано.
    /// Возвращает false быстро (без длинных пауз), если выделения нет —
    /// чтобы вызывающий код мог сразу перейти к буферному режиму.
    /// </summary>
    public static bool TryConvertSelection()
    {
        string? originalText = null;
        try { if (Clipboard.ContainsText()) originalText = Clipboard.GetText(); } catch { }

        try { Clipboard.Clear(); } catch { }

        Sender.SendCtrlKey('C');

        // Проба выделения. При реальном выделении Ctrl+C наполняет clipboard
        // почти мгновенно, и цикл выходит рано. Полный таймаут короткий (120мс),
        // чтобы отсутствие выделения не тормозило переход к буферному режиму.
        string? text = null;
        var deadline = DateTime.UtcNow.AddMilliseconds(120);
        while (DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            System.Threading.Thread.Sleep(8);
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
            RestoreTextLater(originalText);
            return false;
        }

        // 1-в-1 конверсия всего выделения в доминантную раскладку.
        var (converted, dir) = LayoutConverter.AutoConvertWithDirection(text);
        ClipboardSafe.SetText(converted);
        System.Threading.Thread.Sleep(30);
        Sender.SendCtrlKey('V');

        if (dir == LayoutConverter.Direction.ToRu) LayoutSwitcher.SwitchToRussian();
        else if (dir == LayoutConverter.Direction.ToEn) LayoutSwitcher.SwitchToEnglish();

        // Восстанавливаем оригинальный clipboard отложенно (1с), не блокируя поток —
        // даём Ctrl+V гарантированно прочитать наш текст (UWP-приложения читают async).
        RestoreTextLater(originalText);
        return true;
    }

    private static void RestoreTextLater(string? snapshot)
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
