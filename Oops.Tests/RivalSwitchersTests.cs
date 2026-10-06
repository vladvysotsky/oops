using Oops.Core;
using Xunit;

namespace Oops.Tests;

public class RivalSwitchersTests
{
    [Theory]
    [InlineData("Punto Switcher", null, "Punto Switcher")]
    [InlineData(null, "Punto Switcher", "Punto Switcher")]
    [InlineData("punto switcher", "", "Punto Switcher")]          // регистр в ресурсах бывает любым
    [InlineData("Caramba Switcher 2026", null, "Caramba Switcher")] // версия в названии продукта
    [InlineData(null, "Mahou — layout indicator", "Mahou")]
    public void RecognisesKnownSwitchersByTheirResources(string? product, string? description, string expected)
        => Assert.Equal(expected, RivalSwitchers.Match(product, description));

    [Theory]
    // Имени процесса верить нельзя: у Punto Switcher он «ps.exe», и ровно так же
    // может называться что угодно. Поэтому смотрим только на ресурсы exe, и
    // обычные программы не должны давать ложной тревоги.
    [InlineData("ps", "ps")]
    [InlineData("Microsoft® Windows® Operating System", "Windows Explorer")]
    [InlineData("Google Chrome", "Google Chrome")]
    [InlineData(null, null)]
    [InlineData("", "")]
    public void OrdinaryProgramsRaiseNoAlarm(string? product, string? description)
        => Assert.Null(RivalSwitchers.Match(product, description));
}
