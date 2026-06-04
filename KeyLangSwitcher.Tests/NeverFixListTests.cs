using System.IO;
using KeyLangSwitcher.Core;
using Xunit;

namespace KeyLangSwitcher.Tests;

public class NeverFixListTests
{
    [Fact]
    public void Add_PersistsAcrossInstances()
    {
        // Чистим файл, чтобы не зависеть от прошлых прогонов.
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KeyLangSwitcher", "never_fix.txt");
        if (File.Exists(path)) File.Delete(path);

        var a = new NeverFixList();
        a.Add("Ghbdtn"); // регистр должен нормализоваться
        Assert.True(a.Contains("ghbdtn"));
        Assert.True(a.Contains("GHBDTN"));

        var b = new NeverFixList();
        Assert.True(b.Contains("ghbdtn"));
    }

    [Fact]
    public void Contains_OnUnknownWord_IsFalse()
    {
        var list = new NeverFixList();
        Assert.False(list.Contains("totally_unique_token_xyz_qq"));
    }
}
