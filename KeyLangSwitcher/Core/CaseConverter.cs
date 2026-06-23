namespace KeyLangSwitcher.Core;

/// <summary>
/// Меняет регистр текста в буфере: если есть заглавная — всё к нижнему,
/// иначе всё к верхнему. Работает с накопленным буфером (тем, что пользователь
/// только что напечатал), напрямую через Backspace+SendUnicode, без clipboard.
/// </summary>
public static class CaseConverter
{
    /// <summary>
    /// Если есть заглавные → всё к нижнему, иначе всё к верхнему.
    /// </summary>
    public static string Toggle(string text)
    {
        bool hasUpper = false;
        foreach (var c in text) if (char.IsUpper(c)) { hasUpper = true; break; }
        return hasUpper ? text.ToLower() : text.ToUpper();
    }
}
