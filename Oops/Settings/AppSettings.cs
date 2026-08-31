using System.Text.Json;
using System.Text.Json.Serialization;
using Oops.Core;

namespace Oops.Settings;

public sealed class AppSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Язык интерфейса: «auto» (по языку Windows), «ru» или «en».
    /// См. <see cref="Core.L10n"/>.
    /// </summary>
    public string Language { get; set; } = Core.L10n.Auto;

    /// <summary>
    /// Мастер первого запуска уже показывали.
    ///
    /// Автозапуска здесь намеренно НЕТ: единственный источник правды —
    /// запись в реестре (<see cref="Autostart"/>). Инсталлятор создаёт её сам,
    /// по галочке в мастере установки, а настройки при первом сохранении
    /// затирали её значением по умолчанию (false) — галочку приходилось
    /// ставить заново руками.
    /// </summary>
    public bool FirstRunCompleted { get; set; } = false;

    /// <summary>Сколько секунд бездействия обнуляют набранный буфер.</summary>
    public int BufferIdleTimeoutSeconds { get; set; } = 30;

    /// <summary>Сколько секунд следующее нажатие продолжает расширять ту же область.</summary>
    public int ExpandWindowSeconds { get; set; } = 2;

    /// <summary>
    /// Печатать результат строго по одному символу. Медленно (фраза в тридцать
    /// символов переписывается около секунды вместо десятков миллисекунд), зато
    /// переживает приёмники, которые теряют события из batched SendInput —
    /// такое встречается в Electron и браузерных полях ввода.
    /// </summary>
    public bool CharByCharTyping { get; set; } = false;

    /// <summary>Проверять обновления на GitHub при запуске (не чаще раза в сутки).</summary>
    public bool AutoCheckUpdates { get; set; } = true;

    /// <summary>Когда последний раз проверяли обновления.</summary>
    public DateTime LastUpdateCheckUtc { get; set; } = DateTime.MinValue;

    public HotkeyConfig ConvertHotkey { get; set; } = HotkeyConfig.Default;
    public HotkeyConfig ChangeCaseHotkey { get; set; } = HotkeyConfig.ChangeCaseDefault;

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Oops", "settings.json");

    /// <summary>
    /// Почему настройки не прочитались, если файл был. Заполняется при загрузке
    /// и показывается один раз при старте: молча вернуть дефолты — значит отобрать
    /// у человека настроенные хоткеи без единого слова, и он решит, что программа
    /// сломалась сама.
    /// </summary>
    [JsonIgnore]
    public string? LoadError { get; private set; }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var s = JsonSerializer.Deserialize<AppSettings>(json);
                if (s != null) { s.Sanitize(); return s; }
                return new AppSettings { LoadError = "settings file is empty" };
            }
        }
        catch (Exception ex)
        {
            return new AppSettings { LoadError = ex.Message };
        }
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

        // Два одинаковых сочетания = второй хоткей мёртв: App проверяет раскладку
        // первой и до регистра дело не доходит вообще — «никакой реакции».
        // Такое могли сохранить старые сборки, где диалог записи ошибался.
        if (ConvertHotkey.SameCombo(ChangeCaseHotkey))
        {
            ChangeCaseHotkey = HotkeyConfig.ChangeCaseDefault;
            if (ConvertHotkey.SameCombo(ChangeCaseHotkey))
                ConvertHotkey = HotkeyConfig.Default;
        }

        if (BufferIdleTimeoutSeconds < 5) BufferIdleTimeoutSeconds = 30;
        if (ExpandWindowSeconds < 1) ExpandWindowSeconds = 2;

        static HotkeyConfig Fix(HotkeyConfig? h, HotkeyConfig fallback)
        {
            if (h == null) return fallback;
            bool isAltShift = h.Alt && h.Shift && !h.Ctrl && !h.Win;
            if (isAltShift) return fallback;
            // Сочетание без единой клавиши сработать не может — только молчать.
            if (!h.Ctrl && !h.Shift && !h.Alt && !h.Win && h.Key == 0) return fallback;
            return h;
        }
    }

    /// <summary>
    /// Сохраняет настройки. Возвращает текст ошибки, если не удалось, — раньше
    /// отказ проглатывался, и человек закрывал окно в уверенности, что хоткеи
    /// переназначены, а после перезапуска получал прежние.
    /// </summary>
    public string? Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Куда пишутся настройки — чтобы показать путь в сообщении об ошибке.</summary>
    public static string Location => FilePath;
}
