using System.Windows.Forms;
using KeyLangSwitcher.Settings;
using KeyLangSwitcher.UI;

namespace KeyLangSwitcher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Single-instance guard
        using var mutex = new System.Threading.Mutex(initiallyOwned: true,
            name: "Global\\KeyLangSwitcher_SingleInstance", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("KeyLangSwitcher уже запущен (см. иконку в трее).",
                "KeyLangSwitcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        // Гарантируем UI SynchronizationContext до создания App
        WindowsFormsSynchronizationContext.AutoInstall = true;
        _ = new Control(); // принудительно создаёт SyncContext в этом потоке

        var settings = AppSettings.Load();
        System.Diagnostics.Debug.WriteLine($"[startup] settings loaded. hotkey={settings.ConvertHotkey} enabled={settings.Enabled}");
        var app = new App(settings);
        System.Diagnostics.Debug.WriteLine("[startup] hooks installed");
        var ctx = new TrayContext(app);
        System.Diagnostics.Debug.WriteLine("[startup] tray ready, entering message loop");

        Application.Run(ctx);
    }
}
