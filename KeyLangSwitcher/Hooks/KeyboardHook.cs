using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KeyLangSwitcher.Hooks;

/// <summary>
/// Глобальный low-level keyboard hook (WH_KEYBOARD_LL).
/// Получает все нажатия системно; может "проглатывать" события, возвращая true из обработчика.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYUP = 0x0105;

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte[] lpKeyState,
        [Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pwszBuff, int cchBuff,
        uint wFlags, IntPtr dwhkl);

    [DllImport("user32.dll")]
    private static extern bool GetKeyboardState(byte[] lpKeyState);

    private LowLevelKeyboardProc? _proc;
    private IntPtr _hook = IntPtr.Zero;

    public event EventHandler<KeyEvent>? KeyDown;

    public sealed class KeyEvent
    {
        public Keys VirtualKey { get; init; }
        public uint ScanCode { get; init; }
        public char? TypedChar { get; init; }
        public bool Handled { get; set; }
        public bool Ctrl { get; init; }
        public bool Alt { get; init; }
        public bool Shift { get; init; }
        public bool Win { get; init; }
    }

    public void Install()
    {
        if (_hook != IntPtr.Zero) return;
        _proc = HookCallback;
        using var proc = Process.GetCurrentProcess();
        using var mod = proc.MainModule!;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(mod.ModuleName), 0);
        if (_hook == IntPtr.Zero)
            throw new InvalidOperationException("SetWindowsHookEx failed, error=" + Marshal.GetLastWin32Error());
    }

    public void Uninstall()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return CallNextHookEx(_hook, nCode, wParam, lParam);

        int msg = wParam.ToInt32();
        if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

            // Игнорируем события, которые мы сами инжектируем через SendInput —
            // иначе наши Backspace'ы попадут обратно в логику буфера.
            const uint LLKHF_INJECTED = 0x10;
            if ((data.flags & LLKHF_INJECTED) != 0)
                return CallNextHookEx(_hook, nCode, wParam, lParam);

            var vk = (Keys)data.vkCode;

            // GetAsyncKeyState даёт системно-актуальное состояние, в отличие от GetKeyState
            // (которое читает состояние нашего потока и в LL-хуке может отставать).
            bool shift = (GetAsyncKeyState(0x10) & 0x8000) != 0;
            bool ctrl = (GetAsyncKeyState(0x11) & 0x8000) != 0;
            bool alt = (GetAsyncKeyState(0x12) & 0x8000) != 0;
            bool win = (GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0;

            char? typed = null;
            if (!ctrl && !alt)
                typed = TryResolveChar(data.vkCode, data.scanCode, shift);

            var evt = new KeyEvent
            {
                VirtualKey = vk,
                ScanCode = data.scanCode,
                TypedChar = typed,
                Ctrl = ctrl, Alt = alt, Shift = shift, Win = win,
            };

            try { KeyDown?.Invoke(this, evt); }
            catch { /* hook не должен падать */ }

            if (evt.Handled)
                return (IntPtr)1; // событие "проглочено"
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static char? TryResolveChar(uint vk, uint scan, bool shift)
    {
        var hwnd = GetForegroundWindow();
        uint tid = GetWindowThreadProcessId(hwnd, out _);
        var hkl = GetKeyboardLayout(tid);

        var state = new byte[256];
        GetKeyboardState(state);
        // GetKeyboardState отражает состояние НАШЕГО потока — в LL-хуке оно почти всегда пустое.
        // Поэтому переустанавливаем модификаторы из живых GetAsyncKeyState, и оверрайдим shift.
        state[0x10] = (byte)(shift ? 0x80 : 0);
        state[0x11] = (byte)(((GetAsyncKeyState(0x11) & 0x8000) != 0) ? 0x80 : 0);
        state[0x12] = (byte)(((GetAsyncKeyState(0x12) & 0x8000) != 0) ? 0x80 : 0);
        state[0x14] = (byte)(((GetKeyState(0x14) & 1) != 0) ? 1 : 0); // CapsLock toggle

        var sb = new System.Text.StringBuilder(8);
        // wFlags бит 2 (0x4) = "не менять состояние клавиатуры" (Win10 1607+) —
        // без него мы крадём dead-key state у активного приложения и сами портим себе следующие вызовы.
        const uint NO_STATE_CHANGE = 0x4;
        int n = ToUnicodeEx(vk, scan, state, sb, sb.Capacity, NO_STATE_CHANGE, hkl);
        if (n >= 1 && sb.Length >= 1)
        {
            var c = sb[0];
            if (!char.IsControl(c)) return c;
        }

        // Фолбэк: если это печатная клавиша (A..Z, 0..9, OEM-знаки, пробел) —
        // запоминаем плейсхолдер, чтобы количество backspace при конвертации совпало с реально набранным.
        if (IsPrintableVk(vk))
            return '?';
        return null;
    }

    private static bool IsPrintableVk(uint vk) =>
        (vk >= 0x30 && vk <= 0x39) ||          // 0..9
        (vk >= 0x41 && vk <= 0x5A) ||          // A..Z
        vk == 0x20 ||                          // Space
        (vk >= 0xBA && vk <= 0xC0) ||          // OEM_1..OEM_3 (;=,-./` и кириллица)
        (vk >= 0xDB && vk <= 0xDF) ||          // OEM_4..OEM_8 ([\]'`)
        vk == 0xE2;                            // OEM_102 (\)

    public void Dispose() => Uninstall();
}
