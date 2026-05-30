namespace KeyLangSwitcher.Core;

/// <summary>
/// Эвристический детектор неправильно набранной раскладки. Анализирует ОДНО слово
/// (последнее перед пробелом/пунктуацией) и решает, выглядит ли оно "иностранно"
/// для текущей раскладки.
///
/// Эвристика проста:
///   - если слово в EN-латинице и НЕ содержит ни одной английской гласной (a/e/i/o/u/y)
///     при длине ≥ 3 — скорее всего, это RU-слово, набранное в EN-раскладке;
///   - если слово в RU-кириллице и НЕ содержит ни одной русской гласной
///     при длине ≥ 3 — это EN-слово, набранное в RU-раскладке.
///
/// Это не статистическая модель и даст ложные срабатывания (например, "xml", "kk"),
/// поэтому подходит только в качестве дополнения к ручному хоткею. На словах ≤ 2 символа
/// отключается. Не трогает слова со смешанными алфавитами и со специальными символами.
/// </summary>
public static class AutoDetector
{
    private const int MinWordLength = 3;
    private static readonly HashSet<char> EnVowels = new("aeiouyAEIOUY");
    private static readonly HashSet<char> RuVowels = new("аеёиоуыэюяАЕЁИОУЫЭЮЯ");

    /// <summary>Решение детектора.</summary>
    public enum Verdict
    {
        /// <summary>Слово выглядит нормально или неоднозначно — ничего не делать.</summary>
        Keep,
        /// <summary>Слово выглядит как RU, ошибочно набранное в EN — конвертировать в RU.</summary>
        WasMeantRussian,
        /// <summary>Слово выглядит как EN, ошибочно набранное в RU — конвертировать в EN.</summary>
        WasMeantEnglish,
    }

    public static Verdict Analyze(string word)
    {
        if (string.IsNullOrEmpty(word) || word.Length < MinWordLength) return Verdict.Keep;

        int en = 0, ru = 0, enVowels = 0, ruVowels = 0;
        foreach (var c in word)
        {
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
            {
                en++;
                if (EnVowels.Contains(c)) enVowels++;
            }
            else if ((c >= 'а' && c <= 'я') || (c >= 'А' && c <= 'Я') || c == 'ё' || c == 'Ё')
            {
                ru++;
                if (RuVowels.Contains(c)) ruVowels++;
            }
            else
            {
                // Цифры, пунктуация в середине слова — не классифицируем.
                return Verdict.Keep;
            }
        }

        // Смешанные слова игнорируем — слишком высок шанс ошибки.
        if (en > 0 && ru > 0) return Verdict.Keep;

        if (en >= MinWordLength && enVowels == 0) return Verdict.WasMeantRussian;
        if (ru >= MinWordLength && ruVowels == 0) return Verdict.WasMeantEnglish;
        return Verdict.Keep;
    }
}
