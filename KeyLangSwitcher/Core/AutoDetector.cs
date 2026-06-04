namespace KeyLangSwitcher.Core;

/// <summary>
/// Многоуровневый детектор неправильно набранной раскладки.
///
/// Иерархия эвристик (срабатывает первая давшая однозначный ответ):
///   1) Словарь: если слово известно в исходном языке — оставляем; если оно
///      известно после layout-конвертации в другом языке, а в исходном нет —
///      конвертируем.
///   2) Гласные: для слов вне словаря — латиница ≥3 символов без a/e/i/o/u/y
///      классифицируется как RU-набранное-в-EN, и симметрично для кириллицы.
/// </summary>
public static class AutoDetector
{
    private const int MinWordLength = 2;
    private const int MinFallbackWordLength = 3;
    private static readonly HashSet<char> EnVowels = new("aeiouyAEIOUY");
    private static readonly HashSet<char> RuVowels = new("аеёиоуыэюяАЕЁИОУЫЭЮЯ");

    public enum Verdict
    {
        Keep,
        WasMeantRussian,
        WasMeantEnglish,
    }

    public static Verdict Analyze(string word)
    {
        if (string.IsNullOrEmpty(word) || word.Length < MinWordLength) return Verdict.Keep;

        // Классификация по алфавиту.
        int latin = 0, cyr = 0, other = 0;
        foreach (var c in word)
        {
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) latin++;
            else if ((c >= 'а' && c <= 'я') || (c >= 'А' && c <= 'Я') || c == 'ё' || c == 'Ё') cyr++;
            else other++;
        }
        if (other > 0) return Verdict.Keep;          // пунктуация/цифры в слове — не классифицируем
        if (latin > 0 && cyr > 0) return Verdict.Keep; // смешанные

        var lower = word.ToLowerInvariant();

        if (latin > 0)
        {
            bool knownEn = WordDictionary.IsKnownEn(lower);
            var converted = LayoutConverter.ToRussian(lower);
            bool knownRu = WordDictionary.IsKnownRu(converted);

            if (knownEn && !knownRu) return Verdict.Keep;
            if (!knownEn && knownRu) return Verdict.WasMeantRussian;
            // если оба известны / оба неизвестны — fallback
        }
        else // cyr > 0
        {
            bool knownRu = WordDictionary.IsKnownRu(lower);
            var converted = LayoutConverter.ToEnglish(lower).ToLowerInvariant();
            bool knownEn = WordDictionary.IsKnownEn(converted);

            if (knownRu && !knownEn) return Verdict.Keep;
            if (!knownRu && knownEn) return Verdict.WasMeantEnglish;
        }

        // Fallback по гласным.
        if (word.Length < MinFallbackWordLength) return Verdict.Keep;
        if (latin >= MinFallbackWordLength && !HasAnyEnVowel(word)) return Verdict.WasMeantRussian;
        if (cyr >= MinFallbackWordLength && !HasAnyRuVowel(word)) return Verdict.WasMeantEnglish;
        return Verdict.Keep;
    }

    private static bool HasAnyEnVowel(string word)
    {
        foreach (var c in word) if (EnVowels.Contains(c)) return true;
        return false;
    }

    private static bool HasAnyRuVowel(string word)
    {
        foreach (var c in word) if (RuVowels.Contains(c)) return true;
        return false;
    }
}
