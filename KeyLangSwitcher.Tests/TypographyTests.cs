using KeyLangSwitcher.Core;
using Xunit;

namespace KeyLangSwitcher.Tests;

public class TypographyTests
{
    [Theory]
    [InlineData("пРИВЕТ", "Привет")]
    [InlineData("hELLO", "Hello")]
    [InlineData("kEY", "Key")]
    public void FixAccidentalCapsLock_InvertsCase(string input, string expected)
    {
        Assert.Equal(expected, Typography.FixAccidentalCapsLock(input));
    }

    [Theory]
    [InlineData("Hello")]    // нормальное слово — не трогаем
    [InlineData("HELLO")]    // полностью верхний регистр — это не "забытый caps lock"
    [InlineData("Hi")]       // слишком короткое
    [InlineData("Word123")]  // не только буквы
    public void FixAccidentalCapsLock_KeepsRegularWords(string input)
    {
        Assert.Null(Typography.FixAccidentalCapsLock(input));
    }

    [Theory]
    [InlineData("ПРивет", "Привет")]
    [InlineData("HEllo", "Hello")]
    [InlineData("ТЕст", "Тест")]
    public void FixDoubleCapital_LowercasesSecondLetter(string input, string expected)
    {
        Assert.Equal(expected, Typography.FixDoubleCapital(input));
    }

    [Theory]
    [InlineData("Hello")]
    [InlineData("USB")]      // полностью верхний — аббревиатура, не трогаем
    [InlineData("hELLO")]    // первая строчная — это другая правка
    public void FixDoubleCapital_LeavesAcronymsAndRegularWords(string input)
    {
        Assert.Null(Typography.FixDoubleCapital(input));
    }
}
