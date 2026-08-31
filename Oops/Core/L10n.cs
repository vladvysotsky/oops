using System.Globalization;
using System.Text.Json;

namespace Oops.Core;

/// <summary>
/// Строки интерфейса на двух языках.
///
/// Почему не .resx и не сателлитные сборки — при PublishSingleFile сателлиты
/// НЕ попадают внутрь exe: они раскладываются в подпапки культур рядом с ним,
/// а портативный архив у нас собирается из одного файла и потерял бы их.
/// Поэтому строки живут встроенными ресурсами прямо в сборке.
///
/// Отсутствующий ключ возвращает сам ключ: на экране это видно сразу, но
/// программа не падает. Совпадение наборов ключей проверяется тестом —
/// забытый перевод ловится до запуска.
/// </summary>
public static class L10n
{
    /// <summary>Язык интерфейса. «auto» — по языку Windows.</summary>
    public const string Auto = "auto";
    public const string Russian = "ru";
    public const string English = "en";

    private static Dictionary<string, string> _strings = new();
    private static string _language = Russian;

    /// <summary>Текущий язык: «ru» или «en» (уже разрешённый, не «auto»).</summary>
    public static string Language => _language;

    /// <summary>
    /// Загружает строки. Вызывать один раз при старте, до создания любого окна.
    /// Значения фиксируются, как палитра в Theme: WinForms всё равно не
    /// перерисует уже созданные контролы.
    /// </summary>
    public static void Init(string? preference)
    {
        _language = Resolve(preference);
        _strings = TryLoad(_language);

        // Русский — исходный язык, в нём заведомо есть все ключи. Английский
        // дополняем им же: пропущенный перевод покажет русский текст, а не
        // голый ключ. Тест на расхождение всё равно упадёт, но пользователь
        // сломанного экрана не увидит.
        if (_language != Russian)
        {
            foreach (var (key, value) in TryLoad(Russian))
                _strings.TryAdd(key, value);
        }
    }

    /// <summary>Разрешает «auto» в конкретный язык по настройкам Windows.</summary>
    public static string Resolve(string? preference)
    {
        if (preference == Russian || preference == English) return preference;
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            .Equals(Russian, StringComparison.OrdinalIgnoreCase) ? Russian : English;
    }

    /// <summary>Строка по ключу. Неизвестный ключ возвращается как есть.</summary>
    public static string T(string key) =>
        _strings.TryGetValue(key, out var value) ? value : key;

    /// <summary>Строка с подстановкой: T("update.available", version).</summary>
    public static string T(string key, params object?[] args)
    {
        var format = T(key);
        try { return string.Format(CultureInfo.CurrentCulture, format, args); }
        catch (FormatException) { return format; }   // кривой плейсхолдер не должен ронять окно
    }

    /// <summary>
    /// Читает словарь, не бросая исключений. Инициализация языка не имеет права
    /// уронить запуск: без строк интерфейс покажет ключи — уродливо, но человек
    /// хотя бы сможет открыть настройки и переключить язык. Раньше здесь падало
    /// приложение целиком, ещё до появления трея.
    /// </summary>
    private static Dictionary<string, string> TryLoad(string language)
    {
        try { return Load(language); }
        catch { return new Dictionary<string, string>(); }
    }

    /// <summary>Читает встроенный словарь. Публичный — им же пользуется тест.</summary>
    public static Dictionary<string, string> Load(string language)
    {
        var name = $"Oops.Resources.lang_{language}.json";
        using var stream = typeof(L10n).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Не найден ресурс со строками: {name}");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new();
    }
}
