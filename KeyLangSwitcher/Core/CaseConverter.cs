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
    public static bool TryToggleSelectionCase()
    {
        string? originalText = null;
        try { if (Clipboard.ContainsText()) originalText = Clipboard.GetText(); } catch { }

        try { Clipboard.Clear(); } catch { }
        Sender.SendCtrlKey('C');

        string? selection = null;
        var deadline = DateTime.UtcNow.AddMilliseconds(250);
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
