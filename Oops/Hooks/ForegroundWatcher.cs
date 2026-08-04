using System.Runtime.InteropServices;

namespace Oops.Hooks;

/// <summary>
/// Сигналит при смене активного окна — повод сбросить буфер ввода.
/// </summary>
public sealed class ForegroundWatcher : IDisposable
{
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType,
        IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    private WinEventDelegate? _proc;
    private IntPtr _hook = IntPtr.Zero;

    public event EventHandler? ForegroundChanged;

    public void Install()
    {
        if (_hook != IntPtr.Zero) return;
        _proc = OnEvent;
        _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _proc, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    public void Uninstall()
    {
        if (_hook != IntPtr.Zero) { UnhookWinEvent(_hook); _hook = IntPtr.Zero; }
    }

    private void OnEvent(IntPtr hHook, uint type, IntPtr hwnd, int idObject, int idChild,
        uint thread, uint time)
    {
        try { ForegroundChanged?.Invoke(this, EventArgs.Empty); } catch { }
    }

    public void Dispose() => Uninstall();
}
