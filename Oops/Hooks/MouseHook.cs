using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Oops.Hooks;

/// <summary>
/// Low-level mouse hook. Сигналит про клики — это повод сбросить буфер ввода.
/// </summary>
public sealed class MouseHook : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0207;

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private LowLevelMouseProc? _proc;
    private IntPtr _hook = IntPtr.Zero;

    /// <summary>
    /// Поток, в котором живёт хук (см. <see cref="HookThread"/>). Этот хук у
    /// него обязателен по той же причине, что и у клавиатурного: мышиный
    /// LL-хук Windows снимает по тому же таймауту. Снятый хук молчит, клик
    /// перестаёт сбрасывать ленту — и следующая конвертация сотрёт не те
    /// символы, потому что каретку человек уже переставил.
    /// </summary>
    private readonly HookThread? _host;

    public event EventHandler? Clicked;

    public MouseHook() { }

    /// <param name="host">Поток ввода; событие Clicked приходит в нём.</param>
    public MouseHook(HookThread host) => _host = host;

    public void Install()
    {
        if (_host != null) _host.Invoke(InstallCore);
        else InstallCore();
    }

    private void InstallCore()
    {
        if (_hook != IntPtr.Zero) return;
        _proc = HookCallback;
        using var proc = Process.GetCurrentProcess();
        using var mod = proc.MainModule!;
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(mod.ModuleName), 0);
    }

    public void Uninstall()
    {
        if (_host != null) _host.Invoke(UninstallCore);
        else UninstallCore();
    }

    private void UninstallCore()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN || msg == WM_MBUTTONDOWN)
            {
                try { Clicked?.Invoke(this, EventArgs.Empty); } catch { }
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose() => Uninstall();
}
