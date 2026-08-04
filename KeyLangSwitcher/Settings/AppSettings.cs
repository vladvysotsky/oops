using System.Text.Json;
using KeyLangSwitcher.Core;

namespace KeyLangSwitcher.Settings;

public sealed class AppSettings
{
    public bool Enabled { get; set; } = true;
    public bool Autostart { get; set; } = false;

    /// <summary>Сколько секунд бездействия обнуляют набранный буфер.</summary>
    public int BufferIdleTimeoutSeconds { get; set; } = 30;

    /// <summary>Сколько секунд следующее нажатие продолжает расширять ту же область.</summary>
    public int ExpandWindowSeconds { get; set; } = 2;

    /// <summary>Проверять обновления на GitHub при запуске (не чаще раза в сутки).</summary>
    public bool AutoCheckUpdates { get; set; } = true;

    /// <summary>Когда последний раз проверяли обновления.</summary>
    public DateTime LastUpdateCheckUtc { get; set; } = DateTime.MinValue;

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
    /// Чинит настройки из старых/битых файлов, чтобы хоткей не «молчал»:
    ///   - null после десериализации (свойства не было в старом файле) → дефолт;
    ///   - Alt+Shift → дефолт: это системный шорткат смены раскладки Windows,
    ///     он перехватывается системой и до нашего хука в рабочем виде не доходит.
    /// </summary>
    private void Sanitize()
    {
        ConvertHotkey = Fix(ConvertHotkey, HotkeyConfig.Default);
        ChangeCaseHotkey = Fix(ChangeCaseHotkey, HotkeyConfig.ChangeCaseDefault);
        if (BufferIdleTimeoutSeconds < 5) BufferIdleTimeoutSeconds = 30;
        if (ExpandWindowSeconds < 1) ExpandWindowSeconds = 2;

        static HotkeyConfig Fix(HotkeyConfig? h, HotkeyConfig fallback)
        {
            if (h == null) return fallback;
            bool isAltShift = h.Alt && h.Shift && !h.Ctrl && !h.Win;
            return isAltShift ? fallback : h;
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
