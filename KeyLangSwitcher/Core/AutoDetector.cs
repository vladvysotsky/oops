namespace KeyLangSwitcher.Core;

/// <summary>
/// Многоуровневый детектор неправильно набранной раскладки.
///
/// Стратегия "не навреди": срабатывает только когда УВЕРЕН. Все эвристики
/// ниже консервативны и могут вернуть Keep даже при неоднозначном слове.
///
/// Шаги:
///   1) Аббревиатуры (всё верхним регистром, ≥2 букв) — никогда не трогаем.
///   2) Словарь: слово известно в исходном языке + НЕизвестно после конвертации
///      → keep. Слово неизвестно в исходном + известно после конвертации
///      → convert.
///   3) Fallback по гласным — только для слов ≥5 символов (короткие
///      нераспознанные — слишком велик риск ложного срабатывания на
///      аббревиатурах, доменах, частицах кодов).
/// </summary>
public static class AutoDetector
{
    private const int MinWordLength = 2;
    private const int MinFallbackWordLength = 5;
    private static readonly HashSet<char> EnVowels = new("aeiouyAEIOUY");
    private static readonly HashSet<char> RuVowels = new("аеёиоуыэюяАЕЁИОУЫЭЮЯ");

    public enum Verdict
    {
        Keep,
        WasMeantRussian,
        WasMeantEnglish,
    }

    public static Verdict Analyze(string word) => Analyze(word, ContextLanguage.Unknown);

    /// <summary>
    /// Анализ с учётом контекста: <paramref name="recent"/> — преобладающий
    /// язык в недавно набранных словах. При неоднозначности AutoDetector
    /// предпочтёт продолжить в том же языке.
    /// </summary>
    public static Verdict Analyze(string word, ContextLanguage recent)
    {
        if (string.IsNullOrEmpty(word) || word.Length < MinWordLength) return Verdict.Keep;

        // 1) Аббревиатуры — всегда оставляем.
        if (IsAllUpperLetters(word)) return Verdict.Keep;

        // 2) Классификация по алфавиту.
        int latin = 0, cyr = 0, other = 0;
        foreach (var c in word)
        {
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) latin++;
            else if ((c >= 'а' && c <= 'я') || (c >= 'А' && c <= 'Я') || c == 'ё' || c == 'Ё') cyr++;
            else other++;
        }
        if (other > 0) return Verdict.Keep;
        if (latin > 0 && cyr > 0) return Verdict.Keep;

        var lower = word.ToLowerInvariant();

        if (latin > 0)
        {
            bool knownEn = WordDictionary.IsKnownEn(lower);
            var converted = LayoutConverter.ToRussian(lower);
            bool knownRu = WordDictionary.IsKnownRu(converted);

            if (knownEn && !knownRu) return Verdict.Keep;
            if (!knownEn && knownRu) return Verdict.WasMeantRussian;
            // оба или ни одного — fallback
        }
        else
        {
            bool knownRu = WordDictionary.IsKnownRu(lower);
            var converted = LayoutConverter.ToEnglish(lower).ToLowerInvariant();
            bool knownEn = WordDictionary.IsKnownEn(converted);

            if (knownRu && !knownEn) return Verdict.Keep;
            if (!knownRu && knownEn) return Verdict.WasMeantEnglish;
        }

        // 3) Fallback по гласным — только для длинных слов.
        if (word.Length < MinFallbackWordLength) return Verdict.Keep;

        bool latinNoVowels = latin >= MinFallbackWordLength && !HasAny(word, EnVowels);
        bool cyrNoVowels = cyr >= MinFallbackWordLength && !HasAny(word, RuVowels);

        // 4) Контекст: если недавно писали в определённом языке и эвристика хочет нас перенести —
        //    подавляем "слабые" срабатывания. Перевод в ДРУГОЙ язык — только если эвристика реально
        //    говорит "это не текущий язык".
        if (latinNoVowels)
        {
            // Латиница без гласных — обычно русское слово, набранное в EN. НО: если контекст
            // явно EN, возможно это специфический термин (например, "rhythm" — нет гласных
            // в широком смысле, но... в нашем определении 'y' — гласная, так что не сработает).
            if (recent == ContextLanguage.English) return Verdict.Keep;
            return Verdict.WasMeantRussian;
        }
        if (cyrNoVowels)
        {
            if (recent == ContextLanguage.Russian) return Verdict.Keep;
            return Verdict.WasMeantEnglish;
        }
        return Verdict.Keep;
    }

    public enum ContextLanguage { Unknown, English, Russian }

    private static bool IsAllUpperLetters(string word)
    {
        if (word.Length < 2) return false;
        foreach (var c in word) if (!char.IsLetter(c) || !char.IsUpper(c)) return false;
        return true;
    }

    private static bool HasAny(string word, HashSet<char> set)
    {
        foreach (var c in word) if (set.Contains(c)) return true;
        return false;
    }
}

