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
    /// Снимаем зажатые модификаторы (Ctrl/Alt/Win), затем удерживаем Shift и шлём Left N раз
    /// с задержкой между нажатиями — single-batch SendInput теряет события в Electron/React-полях.
    /// </summary>
    private static void SelectLastN(int n)
    {
        int sz = Marshal.SizeOf<INPUT>();

        // 1) снимаем потенциально зажатые модификаторы пользователя
        var release = new[]
        {
            Key(VK_CONTROL, true), Key(VK_MENU, true),
            Key(VK_LWIN, true),    Key(VK_RWIN, true),
            Key(VK_SHIFT, true),
        };
        SendInput((uint)release.Length, release, sz);
        System.Threading.Thread.Sleep(20);

        // 2) Shift down
        var shiftDown = new[] { Key(VK_SHIFT, false) };
        SendInput(1, shiftDown, sz);

        // 3) Left × N, по одному
        var leftPair = new[] { Key(VK_LEFT, false), Key(VK_LEFT, true) };
        for (int i = 0; i < n; i++)
        {
            SendInput(2, leftPair, sz);
            System.Threading.Thread.Sleep(25);
        }

        // 4) Shift up
        var shiftUp = new[] { Key(VK_SHIFT, true) };
        SendInput(1, shiftUp, sz);
    }

    private static INPUT Key(ushort vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? KEYEVENTF_KEYUP : 0 } }
    };
}
