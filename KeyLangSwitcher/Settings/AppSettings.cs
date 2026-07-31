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
                if (s != null) { s.Sanitize(); return s; }
            }
        }
        catch { }
        return new AppSettings();
    }

    /// <summary>
    /// Чинит настройки, пришедшие из старых/битых файлов, чтобы хоткей не «молчал»:
    ///   - null после десериализации (свойства не было в старом файле) → дефолт;
    ///   - Alt+Shift-комбинации → дефолт: Alt+Shift это системный шорткат смены
    ///     раскладки Windows, он перехватывается до нас и хоткей не срабатывает.
    /// </summary>
    private void Sanitize()
    {
        ConvertHotkey = Fix(ConvertHotkey, HotkeyConfig.Default);
        ChangeCaseHotkey = Fix(ChangeCaseHotkey, HotkeyConfig.ChangeCaseDefault);

        static HotkeyConfig Fix(HotkeyConfig? h, HotkeyConfig fallback)
        {
            if (h == null) return fallback;
            bool isAltShift = h.Alt && h.Shift && !h.Ctrl && !h.Win;
            if (isAltShift) return fallback;
            return h;
        }
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
