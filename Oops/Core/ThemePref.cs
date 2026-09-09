namespace Oops.Core;

/// <summary>
/// Предпочтение темы оформления: «auto» (по системе), «light» или «dark».
///
/// Константы вынесены в Core, чтобы <see cref="Oops.Settings.AppSettings"/>
/// не тянул за собой UI: значения хранятся в settings.json и читаются до
/// того, как WinForms вообще инициализирован. Сама палитра и её применение —
/// в <see cref="Oops.UI.Theme"/>.
/// </summary>
public static class ThemePref
{
    /// <summary>Следовать текущей теме Windows.</summary>
    public const string Auto = "auto";

    /// <summary>Всегда светлая палитра.</summary>
    public const string Light = "light";

    /// <summary>Всегда тёмная палитра.</summary>
    public const string Dark = "dark";

    /// <summary>Значение записано корректно (не мусор из старого файла).</summary>
    public static bool IsValid(string? value) =>
        value == Auto || value == Light || value == Dark;

    /// <summary>Битое значение чинится молча — как и все остальные настройки.</summary>
    public static string Sanitize(string? value) => IsValid(value) ? value! : Auto;
}
