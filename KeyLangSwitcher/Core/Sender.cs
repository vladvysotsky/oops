using System.Runtime.InteropServices;

namespace KeyLangSwitcher.Core;

/// <summary>
/// Обёртка над SendInput для отправки Backspace и юникодных символов.
/// </summary>
public static class Sender
{
    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const ushort VK_BACK = 0x08;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_MENU = 0x12;
    private const ushort VK_LWIN = 0x5B;
    private const ushort VK_RWIN = 0x5C;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL, wParamH;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int nVirtKey);

    /// <summary>
    /// Нейтрализует «тап» Alt. Если Windows видит Alt-down → Alt-up без клавиш
    /// между ними, она активирует строку меню окна — фокус уходит из поля ввода,
    /// и последующий SendUnicode печатает в никуда. Безобидный Ctrl-тап, вставленный
    /// пока Alt ещё зажат, ломает этот шаблон: Alt-down → Ctrl → Alt-up меню не активирует.
    /// Ctrl без последующей буквы не делает ничего ни в одном приложении.
    /// </summary>
    public static void CancelAltMenuActivation()
    {
        if ((GetAsyncKeyState(VK_MENU) & 0x8000) == 0) return;
        var inputs = new[]
        {
            new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_CONTROL } } },
            new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_CONTROL, dwFlags = KEYEVENTF_KEYUP } } },
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    /// <summary>
    /// Ждёт, пока пользователь физически отпустит все модификаторы хоткея.
    /// Без этого зажатый Ctrl превратит наши Backspace в Ctrl+Backspace (удаление
    /// слова целиком), а зажатый Alt/Win исказит ввод.
    /// </summary>
    public static void WaitForModifiersReleased(int maxMs = 1000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(maxMs);
        while (DateTime.UtcNow < deadline && AnyModifierDown())
            System.Threading.Thread.Sleep(15);
        System.Threading.Thread.Sleep(40); // даём системе доставить key-up'ы
    }

    private static bool AnyModifierDown() =>
        (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0 ||
        (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0 ||
        (GetAsyncKeyState(VK_MENU) & 0x8000) != 0 ||
        (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 ||
        (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;

    /// <summary>
    /// Снимает потенциально зажатые пользователем модификаторы хоткея (Ctrl/Shift/Alt/Win).
    /// Должно вызываться перед последовательностью бэкспейсов / Unicode-ввода,
    /// иначе зажатый Ctrl превратит Backspace в Ctrl+Backspace (удаление слова),
    /// а Win-down под Backspace может сработать как системный шорткат.
    /// </summary>
    public static void ReleaseHotkeyModifiers()
    {
        var inputs = new[]
        {
            new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_CONTROL, dwFlags = KEYEVENTF_KEYUP } } },
            new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_SHIFT,   dwFlags = KEYEVENTF_KEYUP } } },
            new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_MENU,    dwFlags = KEYEVENTF_KEYUP } } },
            new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_LWIN,    dwFlags = KEYEVENTF_KEYUP } } },
            new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_RWIN,    dwFlags = KEYEVENTF_KEYUP } } },
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        System.Threading.Thread.Sleep(20); // даём приложению переварить key-up'ы
    }

    /// <summary>Перемещает каретку вправо N раз (полезно перед SendBackspaces, если буфер-курсор не в конце).</summary>
    public static void SendRightArrow(int count)
    {
        if (count <= 0) return;
        const ushort VK_RIGHT = 0x27;
        var pair = new INPUT[2];
        pair[0] = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_RIGHT } } };
        pair[1] = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_RIGHT, dwFlags = KEYEVENTF_KEYUP } } };
        int sz = Marshal.SizeOf<INPUT>();
        for (int i = 0; i < count; i++)
        {
            SendInput(2, pair, sz);
            System.Threading.Thread.Sleep(20);
        }
    }

    public static void SendBackspaces(int count)
    {
        if (count <= 0) return;
        // Шлём по одному, с микро-задержкой между нажатиями.
        // Electron/браузерные текстарии теряют события из больших batched SendInput-ов.
        var pair = new INPUT[2];
        pair[0] = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_BACK } } };
        pair[1] = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_BACK, dwFlags = KEYEVENTF_KEYUP } } };
        int sz = Marshal.SizeOf<INPUT>();
        for (int i = 0; i < count; i++)
        {
            SendInput(2, pair, sz);
            System.Threading.Thread.Sleep(50);
        }
    }

    /// <summary>Отправляет строку как последовательность KEYEVENTF_UNICODE — работает в любой раскладке.</summary>
    public static void SendUnicode(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        int sz = Marshal.SizeOf<INPUT>();
        var pair = new INPUT[2];
        foreach (var ch in text)
        {
            pair[0] = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wScan = ch, dwFlags = KEYEVENTF_UNICODE } } };
            pair[1] = new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wScan = ch, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP } } };
            SendInput(2, pair, sz);
            System.Threading.Thread.Sleep(25);
        }
    }

    /// <summary>
    /// Эмуляция Ctrl+C / Ctrl+V. Перед основной комбинацией снимаем все "лишние"
    /// модификаторы (Shift/Alt/Win), которые мог удерживать пользователь от хоткея —
    /// иначе приложение увидит, например, Ctrl+Alt+C вместо чистого Ctrl+C и copy не сработает.
    /// </summary>
    public static void SendCtrlKey(char key)
    {
        ushort vk = (ushort)char.ToUpper(key);
        var inputs = new[]
        {
            // снимаем потенциально зажатые модификаторы
            new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_SHIFT, dwFlags = KEYEVENTF_KEYUP } } },
            new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_MENU,  dwFlags = KEYEVENTF_KEYUP } } },
            new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_LWIN,  dwFlags = KEYEVENTF_KEYUP } } },
            new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_RWIN,  dwFlags = KEYEVENTF_KEYUP } } },
            // и саму комбинацию Ctrl+key
            new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_CONTROL } } },
            new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = vk } } },
            new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = KEYEVENTF_KEYUP } } },
            new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = VK_CONTROL, dwFlags = KEYEVENTF_KEYUP } } },
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }
}
