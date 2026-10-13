using System.Runtime.InteropServices;

namespace Oops.Core;

/// <summary>
/// Переключение системной раскладки активного окна. Используется после конвертации,
/// чтобы пользователь мог продолжить печатать в "правильной" раскладке.
/// </summary>
public static class LayoutSwitcher
{
    private const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;
    private const ushort LANG_RUSSIAN = 0x19;
    private const ushort LANG_ENGLISH = 0x09;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetKeyboardLayoutList(int nBuff, [Out] IntPtr[] lpList);

    /// <summary>
    /// Переключить на раскладку пары. У системной раскладки есть свой HKL —
    /// его и просим. У встроенной HKL нет, и тогда ищем установленную
    /// раскладку того же языка, как раньше для русской и английской.
    /// </summary>
    public static void SwitchTo(KeyboardLayout layout)
    {
        var hkl = layout.Handle != IntPtr.Zero ? layout.Handle : FindInstalledLayout(PrimaryLanguage(layout.Language));
        Post(hkl);
    }

    private static ushort PrimaryLanguage(string iso) => iso switch
    {
        "ru" => LANG_RUSSIAN,
        "en" => LANG_ENGLISH,
        _ => (ushort)0,
    };

    private static void Post(IntPtr hkl)
    {
        if (hkl == IntPtr.Zero) return;
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;
        PostMessage(hwnd, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hkl);
    }

    private static IntPtr FindInstalledLayout(ushort primaryLang)
    {
        if (primaryLang == 0) return IntPtr.Zero;
        int count = GetKeyboardLayoutList(0, Array.Empty<IntPtr>());
        if (count <= 0) return IntPtr.Zero;
        var list = new IntPtr[count];
        GetKeyboardLayoutList(count, list);
        foreach (var hkl in list)
        {
            // HKL low word = LANGID, low 10 бит = primary language id.
            ushort langid = (ushort)(hkl.ToInt64() & 0xFFFF);
            if ((langid & 0x3FF) == primaryLang) return hkl;
        }
        return IntPtr.Zero;
    }
}
