using System.IO;
using System.Windows.Forms;

namespace Oops.Core;

/// <summary>
/// Помощник для записи в буфер обмена так, чтобы текст НЕ попадал в историю Win+V
/// и облачный clipboard. Используем регистрируемые форматы Windows 11.
/// </summary>
internal static class ClipboardSafe
{
    private const string FmtExcludeMonitor = "ExcludeClipboardContentFromMonitorProcessing";
    private const string FmtHistory       = "CanIncludeInClipboardHistory";
    private const string FmtCloud         = "CanUploadToCloudClipboard";

    public static void SetText(string text)
    {
        var data = new DataObject();
        data.SetText(text);
        // Любые типы значений работают — clipboard-история проверяет НАЛИЧИЕ формата;
        // для cloud/history также используем DWORD=0, упакованный в MemoryStream.
        data.SetData(FmtExcludeMonitor, true);
        data.SetData(FmtHistory, ZeroDword());
        data.SetData(FmtCloud,   ZeroDword());
        try { Clipboard.SetDataObject(data, copy: true); } catch { }
    }

    private static MemoryStream ZeroDword() => new(new byte[] { 0, 0, 0, 0 });
}
