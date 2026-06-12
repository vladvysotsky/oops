using System.Windows.Forms;

namespace KeyLangSwitcher.Core;

/// <summary>
/// Атомарная вставка текста через буфер обмена: SetClipboard → Ctrl+V → отложенное восстановление.
/// Используется как замена SendUnicode (посимвольный ввод), чтобы избежать визуального
/// "тикания" символов в больших строках.
/// </summary>
public static class ClipboardPaste
{
    public static void Paste(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        // Снимаем СНИМОК содержимого clipboard как текст. Не храним IDataObject —
        // его COM-прокси становится невалидным к моменту отложенного восстановления,
        // и SetDataObject крашит процесс через combase.dll (STATUS_FATAL_USER_CALLBACK_EXCEPTION).
        string? originalText = null;
        try
        {
            if (Clipboard.ContainsText()) originalText = Clipboard.GetText();
        }
        catch { }

        ClipboardSafe.SetText(text);
        System.Threading.Thread.Sleep(40);
        Sender.SendCtrlKey('V');

        // Восстанавливаем оригинал через 1сек — даём Ctrl+V гарантированно
        // отработать в приёмнике (особенно UWP, где paste обрабатывается асинхронно).
        if (originalText != null)
        {
            var uiCtx = System.Threading.SynchronizationContext.Current;
            var snapshot = originalText;
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(1000);
                uiCtx?.Post(_ => { try { ClipboardSafe.SetText(snapshot); } catch { } }, null);
            });
        }
    }
}

