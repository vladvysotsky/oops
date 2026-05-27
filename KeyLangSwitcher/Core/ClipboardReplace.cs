using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KeyLangSwitcher.Core;

/// <summary>
/// Заменяет последние N символов перед курсором новым текстом через выделение + paste.
/// Надёжнее, чем последовательные Backspace+SendInput: React/Electron-инпуты атомарно
/// обрабатывают одно событие Ctrl+V, тогда как длинные серии бэкспейсов схлопывают.
/// </summary>
public static class ClipboardReplace
{
    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_MENU = 0x12;
    private const ushort VK_LWIN = 0x5B;
    private const ushort VK_RWIN = 0x5C;
    private const ushort VK_LEFT = 0x25;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public int type; public InputUnion u; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion { [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public static void ReplaceLastN(int n, string newText)
    {
        if (n <= 0) return;

        IDataObject? original = null;
        try { original = Clipboard.GetDataObject(); } catch { }

        try { Clipboard.SetText(newText); } catch { return; }

        SelectLastN(n);
        System.Threading.Thread.Sleep(60);

        Sender.SendCtrlKey('V');

        System.Threading.Thread.Sleep(150);
        if (original != null)
            try { Clipboard.SetDataObject(original, copy: true); } catch { }
    }

    /// <summary>
    /// Сначала снимаем потенциально зажатые пользователем модификаторы хоткея
    /// (Ctrl/Alt/Win) — иначе наш Shift+Left приложение увидит как Ctrl+Alt+Shift+Left
    /// и выделение либо сломается, либо съест больше/меньше нужного.
    /// Затем Shift down, Left down/up * N, Shift up — одной пачкой SendInput.
    /// </summary>
    private static void SelectLastN(int n)
    {
        var inputs = new INPUT[5 + 2 + n * 2];
        int idx = 0;
        inputs[idx++] = Key(VK_CONTROL, true);
        inputs[idx++] = Key(VK_MENU,    true);
        inputs[idx++] = Key(VK_LWIN,    true);
        inputs[idx++] = Key(VK_RWIN,    true);
        inputs[idx++] = Key(VK_SHIFT,   true); // на случай, если ранее остался "висящий" Shift
        inputs[idx++] = Key(VK_SHIFT, false);
        for (int i = 0; i < n; i++)
        {
            inputs[idx++] = Key(VK_LEFT, false);
            inputs[idx++] = Key(VK_LEFT, true);
        }
        inputs[idx++] = Key(VK_SHIFT, true);
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static INPUT Key(ushort vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? KEYEVENTF_KEYUP : 0 } }
    };
}
