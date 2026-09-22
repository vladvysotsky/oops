using Oops.Core;
using Xunit;

namespace Oops.Tests;

public class LayoutConverterTests
{
    [Theory]
    [InlineData("vfvf", "мама")]
    [InlineData("Z nt,z k.,k.", "Я тебя люблю")]
    [InlineData("ghbdtn", "привет")]
    [InlineData("vfvfxrf", "мамачка")]
    [InlineData("", "")]
    public void EnToRu_ConvertsCorrectly(string en, string ru)
    {
        Assert.Equal(ru, LayoutConverter.ToRussian(en));
    }

    [Theory]
    [InlineData("мама", "vfvf")]
    [InlineData("Я тебя люблю", "Z nt,z k.,k.")]
    [InlineData("привет", "ghbdtn")]
    public void RuToEn_ConvertsCorrectly(string ru, string en)
    {
        Assert.Equal(en, LayoutConverter.ToEnglish(ru));
    }

    [Fact]
    public void Roundtrip_EnRuEn_Stable()
    {
        const string original = "Hello, World!";
        var ru = LayoutConverter.ToRussian(original);
        var back = LayoutConverter.ToEnglish(ru);
        Assert.Equal(original, back);
    }

    [Fact]
    public void Roundtrip_RuEnRu_Stable()
    {
        const string original = "Привет, мир!";
        var en = LayoutConverter.ToEnglish(original);
        var back = LayoutConverter.ToRussian(en);
        Assert.Equal(original, back);
    }

    [Theory]
    [InlineData("vfvf", LayoutConverter.Direction.ToRu, "мама")]
    // RU→EN — на сломанном слове, а не на «мама»: превращать настоящее русское
    // слово в «vfvf» как раз и незачем, и модель языка теперь этого не делает.
    [InlineData("црфе", LayoutConverter.Direction.ToEn, "what")]
    [InlineData("123 !@#", LayoutConverter.Direction.None, "123 !@#")]
    public void AutoConvert_PicksDirectionByMajorityCharset(string input, LayoutConverter.Direction expectedDir, string expected)
    {
        var (result, dir) = LayoutConverter.AutoConvertWithDirection(input);
        Assert.Equal(expectedDir, dir);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void DirectionIsChosenPerWord()
    {
        // Первые четыре слова набраны в EN-раскладке вместо русской, последнее —
        // в русской вместо английской. Одно направление на весь кусок такой
        // текст не чинило вообще.
        var (result, dir) = LayoutConverter.AutoConvertWithDirection(
            "Z djn [jxe gjckfnm уьфшд");

        Assert.Equal("Я вот хочу послать email", result);
        // Наружу идёт направление последнего слова: каретка в конце, и системную
        // раскладку надо переключить под то, что будут печатать дальше.
        Assert.Equal(LayoutConverter.Direction.ToEn, dir);
    }

    [Fact]
    public void RealForeignWordsSurviveTheConversion()
    {
        // Жалоба пользователя: «password» превращался в «зфыыцщкв».
        // Оба слова вокруг него полностью латинские, как и оно само, —
        // подсчётом букв их не отличить, различает только язык.
        var (result, _) = LayoutConverter.AutoConvertWithDirection("f lkz xtuj password ye;ty");
        Assert.Equal("а для чего password нужен", result);
    }

    [Fact]
    public void ShortWordInAMangledPhraseIsConvertedWithItsNeighbours()
    {
        // «rfr» — три буквы, и пар в нём слишком мало, чтобы набрать полный
        // запас правдоподобия: у соседей выигрыш около 2.8, у него 0.35.
        // Само по себе слово оставалось нетронутым, и получалось «привет rfr
        // дела» — ровно то месиво, ради которого модель языка и заводилась.
        var (result, _) = LayoutConverter.AutoConvertWithDirection("ghbdtn rfr ltkf");
        Assert.Equal("привет как дела", result);
    }

    [Fact]
    public void NeighboursDoNotDragARealWordIntoTheConversion()
    {
        // Обратная сторона: поддержка соседей снимает запас, но не порог.
        // Настоящее слово уходит в минус в любом окружении — «password» −6.0,
        // «tot» −1.3, «get» −0.2, — и вытащить его соседями нельзя.
        Assert.Equal("привет tot дела",
            LayoutConverter.AutoConvertWithDirection("ghbdtn tot ltkf").Result);
        Assert.Equal("привет get дела",
            LayoutConverter.AutoConvertWithDirection("ghbdtn get ltkf").Result);
    }

    [Fact]
    public void LiteralModeConvertsEveryWordBecauseASelectionHasNoSecondPress()
    {
        // Жалоба пользователя: «я думаю надо предусмотреть такую inere? потомму».
        // Модель на биграммах тут не видит разницы — «inere» как английское 4.6
        // против «штуку» как русского 6.7, — а выделение исправить нечем:
        // следующее нажатие прочитает то же выделение и решит так же.
        var (result, dir) = LayoutConverter.AutoConvertWithDirection(
            "z levf. yflj ghtlecvjnhtnm nfre. inere? gjnjvve", literal: true);

        Assert.Equal("я думаю надо предусмотреть такую штуку, потомму", result);
        Assert.Equal(LayoutConverter.Direction.ToRu, dir);
    }

    [Fact]
    public void LiteralModeStillPicksTheDirectionPerWord()
    {
        // Буквально — не значит «в одну сторону»: смешанный текст обязан
        // пережить выделение целиком.
        var (result, dir) = LayoutConverter.AutoConvertWithDirection(
            "Z djn [jxe gjckfnm уьфшд", literal: true);

        Assert.Equal("Я вот хочу послать email", result);
        Assert.Equal(LayoutConverter.Direction.ToEn, dir);
    }

    [Fact]
    public void LiteralModeGivesUpTheProtectionOfRealWords_AKnownPrice()
    {
        // Цена буквального режима, записанная явно, чтобы её не приняли за
        // регрессию: в выделении настоящее слово тоже конвертируется. На
        // набранном тексте оно по-прежнему защищено — там есть второе нажатие.
        Assert.Equal("а для чего зфыыцщкв нужен",
            LayoutConverter.AutoConvertWithDirection(
                "f lkz xtuj password ye;ty", literal: true).Result);

        Assert.Equal("а для чего password нужен",
            LayoutConverter.AutoConvertWithDirection("f lkz xtuj password ye;ty").Result);
    }

    [Fact]
    public void LoneShortWordIsConvertedWhenThereAreNoBystanders()
    {
        // Первое нажатие берёт в область ровно одно слово, и защищать в ней
        // некого: человек показал именно на него. Иначе хоткей на «rfr» молчал
        // бы — три буквы не набирают полного запаса.
        var (result, dir) = LayoutConverter.AutoConvertWithDirection("rfr");
        Assert.Equal("как", result);
        Assert.Equal(LayoutConverter.Direction.ToRu, dir);
    }

    [Theory]
    // Запас снят, но порог остался, и всё настоящее держится им одним:
    // на замерах эти уходят в минус от −0.2 до −6.0.
    [InlineData("tot")]
    [InlineData("get")]
    [InlineData("password")]
    [InlineData("email")]
    [InlineData("Anderson")]
    [InlineData("photo.jpg")]
    public void LoneRealWordIsLeftAloneEvenWithoutTheMargin(string word)
    {
        var (result, dir) = LayoutConverter.AutoConvertWithDirection(word);
        Assert.Equal(word, result);
        Assert.Equal(LayoutConverter.Direction.None, dir);
    }

    [Theory]
    // Ничего из этого трогать нельзя: одно нажатие превратило бы в мусор,
    // а вернуть повторным нажатием уже не получится.
    [InlineData("https://example.com")]
    [InlineData("photo.jpg")]
    [InlineData("Helsinki")]
    [InlineData("online")]
    [InlineData("meeting")]
    public void CorrectTextIsLeftAlone(string input)
    {
        var (result, _) = LayoutConverter.AutoConvertWithDirection(input);
        Assert.Equal(input, result);
    }

    [Fact]
    public void SmartSelectionCanBeTurnedOff()
    {
        // Запасной ход для случая, когда модель ошиблась и сочетание молчит.
        try
        {
            LayoutConverter.SmartWordSelection = false;
            var (result, _) = LayoutConverter.AutoConvertWithDirection("password");
            Assert.Equal("зфыыцщкв", result);
        }
        finally
        {
            LayoutConverter.SmartWordSelection = true;
        }
    }

    [Fact]
    public void WhitespaceIsPreservedExactly()
    {
        // Пословная обработка не имеет права трогать разделители: на них
        // держится арифметика стирания.
        const string input = "  vfvf\t\nvfvf  ";
        var (result, _) = LayoutConverter.AutoConvertWithDirection(input);
        Assert.Equal("  мама\t\nмама  ", result);
        Assert.Equal(input.Length, result.Length);
    }

    [Fact]
    public void WordsWithoutLettersAreLeftAlone()
    {
        var (result, dir) = LayoutConverter.AutoConvertWithDirection("123 vfvf 456");
        Assert.Equal("123 мама 456", result);
        Assert.Equal(LayoutConverter.Direction.ToRu, dir);
    }

    [Theory]
    [InlineData('@', '"')]
    [InlineData('#', '№')]
    [InlineData('$', ';')]
    [InlineData('^', ':')]
    [InlineData('&', '?')]
    [InlineData('|', '/')]
    [InlineData('?', ',')]
    [InlineData('~', 'Ё')]
    [InlineData('`', 'ё')]
    public void ShiftSymbols_MapPositionally(char en, char ru)
    {
        Assert.Equal(ru.ToString(), LayoutConverter.ToRussian(en.ToString()));
        Assert.Equal(en.ToString(), LayoutConverter.ToEnglish(ru.ToString()));
    }
}
