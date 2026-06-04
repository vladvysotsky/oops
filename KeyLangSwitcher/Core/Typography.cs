namespace KeyLangSwitcher.Core;

/// <summary>
/// Мелкие типографические автокоррекции, применяемые к завершённому слову
/// (на разделителе). Каждая правка возвращает (исправленное_слово, длина_исходного),
/// чтобы вызывающий код знал, сколько символов нужно стереть в активном поле.
/// </summary>
public static class Typography
{
    /// <summary>Результат правки слова.</summary>
    public readonly record struct Fix(string Corrected, int ReplaceLength)
    {
        public bool Changed => Corrected.Length != ReplaceLength || Corrected != null;
    }

    /// <summary>
    /// Исправляет случайно нажатый CapsLock: если слово ≥3 букв и КАЖДАЯ буква в верхнем
    /// регистре кроме первой (а первая — строчная), скорее всего пользователь забыл CapsLock.
    /// Преобразует "пРИВЕТ" → "Привет".
    /// </summary>
    public static string? FixAccidentalCapsLock(string word)
    {
        if (word.Length < 3) return null;
        if (!char.IsLetter(word[0]) || !char.IsLower(word[0])) return null;
        for (int i = 1; i < word.Length; i++)
            if (!char.IsLetter(word[i]) || !char.IsUpper(word[i])) return null;
        // Инвертируем регистр: первая в Title, остальные в lower.
        var arr = word.ToCharArray();
        arr[0] = char.ToUpper(arr[0]);
        for (int i = 1; i < arr.Length; i++) arr[i] = char.ToLower(arr[i]);
        return new string(arr);
    }

    /// <summary>
    /// "ПРивет" → "Привет": две первые заглавные подряд + дальше строчные.
    /// </summary>
    public static string? FixDoubleCapital(string word)
    {
        if (word.Length < 3) return null;
        if (!char.IsLetter(word[0]) || !char.IsUpper(word[0])) return null;
        if (!char.IsLetter(word[1]) || !char.IsUpper(word[1])) return null;
        // Оставшиеся буквы должны быть строчными, иначе это аббревиатура (USB, ФБР).
        for (int i = 2; i < word.Length; i++)
            if (char.IsLetter(word[i]) && !char.IsLower(word[i])) return null;
        var arr = word.ToCharArray();
        arr[1] = char.ToLower(arr[1]);
        return new string(arr);
    }
}
