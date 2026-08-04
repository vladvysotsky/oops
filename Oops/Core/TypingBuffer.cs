using System.Text;

namespace Oops.Core;

/// <summary>
/// Лента символов, которые пользователь набрал с момента последнего сброса.
/// Никаких словарей и эвристик — просто «что было напечатано».
///
/// Умеет отдать границу N-го слова с конца — на этом построен режим
/// расширяющейся области: 1-е нажатие хоткея правит последнее слово,
/// 2-е — два последних, и так далее.
///
/// Сбрасывается на Enter/Tab/Esc/стрелках/Delete, клике мыши, смене окна,
/// ручной смене раскладки и по таймауту бездействия.
/// </summary>
public sealed class TypingBuffer
{
    private readonly StringBuilder _buffer = new();
    private DateTime _lastInputUtc = DateTime.UtcNow;
    private readonly object _gate = new();

    /// <summary>Таймаут бездействия, после которого буфер считается устаревшим.</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public string Snapshot()
    {
        lock (_gate)
        {
            EvictIfIdleLocked();
            return _buffer.ToString();
        }
    }

    public int Length
    {
        get { lock (_gate) { EvictIfIdleLocked(); return _buffer.Length; } }
    }

    public void Append(char c)
    {
        lock (_gate)
        {
            EvictIfIdleLocked();
            _buffer.Append(c);
            _lastInputUtc = DateTime.UtcNow;
        }
    }

    public void Backspace()
    {
        lock (_gate)
        {
            EvictIfIdleLocked();
            if (_buffer.Length > 0) _buffer.Length--;
            _lastInputUtc = DateTime.UtcNow;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _buffer.Clear();
            _lastInputUtc = DateTime.UtcNow;
        }
    }

    /// <summary>Заменяет содержимое буфера (после того, как мы сами переписали текст на экране).</summary>
    public void Reset(string text)
    {
        lock (_gate)
        {
            _buffer.Clear();
            _buffer.Append(text);
            _lastInputUtc = DateTime.UtcNow;
        }
    }

    private void EvictIfIdleLocked()
    {
        if (DateTime.UtcNow - _lastInputUtc > IdleTimeout)
            _buffer.Clear();
    }

    /// <summary>
    /// Индекс начала <paramref name="wordsFromEnd"/>-го слова с конца.
    /// wordsFromEnd = 1 → начало последнего слова, 2 → начало предпоследнего и т.д.
    /// Если слов меньше — возвращает 0 (вся строка).
    /// Словом считается непрерывная последовательность непробельных символов.
    /// </summary>
    public static int StartOfLastWords(string text, int wordsFromEnd)
    {
        if (string.IsNullOrEmpty(text) || wordsFromEnd <= 0) return text?.Length ?? 0;

        int i = text.Length;
        int found = 0;
        while (i > 0)
        {
            // пропускаем пробелы справа
            while (i > 0 && char.IsWhiteSpace(text[i - 1])) i--;
            if (i == 0) break;

            // проходим слово
            int wordEnd = i;
            while (i > 0 && !char.IsWhiteSpace(text[i - 1])) i--;
            found++;
            if (found == wordsFromEnd) return i;
            if (wordEnd == i) break; // страховка от зацикливания
        }
        return 0;
    }

    /// <summary>Сколько слов в строке.</summary>
    public static int CountWords(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        int n = 0;
        bool inWord = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c)) inWord = false;
            else if (!inWord) { inWord = true; n++; }
        }
        return n;
    }
}
