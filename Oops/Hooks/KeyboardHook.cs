using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Oops.Hooks;

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

    /// <summary>
    /// Физически зажатые сейчас клавиши. Нужны, чтобы отличить автоповтор от
    /// нового нажатия: Windows шлёт поток WM_KEYDOWN, пока клавишу держат, и
    /// modifier-only хоткей (Ctrl+Win) иначе срабатывал бы десятки раз за одно
    /// удержание.
    /// </summary>
    private readonly HashSet<uint> _physicallyDown = new();

    public event EventHandler<KeyEvent>? KeyDown;

    /// <summary>
    /// Отпускание клавиши. Нужно тем, кто глотает нажатия: проглоченное хуком
    /// событие НЕ обновляет состояние клавиш в системе, и спрашивать
    /// GetAsyncKeyState «всё ли отпущено» после этого бессмысленно — система
    /// ответит «да», потому что нажатия она не видела. Единственный способ вести
    /// список зажатых клавиш в таком режиме — считать нажатия и отпускания самим.
    /// </summary>
    public event EventHandler<KeyEvent>? KeyUp;

    public sealed class KeyEvent
    {
        public Keys VirtualKey { get; init; }
        public uint ScanCode { get; init; }
        public char? TypedChar { get; init; }
        public bool Handled { get; set; }
        /// <summary>Событие пришло от автоповтора удерживаемой клавиши.</summary>
        public bool IsRepeat { get; init; }
        public bool Ctrl { get; init; }
        public bool Alt { get; init; }
        public bool Shift { get; init; }
        public bool Win { get; init; }
    }

    public void Install()
    {
        if (_hook != IntPtr.Zero) return;
        // Что было зажато до установки хука, мы не видели, а отпускание увидим и
        // вычтем из пустого множества. Начинаем с чистого листа, иначе застрявшая
        // клавиша заставила бы модификатор считаться зажатым навсегда.
        _physicallyDown.Clear();
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

        const uint LLKHF_INJECTED = 0x10;
        int msg = wParam.ToInt32();

        if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
        {
            var up = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if ((up.flags & LLKHF_INJECTED) != 0)
                return CallNextHookEx(_hook, nCode, wParam, lParam);

            _physicallyDown.Remove(up.vkCode);

            var upHandler = KeyUp;
            if (upHandler != null)
            {
                var upEvt = new KeyEvent
                {
                    VirtualKey = (Keys)up.vkCode,
                    ScanCode = up.scanCode,
                };
                try { upHandler(this, upEvt); }
                catch { /* hook не должен падать */ }

                if (upEvt.Handled) return (IntPtr)1;
            }

            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

            // Игнорируем события, которые мы сами инжектируем через SendInput —
            // иначе наши Backspace'ы попадут обратно в логику буфера.
            if ((data.flags & LLKHF_INJECTED) != 0)
                return CallNextHookEx(_hook, nCode, wParam, lParam);

            // Если клавиша уже была зажата — это автоповтор, а не новое нажатие.
            bool isRepeat = !_physicallyDown.Add(data.vkCode);

            var vk = (Keys)data.vkCode;

            // Состояние модификаторов берём из ТРЁХ источников, объединяя по «или».
            //
            // Спрашивать только GetAsyncKeyState нельзя, и это не теория:
            // сочетание «Alt, потом Win» не собиралось в аккорд — на нажатии Win
            // система отвечала, что Alt отпущен, хотя его держали. Причин у неё
            // как минимум две, и обе наши:
            //   - проглоченное хуком нажатие (Handled = true) вообще не обновляет
            //     состояние клавиш в системе;
            //   - ReleaseHotkeyModifiers и CancelMenuActivation ИНЖЕКТИРУЮТ
            //     отпускание Alt/Win, и после этого система считает клавишу
            //     отпущенной до конца удержания, хотя палец на ней.
            //
            // Поэтому основной источник — _physicallyDown: он собран из самого
            // потока событий, до всякой обработки, и врать не может. Хук отдаёт
            // конкретные L/R-варианты (0xA4/0xA5 для Alt), общий VK_MENU в него
            // не приходит.
            //
            // GetAsyncKeyState оставлен подстраховкой на случай клавиш, зажатых
            // ещё до установки хука: их в _physicallyDown нет.
            // Третий источник — сама текущая клавиша: в момент её WM_KEYDOWN
            // система может ещё не успеть обновить состояние.
            bool shift = Held(0xA0, 0xA1, 0x10)
                || vk is Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey;
            bool ctrl = Held(0xA2, 0xA3, 0x11)
                || vk is Keys.ControlKey or Keys.LControlKey or Keys.RControlKey;
            bool alt = Held(0xA4, 0xA5, 0x12)
                || vk is Keys.Menu or Keys.LMenu or Keys.RMenu;
            bool win = Held(0x5B, 0x5C)
                || vk is Keys.LWin or Keys.RWin;

            char? typed = null;
            if (!ctrl && !alt)
                typed = TryResolveChar(data.vkCode, data.scanCode, shift);

            var evt = new KeyEvent
            {
                VirtualKey = vk,
                ScanCode = data.scanCode,
                TypedChar = typed,
                IsRepeat = isRepeat,
                Ctrl = ctrl, Alt = alt, Shift = shift, Win = win,
            };

            try { KeyDown?.Invoke(this, evt); }
            catch { /* hook не должен падать */ }

            if (evt.Handled)
                return (IntPtr)1; // событие "проглочено"
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    /// <summary>
    /// Зажата ли хоть одна из перечисленных клавиш — по нашему списку нажатых
    /// или, как подстраховка, по мнению системы.
    /// </summary>
    private bool Held(params uint[] vks)
    {
        foreach (var v in vks)
        {
            if (_physicallyDown.Contains(v)) return true;
            if ((GetAsyncKeyState((int)v) & 0x8000) != 0) return true;
        }
        return false;
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
