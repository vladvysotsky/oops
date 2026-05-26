using Microsoft.Win32;

namespace KeyLangSwitcher.Settings;

public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "KeyLangSwitcher";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(ValueName) != null;
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (key == null) return;
        if (enabled)
        {
            var exe = Environment.ProcessPath
                ?? Path.Combine(AppContext.BaseDirectory, "KeyLangSwitcher.exe");
            key.SetValue(ValueName, $"\"{exe}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
