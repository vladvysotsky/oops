using System.Runtime.InteropServices;

namespace KeyLangSwitcher.Core;

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

    public static void SwitchToRussian() => SwitchTo(LANG_RUSSIAN);
    public static void SwitchToEnglish() => SwitchTo(LANG_ENGLISH);

    private static void SwitchTo(ushort primaryLang)
    {
        var hkl = FindInstalledLayout(primaryLang);
        if (hkl == IntPtr.Zero) return;
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;
        PostMessage(hwnd, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hkl);
    }

    private static IntPtr FindInstalledLayout(ushort primaryLang)
    {
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
