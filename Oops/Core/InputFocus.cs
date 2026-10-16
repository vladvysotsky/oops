using System.Runtime.InteropServices;

namespace Oops.Core;

/// <summary>
/// Окно, которое на самом деле получает клавиатурный ввод.
///
/// Обычно это окно переднего плана или его ребёнок из того же потока, и
/// разницы нет. Но у приложений на WebView2 (новый Outlook, Teams, многие
/// утилиты) поле ввода — дочернее окно ДРУГОГО процесса, msedgewebview2.exe.
/// У его потока своя раскладка: Alt+Shift в таком поле меняет её, а раскладка
/// потока окна переднего плана остаётся прежней. Спрашивая её, мы распознавали
/// набранное не в той раскладке и переключали язык не тому окну — хоткей
/// в таких полях не делал ничего полезного.
///
/// Окна родителя и ребёнка из разных потоков Windows связывает общей очередью
/// ввода, поэтому GetGUIThreadInfo для потока переднего плана отдаёт настоящее
/// окно с фокусом, даже если оно чужого процесса. Если фокус узнать не удалось,
/// остаётся прежнее поведение — окно переднего плана.
/// </summary>
internal static class InputFocus
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public int cbSize;
        public uint flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public RECT rcCaret;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint idThread);

    /// <summary>Окно с фокусом ввода; IntPtr.Zero, если активного окна нет.</summary>
    public static IntPtr Window()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return IntPtr.Zero;

        var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        uint tid = GetWindowThreadProcessId(fg, out _);
        if (tid != 0 && GetGUIThreadInfo(tid, ref info) && info.hwndFocus != IntPtr.Zero)
            return info.hwndFocus;
        return fg;
    }

    /// <summary>Раскладка потока, который получает ввод.</summary>
    public static IntPtr Layout()
    {
        var hwnd = Window();
        uint tid = hwnd == IntPtr.Zero ? 0 : GetWindowThreadProcessId(hwnd, out _);
        return GetKeyboardLayout(tid);
    }
}
