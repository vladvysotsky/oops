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
