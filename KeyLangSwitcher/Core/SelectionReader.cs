using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KeyLangSwitcher.Core;

/// <summary>
/// Читает выделенный в активном окне текст.
///
/// Единственное место в программе, которое касается буфера обмена, и только на
/// ЧТЕНИЕ: Windows не даёт другого универсального способа узнать выделение.
/// Наш результат в clipboard никогда не пишется — он печатается эмуляцией
/// клавиатуры поверх выделения.
///
/// Факт копирования определяем по системному счётчику буфера обмена
/// (`GetClipboardSequenceNumber`): он растёт только когда кто-то реально
/// записал данные. Это надёжный положительный сигнал, в отличие от «очистили
/// и посмотрели, появился ли текст» — при пустом выделении или медленном
/// приложении такая проверка давала ложный результат.
///
/// Прежнее содержимое буфера обмена восстанавливается сразу, с форматами
/// исключения из истории Win+V и облачного clipboard.
/// </summary>
internal static class SelectionReader
{
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();

    /// <summary>
    /// Возвращает выделенный текст или null, если выделения нет.
    /// Вызывать только из UI-потока (Clipboard требует STA).
    /// </summary>
    public static string? TryRead()
    {
        string? previous = null;
        try { if (Clipboard.ContainsText()) previous = Clipboard.GetText(); } catch { }

        uint seqBefore = GetClipboardSequenceNumber();
        Sender.SendCtrlKey('C');

        string? selection = null;
        var deadline = DateTime.UtcNow.AddMilliseconds(400);
        while (DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            System.Threading.Thread.Sleep(10);
            if (GetClipboardSequenceNumber() == seqBefore) continue;

            // Счётчик сдвинулся — приложение положило данные. Даём ему мгновение
            // дописать форматы, затем читаем.
            System.Threading.Thread.Sleep(20);
            try
            {
                if (Clipboard.ContainsText())
                {
                    var t = Clipboard.GetText();
                    if (!string.IsNullOrEmpty(t)) selection = t;
                }
            }
            catch { }
            break;
        }

        // Возвращаем прежнее содержимое. Если счётчик не менялся, буфер обмена
        // не трогали вообще — и не трогаем (иначе затрём скопированную картинку).
        if (GetClipboardSequenceNumber() != seqBefore)
        {
            try
            {
                if (previous != null) ClipboardSafe.SetText(previous);
                else Clipboard.Clear();
            }
            catch { }
        }

        return selection;
    }
}
