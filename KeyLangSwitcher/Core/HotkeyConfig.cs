using System.Text;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace KeyLangSwitcher.Core;

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
    /// Смена регистра выделенного текста — Ctrl+Shift+U по умолчанию.
    /// НЕ использовать Alt+Shift: это системный шорткат переключения раскладки
    /// Windows, он перехватывается до нас и ломает хоткей.
    /// </summary>
    public static HotkeyConfig ChangeCaseDefault => new()
    {
        Ctrl = true,
        Shift = true,
        Key = (int)Keys.U,
    };

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

    public override string ToString()
    {
        var sb = new StringBuilder();
        if (Ctrl) sb.Append("Ctrl+");
        if (Win) sb.Append("Win+");
        if (Alt) sb.Append("Alt+");
        if (Shift) sb.Append("Shift+");
        if (Key != 0) sb.Append(((Keys)Key).ToString());
        else if (sb.Length > 0) sb.Length--; // убрать висящий '+'
        return sb.ToString();
    }
}
