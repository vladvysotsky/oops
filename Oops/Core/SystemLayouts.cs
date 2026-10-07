using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Oops.Core;

/// <summary>
/// Раскладки, установленные в Windows, — с картой «клавиша ↔ символ» для каждой.
///
/// Карту строит сама Windows: для каждой клавиши с Shift и без спрашиваем
/// ToUnicodeEx, какой символ она даёт в этой раскладке. Так работает любая
/// раскладка, которую человек добавил в систему, — украинская, казахская,
/// немецкая, — а не только прошитая таблица ЙЦУКЕН↔QWERTY.
/// </summary>
public static class SystemLayouts
{
    [DllImport("user32.dll")]
    private static extern int GetKeyboardLayoutList(int nBuff, [Out] IntPtr[] lpList);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte[] lpKeyState,
        [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszBuff, int cchBuff,
        uint wFlags, IntPtr dwhkl);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyEx(uint uCode, uint uMapType, IntPtr dwhkl);

    private const uint MAPVK_VK_TO_VSC = 0;

    /// <summary>
    /// «Не менять состояние клавиатуры» (Windows 10 1607+). Без него опрос
    /// мёртвой клавиши (´, ^ в немецкой раскладке) оставил бы её «нажатой», и
    /// следующая буква у человека вышла бы с чужим диакритическим знаком.
    /// </summary>
    private const uint NO_STATE_CHANGE = 0x4;

    private const int VK_SHIFT = 0x10;
    private const int VK_LSHIFT = 0xA0;

    /// <summary>
    /// Клавиши, которые печатают символы: цифры, буквы, знаки. Порядок важен —
    /// символ, который дают две клавиши, закрепляется за первой (см.
    /// <see cref="KeyboardLayout"/>), поэтому буквы идут раньше знаков.
    /// </summary>
    private static IEnumerable<ushort> PrintableKeys()
    {
        for (ushort vk = 0x41; vk <= 0x5A; vk++) yield return vk;     // A..Z
        foreach (ushort vk in new ushort[] { 0xBA, 0xBB, 0xBC, 0xBD, 0xBE, 0xBF, 0xC0,  // ;=,-./`
                                             0xDB, 0xDC, 0xDD, 0xDE, 0xDF, 0xE2 })     // [\]'  OEM_8 OEM_102
            yield return vk;
        for (ushort vk = 0x30; vk <= 0x39; vk++) yield return vk;     // 0..9
    }

    /// <summary>Установленные HKL одной строкой — чтобы дёшево заметить, что список поменялся.</summary>
    public static string Signature() => string.Join(",", Handles().Select(h => Id(h)));

    /// <summary>
    /// Все установленные раскладки с картами. Раскладку, из которой не удалось
    /// прочитать ни одной буквы, пропускаем: переводить в неё нечего.
    /// </summary>
    public static IReadOnlyList<KeyboardLayout> Installed()
    {
        var handles = Handles();
        var result = new List<KeyboardLayout>(handles.Length);
        var names = handles.Select(h => DisplayName(h)).ToList();

        for (int i = 0; i < handles.Length; i++)
        {
            var hkl = handles[i];
            var keys = ReadKeys(hkl);
            if (!keys.Any(k => char.IsLetter(k.Char))) continue;

            // Две раскладки одного языка (английская США и Великобритании,
            // обычная и машинописная русская) иначе выглядели бы в настройках
            // одинаково, и выбрать нужную было бы невозможно.
            var name = names[i];
            if (names.Count(n => n == name) > 1) name = $"{name} ({Culture(hkl)?.Name ?? Id(hkl)})";

            result.Add(new KeyboardLayout(Id(hkl), Language(hkl), name, hkl, keys));
        }
        return result;
    }

    private static IntPtr[] Handles()
    {
        int count = GetKeyboardLayoutList(0, Array.Empty<IntPtr>());
        if (count <= 0) return Array.Empty<IntPtr>();
        var list = new IntPtr[count];
        count = GetKeyboardLayoutList(count, list);
        return count <= 0 ? Array.Empty<IntPtr>() : list.Take(count).ToArray();
    }

    private static List<(KeyStroke Key, char Char)> ReadKeys(IntPtr hkl)
    {
        var keys = new List<(KeyStroke, char)>();
        var state = new byte[256];
        var buffer = new StringBuilder(8);

        foreach (var shift in new[] { false, true })
        {
            state[VK_SHIFT] = state[VK_LSHIFT] = (byte)(shift ? 0x80 : 0);
            foreach (var vk in PrintableKeys())
            {
                buffer.Clear();
                uint scan = MapVirtualKeyEx(vk, MAPVK_VK_TO_VSC, hkl);
                int n = ToUnicodeEx(vk, scan, state, buffer, buffer.Capacity, NO_STATE_CHANGE, hkl);
                // Ровно один символ. Мёртвая клавиша (-1) и лигатуры (2+) в
                // позиционную карту не годятся: с ними перестала бы сохраняться
                // длина текста, а на ней держится стирание.
                if (n != 1 || buffer.Length < 1) continue;
                var c = buffer[0];
                if (char.IsControl(c) || char.IsSurrogate(c)) continue;
                keys.Add((new KeyStroke(vk, shift), c));
            }
        }
        return keys;
    }

    /// <summary>
    /// Как HKL записывается в настройки. Восемь шестнадцатеричных цифр: на
    /// 64-битной системе HKL вроде 0xF0020409 приходит знакорасширенным, и без
    /// маски одна и та же раскладка давала бы разные строки.
    /// </summary>
    private static string Id(IntPtr hkl) => "0x" + ((ulong)hkl.ToInt64() & 0xFFFFFFFF).ToString("X8");

    private static CultureInfo? Culture(IntPtr hkl)
    {
        try { return CultureInfo.GetCultureInfo((int)(hkl.ToInt64() & 0xFFFF)); }
        catch { return null; }
    }

    private static string Language(IntPtr hkl) => Culture(hkl)?.TwoLetterISOLanguageName ?? string.Empty;

    /// <summary>
    /// Название языка на нём самом — «Русский», «Українська», «English»:
    /// человек, открывший чужую локаль, всё равно узнает свою раскладку.
    /// </summary>
    private static string DisplayName(IntPtr hkl)
    {
        var culture = Culture(hkl);
        if (culture == null) return Id(hkl);
        var neutral = culture.IsNeutralCulture ? culture : culture.Parent;
        var name = string.IsNullOrEmpty(neutral.NativeName) ? culture.NativeName : neutral.NativeName;
        return name.Length == 0 ? Id(hkl) : char.ToUpper(name[0], culture) + name[1..];
    }
}
