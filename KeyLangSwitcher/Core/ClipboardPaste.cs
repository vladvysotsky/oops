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

        IDataObject? original = null;
        try { original = Clipboard.GetDataObject(); } catch { }

        ClipboardSafe.SetText(text);
        System.Threading.Thread.Sleep(40); // даём clipboard стабилизироваться
        Sender.SendCtrlKey('V');

        // Восстанавливаем оригинальный clipboard через 1сек — даём Ctrl+V гарантированно
        // отработать в приёмнике (особенно UWP, где paste обрабатывается асинхронно).
        if (original != null)
        {
            var uiCtx = System.Threading.SynchronizationContext.Current;
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(1000);
                uiCtx?.Post(_ => { try { Clipboard.SetDataObject(original, copy: true); } catch { } }, null);
            });
        }
    }
}
