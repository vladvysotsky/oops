using System.Runtime.InteropServices;

namespace Oops.Core;

/// <summary>
/// Отслеживает изменение раскладки активного окна. Если пользователь руками
/// переключил язык (Alt+Shift, Win+Space и т.п.), накопленный буфер символов
/// больше не отражает то, что увидит приёмник, и должен быть сброшен.
///
/// ВАЖНО: приложение само переключает раскладку после конвертации. Такие смены
/// нужно отличать от пользовательских, иначе первое же нажатие хоткея сбросит
/// ленту и режим расширяющейся области сломается — второе нажатие окажется
/// работать не с чем.
/// </summary>
public sealed class LayoutTracker
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint idThread);

    /// <summary>
    /// Сколько времени после нашего переключения смены раскладки считаются нашими.
    /// Переключение идёт через PostMessage и применяется асинхронно, поэтому
    /// момент, когда GetKeyboardLayout начнёт отдавать новое значение, заранее
    /// неизвестен — отсюда окно, а не одноразовый флаг.
    /// </summary>
    private static readonly TimeSpan SelfSwitchWindow = TimeSpan.FromSeconds(2);

    private IntPtr _lastHkl = IntPtr.Zero;
    private DateTime _selfSwitchUntilUtc = DateTime.MinValue;

    /// <summary>Возвращает true, только если раскладку сменил пользователь, а не мы.</summary>
    public bool UserChangedLayout()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;

        var tid = GetWindowThreadProcessId(hwnd, out _);
        var hkl = GetKeyboardLayout(tid);
        if (hkl == _lastHkl) return false;

        bool wasInitialized = _lastHkl != IntPtr.Zero;
        _lastHkl = hkl;

        // Первый вызов — просто запоминаем текущую раскладку.
        if (!wasInitialized) return false;

        // Смена в пределах окна после нашего переключения — наша, не пользователя.
        if (DateTime.UtcNow <= _selfSwitchUntilUtc) return false;

        return true;
    }

    /// <summary>Сообщить трекеру, что раскладку сейчас переключили мы сами.</summary>
    public void NoteSelfSwitch() => _selfSwitchUntilUtc = DateTime.UtcNow + SelfSwitchWindow;

    public void Reset()
    {
        _lastHkl = IntPtr.Zero;
        _selfSwitchUntilUtc = DateTime.MinValue;
    }
}
