using System.Runtime.InteropServices;

namespace Oops.Core;

/// <summary>
/// Отслеживает изменение раскладки активного окна. Если пользователь руками
/// переключил язык (Alt+Shift, Win+Space и т.п.), накопленный буфер символов
/// больше не отражает то, что увидит приёмник, и должен быть сброшен.
/// </summary>
public sealed class LayoutTracker
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint idThread);

    private IntPtr _lastHkl = IntPtr.Zero;

    /// <summary>Возвращает true, если раскладка изменилась с прошлого вызова.</summary>
    public bool LayoutChangedSinceLastCheck()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;
        var tid = GetWindowThreadProcessId(hwnd, out _);
        var hkl = GetKeyboardLayout(tid);
        if (hkl == _lastHkl) return false;
        bool wasInitialized = _lastHkl != IntPtr.Zero;
        _lastHkl = hkl;
        return wasInitialized; // первый вызов — просто запоминаем, не считаем сменой
    }

    public void Reset() => _lastHkl = IntPtr.Zero;
}
