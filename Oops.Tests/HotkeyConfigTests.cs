using Oops.Core;
using Xunit;
using Keys = System.Windows.Forms.Keys;

namespace Oops.Tests;

public class HotkeyConfigTests
{
    [Fact]
    public void ModifierOnlyCombo_MatchesWhenLastModifierGoesDown()
    {
        var h = HotkeyConfig.ChangeCaseDefault;   // Alt+Win
        Assert.True(h.Matches(Keys.LWin, ctrl: false, shift: false, alt: true, win: true));
    }

    [Fact]
    public void ModifierOnlyCombo_IgnoresExtraModifiers()
    {
        // AltGr — это Ctrl+Alt: Alt+Win не должен на нём срабатывать.
        var h = HotkeyConfig.ChangeCaseDefault;
        Assert.False(h.Matches(Keys.LWin, ctrl: true, shift: false, alt: true, win: true));
    }

    [Fact]
    public void DefaultHotkeys_AreNotTheSameCombo()
    {
        // Одинаковые сочетания = второй хоткей мёртв: App проверяет раскладку
        // первой и до регистра дело не доходит.
        Assert.False(HotkeyConfig.Default.SameCombo(HotkeyConfig.ChangeCaseDefault));
    }

    [Theory]
    [InlineData(true, false, false, true, "Ctrl+Win")]
    [InlineData(false, false, true, true, "Alt+Win")]
    [InlineData(true, true, true, false, "Ctrl+Alt+Shift")]
    public void ToString_ListsModifiersInDocumentedOrder(
        bool ctrl, bool shift, bool alt, bool win, string expected)
    {
        // Порядок из документации: Ctrl → Alt → Shift → Win. Окно настроек
        // показывало «Win+Alt» там, где везде написано «Alt+Win», и при разборе
        // молчащего хоткея это расхождение сбивает с толку.
        var h = new HotkeyConfig { Ctrl = ctrl, Shift = shift, Alt = alt, Win = win };
        Assert.Equal(expected, h.ToString());
    }

    [Fact]
    public void ToString_AppendsMainKeyLast()
    {
        var h = new HotkeyConfig { Ctrl = true, Alt = true, Key = (int)Keys.X };
        Assert.Equal("Ctrl+Alt+X", h.ToString());
    }

    [Fact]
    public void ThreeModifierCombo_Matches()
    {
        // Сочетание может быть и из трёх клавиш.
        var h = new HotkeyConfig { Ctrl = true, Alt = true, Win = true };
        Assert.True(h.Matches(Keys.LWin, ctrl: true, shift: false, alt: true, win: true));
        Assert.False(h.Matches(Keys.LWin, ctrl: false, shift: false, alt: true, win: true));
    }

    [Fact]
    public void SameCombo_ComparesValues_NotReferences()
    {
        Assert.True(HotkeyConfig.Default.SameCombo(HotkeyConfig.Default));
        Assert.True(new HotkeyConfig { Ctrl = true, Win = true }
            .SameCombo(new HotkeyConfig { Ctrl = true, Win = true }));
        Assert.False(new HotkeyConfig { Ctrl = true, Win = true }
            .SameCombo(new HotkeyConfig { Ctrl = true, Win = true, Key = (int)Keys.S }));
    }
}
