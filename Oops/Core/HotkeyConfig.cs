using System.Text;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace Oops.Core;

/// <summary>Хоткей: модификаторы + основная клавиша. Допустимо ноль "основной" — тогда хоткей "только модификаторы".</summary>
public sealed class HotkeyConfig
{
    public bool Ctrl { get; set; }
    public bool Shift { get; set; }
    public bool Alt { get; set; }
    public bool Win { get; set; }

    /// <summary>VK_ код. 0 == нет основной клавиши (например, Ctrl+Win-only мы не поддерживаем — Win считается модификатором).</summary>
    public int Key { get; set; }

    [JsonIgnore]
    public Keys KeyEnum => (Keys)Key;

    public static HotkeyConfig Default => new()
    {
        Ctrl = true,
        Win = true,
        // По умолчанию активирует на нажатии любой "основной" клавиши вместе с Ctrl+Win.
        // Спец-режим: если Key == 0, ловим момент когда Ctrl и Win одновременно зажаты и оба модификатора —
        // см. App.OnKeyDown.
        Key = 0,
    };

    /// <summary>
    /// Смена регистра — Alt+Win по умолчанию (modifier-only).
    /// НЕ использовать Alt+Shift: это системный шорткат смены раскладки Windows,
    /// он перехватывается до нас и хоткей никогда не срабатывает.
    /// </summary>
    public static HotkeyConfig ChangeCaseDefault => new()
    {
        Alt = true,
        Win = true,
        Key = 0,
    };

    /// <summary>
    /// Перевод — Ctrl+Alt+Win по умолчанию (modifier-only, три клавиши).
    ///
    /// Не Shift+Win: на нём висит Win+Shift+S — снимок области экрана. Хоткей
    /// глотает клавишу, замкнувшую сочетание, и снимок перестал бы работать.
    /// Ctrl+Alt без Win системе тоже трогать нельзя (это AltGr на части
    /// раскладок), но наше сравнение требует ещё и Win, так что AltGr сам по
    /// себе под него не подходит.
    /// </summary>
    public static HotkeyConfig TranslateDefault => new()
    {
        Ctrl = true,
        Alt = true,
        Win = true,
        Key = 0,
    };

    /// <summary>Одно и то же сочетание (без учёта ссылочного равенства).</summary>
    public bool SameCombo(HotkeyConfig other) =>
        other != null &&
        Ctrl == other.Ctrl && Shift == other.Shift &&
        Alt == other.Alt && Win == other.Win && Key == other.Key;

    public bool Matches(Keys vk, bool ctrl, bool shift, bool alt, bool win)
    {
        if (Ctrl != ctrl) return false;
        if (Shift != shift) return false;
        if (Alt != alt) return false;
        if (Win != win) return false;
        if (Key == 0)
        {
            // Триггер по самой "поздней" нажатой клавише — App дёрнет, как только все нужные модификаторы зажаты.
            // Совпадение требует, чтобы текущая клавиша БЫЛА одним из модификаторов (Ctrl/Win/Shift/Alt).
            return vk is Keys.LControlKey or Keys.RControlKey or Keys.ControlKey
                       or Keys.LWin or Keys.RWin
                       or Keys.LShiftKey or Keys.RShiftKey or Keys.ShiftKey
                       or Keys.LMenu or Keys.RMenu or Keys.Menu;
        }
        return (int)vk == Key;
    }

    /// <summary>
    /// Порядок Ctrl → Alt → Shift → Win: как в документации и как принято в
    /// Windows. Раньше Win шла второй, и окно настроек показывало «Win+Alt»
    /// там, где везде написано «Alt+Win» — при разборе, почему хоткей молчит,
    /// такое расхождение стоит дороже, чем выглядит.
    /// </summary>
    public override string ToString()
    {
        var sb = new StringBuilder();
        if (Ctrl) sb.Append("Ctrl+");
        if (Alt) sb.Append("Alt+");
        if (Shift) sb.Append("Shift+");
        if (Win) sb.Append("Win+");
        if (Key != 0) sb.Append(((Keys)Key).ToString());
        else if (sb.Length > 0) sb.Length--; // убрать висящий '+'
        return sb.ToString();
    }
}
