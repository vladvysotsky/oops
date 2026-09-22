namespace Oops.Core;

/// <summary>
/// Насколько кусок текста похож на слово того или иного языка.
///
/// Нужно ровно для одного решения: конвертировать слово или оставить. Считать
/// буквы недостаточно — «xtuj» и «appconfig» оба полностью латинские, и
/// отличить мусор от настоящего слова можно только зная язык. Раньше такой
/// текст не чинился ни одним нажатием: «appconfig» превращался в «фззсщташп».
///
/// Это НЕ словарь, который мы когда-то убрали. Список слов не хранится и не
/// ищется: в таблице лежат частоты пар букв (включая границы слова), по 1156 и
/// 784 байта на язык. «фззсщташп» отбраковывается не потому, что его нет в
/// списке, а потому, что сочетания «зз», «сщ», «шп» в русском почти не
/// встречаются. Незнакомое слово — «nginx», «useState» — модель принимает
/// спокойно, пока оно сложено из обычных для языка сочетаний.
///
/// Таблицы построены по открытым спискам слов (1.5 млн русских, 370 тыс.
/// английских) и квантованы в байт с шагом 0.1 натурального логарифма.
/// </summary>
public static class LanguageModel
{
    public enum Language { Russian, English }

    // Алфавиты БЕЗ «ё»: она сводится к «е», иначе половина таблицы пустует.
    private const string RussianLetters = "абвгдежзийклмнопрстуфхцчшщъыьэюя";
    private const string EnglishLetters = "abcdefghijklmnopqrstuvwxyz";

    // Индексы символов: 0 — начало слова, 1..N — буквы, N+1 — конец слова.
    private const string RussianTableBase64 = "pz47Mj45U001PWw4Qjs1MSk1MTw7RUhPSEZZoJScSF1Vp6doQy5CPjlEOkxHPjI3MWJAODY0VE0/SURIS6enmWA1ODmnQWlZZ14+YWpDp1lDVk8/fUBTX0iGZmlqYFpbUWV6ZmBqpyloXGhXOGxMPJxMQlpFNVdOSFFHf3BjXyxZcTZdkHZORKc+fmNyb0mAgkKnZUdiSjSGP2ZvTI6nimFaoKeSmX+DnF+nPl1JW106XF89p05SV0I5VURLWUV8Z1tWWndeS2J7ZFVcp1ZHOTw8P0ZATzs+NjAvSUAvMzVTU0pTREJQp6ecaT5YL6dEb31uTEFkez+nW3BkTF92aWiKVYKge3+Mp4KcZ4WUp2mnNE9FTkhLV2FDp1lOSURCgU5teEyVoHNlXqdZS2d9a1thp0xMLUdEOlA8Szs6OzQ3Q0c1NjhaTT1DO0xSp6ecbEtGMqeEbnR3YG+QcoWnV2dfT2VyaUBNkG54YWVQaaecoKePfzCnM39ReXpHnGM5oF1Cck4xgz1LRT2CeVh1V6enfX2ZcplJpzZfZmBfNGFeM6BLTGhTNG2PTVhEdnB/Xnx1nEs3g0c9R6c6VG10hDmMbzKgWE9USDhPZEJzN2Z/bmNyZ5lBbX11Ui6nMmxgUUk1YVoynEh3gDEtdGlFPzpaZk5QZ2BxMkx2WkVHp1s3Ljc1PURASjs8NTE3SjozMzRQTElRPUlQp5mnWz1TNqc8ioychjennECnXz+FUjBbMFdRR3B+a15rb6BNYIp7U2enLlNSS08vUVcyoElWSEAtVVlHRDlbVFtSUFuIRFd4V0JUp0FVRlpbQGN4P6czQUhFOz5TRC9GWFNVUFRsX0g9emcvWKc2WT9cWjNpbTWcR09WPzRUNkFVQWllZFNcZmc+PHdbTD+nWUdES0ZHSE9dVkhDR0plREI/QW9hTWJGR1Cnp6dpNlo1p02Dl36PTqePRqeHWGlnSJdTaVxYYaeghX6Op2hyh3h1badIcVFobF5+bUynYktYUEVmUEtZWn52dmlkjXOginGnmTanSIBdg4VDp4BBp119dHdaeIh4fleXlH6ggaenWIqOhpBYp0CQcKePOH+nO6dLYGs/Yo9qeVxQnIKOi3Gnp6BcoKegYKc/hF6ZoDGnpzKnT1FlUWBZc3VTQ45+hYaXp6eVRZRtp2WnRad6p6c3p6c4p6enjmR2p2mnp0qnp6enoKenp4anp6dvp6enp6CnUKenmaenp6CgoKenp6enp6enp6Cnp6enp3BahaenVjdTVkBcVW8+TE45WJdNS0hPb6A/a1VRYKenp6eVbEKnmWViZ2JOg19fjU+SWDxnaZJDUqBjdl1cX1enp6CKTlQ2p5l5ZW9slYd6oHdTU2Fee2NdYl2CZ3eKoHanp6eniZyLhKd1WGtrXmxjY3JsXWNhXnZvWkRIp3ZgaFdkM6enp39phzCnoGBFUlFNUlB2WldPTEqFX1VGRm+CUV9WaUmnp6CnSGEsp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enp6enpw==";
    private const string EnglishTableBase64 = "mDI1MDU4Ojs4OUhFPDU5OS9NNi41M0JAWVFPmJhgOTY8QEs9T0BeRy46Llw7XTA2LUJGTFBJTzeYPU1cWz1ma2M9YHQ5YWQ/YoBBTlpDZmyIWIBXmDN4SnA4en01OphAQnBhM3JkPVI7Po15kUZxOJg9XWNNNF1SWjVfcUhXTz1jfkJJZkZcWo1NczCYN0c5MD9FRE9EXFY1OS5CPVMpLDZERkpCTVsqmEZxc3NCR3dyP4J+RHFyQXaYSF5RRY1yilOEUZg+Y3RnOmhLSD1+dEFUSENtkT9NYkWGYphLfjWYOF5oZjVgbGg4fG5NVFM4ZH5FVEpIdFyYPnpBmDRDLzg5QD9bWGdJNz0qND9aPy8ySz1mVWo9RphPjYaBUpiNgluEiIqIe1KKmH+Kik+RjZiDmHWYSmBvbj5jdFpEd2dRYVVSZpFcTWBadV+RV4pGmDJWUEovU1RgMHlUNU9SNU96Y0hGQFJegDVwNpg1RGlqNWFzbDZ6dFxGUjk/hGdMaUVsapFIfDuYNU85NzBGMU80WUtNTkU0SVpLODFHTVNpTVkymEZDOz5ISztSQWVNNjYsPjheMTc5NUBETFJVRpg5YmZqNmRtNjt5bT9iWTlGiDhFQ0WCY5FNkUmYeZiYjYSRkY16mJiNkY2IjYiEhoJBjY2YkZh1mC9HQUEuTkdLL2RMSEBEMEZmQTo9QU1Ufj5oNJg8WjxgNFpgOjZtTUU/ST09Ul80LztnT5hIeSeYNFlNYy1VYDcsbW1GVFY0XHk0QUE/a1GDP100mERCQ0ZGU0ppRGxZOD0xUkJyODU8bVt1XGZfVZhCkYOBNpiCkT+YhHqKfUmKmGl3glpyjZhniGaYQ2FtYEZmcE9GhmRXZlNHaY1WVmRojWuYZXlTmFJyV3pScn1jS5iNb3R9VVN9fWlRX4JzgFWRUphLWkpPTWFUYk+Aa0VKSk1FhktGS2JzXmOBYi+YSnl7eUCKgXxKjX1gfH5Me4h/fH1ngXqYW1hgmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmA==";

    // Значение для пары, которой в таблице нет: цифры, знаки, чужие буквы.
    // Слово с такими символами выглядит неправдоподобным в обоих языках —
    // и это верно: «ye;ty» с точкой с запятой посреди слова почти наверняка
    // набрано не в той раскладке.
    private const int RussianFloor = 167;
    private const int EnglishFloor = 152;

    private static readonly byte[] RussianTable = Convert.FromBase64String(RussianTableBase64);
    private static readonly byte[] EnglishTable = Convert.FromBase64String(EnglishTableBase64);

    private static readonly Dictionary<char, int> RussianIndex = BuildIndex(RussianLetters);
    private static readonly Dictionary<char, int> EnglishIndex = BuildIndex(EnglishLetters);

    private static Dictionary<char, int> BuildIndex(string letters)
    {
        var map = new Dictionary<char, int>(letters.Length);
        for (int i = 0; i < letters.Length; i++) map[letters[i]] = i + 1;
        return map;
    }

    /// <summary>
    /// Средняя «неправдоподобность» на пару букв: МЕНЬШЕ значит больше похоже
    /// на слово этого языка. Величина — натуральный логарифм вероятности со
    /// знаком минус, так что сравнивать можно только между собой.
    /// </summary>
    public static double Implausibility(string word, Language language)
    {
        var letters = language == Language.Russian ? RussianLetters : EnglishLetters;
        var index = language == Language.Russian ? RussianIndex : EnglishIndex;
        var table = language == Language.Russian ? RussianTable : EnglishTable;
        int floor = language == Language.Russian ? RussianFloor : EnglishFloor;
        int size = letters.Length + 2;

        int previous = 0;                       // начало слова
        double total = 0;
        int pairs = 0;

        foreach (var raw in word)
        {
            var c = char.ToLowerInvariant(raw);
            if (c == 'ё') c = 'е';
            int current = index.TryGetValue(c, out var found) ? found : -1;

            total += (previous < 0 || current < 0) ? floor : table[previous * size + current];
            pairs++;
            previous = current;
        }

        total += previous < 0 ? floor : table[previous * size + (size - 1)];   // конец слова
        pairs++;

        return total / pairs / 10.0;            // квантование было с шагом 0.1
    }
}
