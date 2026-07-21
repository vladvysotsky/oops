using System.Text.Json;
using KeyLangSwitcher.Core;

namespace KeyLangSwitcher.Settings;

public sealed class AppSettings
{
    public bool Enabled { get; set; } = true;
    public bool Autostart { get; set; } = false;

    public HotkeyConfig ConvertHotkey { get; set; } = HotkeyConfig.Default;
    public HotkeyConfig ChangeCaseHotkey { get; set; } = HotkeyConfig.ChangeCaseDefault;

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KeyLangSwitcher", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var s = JsonSerializer.Deserialize<AppSettings>(json);
                if (s != null) return s;
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch { }
    }
}
