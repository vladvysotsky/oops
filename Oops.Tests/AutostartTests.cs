using Oops.Settings;
using Xunit;

namespace Oops.Tests;

public class AutostartTests
{
    private static readonly string Xml = Autostart.BuildTaskXml(@"C:\Program Files\oops\oops.exe");

    [Fact]
    public void TaskStartsWithoutDelay()
    {
        // Смысл раннего запуска ровно в этом: Windows растягивает старт записей
        // в HKCU\...\Run (StartupDelayInMSec, около десяти секунд), а задача с
        // нулевой задержкой этой растяжке не подчиняется.
        Assert.Contains("<LogonTrigger>", Xml);
        Assert.Contains("<Delay>PT0S</Delay>", Xml);
    }

    [Fact]
    public void TaskRunsOnBatteryToo()
    {
        // У задач планировщика по умолчанию DisallowStartIfOnBatteries = true.
        // Не выключить — и на ноутбуке без розетки программа не запустится:
        // худший вид бага, работающий у всех, кроме части пользователей.
        Assert.Contains("<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>", Xml);
        Assert.Contains("<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>", Xml);
    }

    [Fact]
    public void TaskDoesNotAskForAdministrator()
    {
        // Манифест приложения — asInvoker, и задача обязана это повторять:
        // Windows молча игнорирует HKCU\...\Run у elevated-приложений, а
        // запущенная с правами админа копия ломает и остальной автозапуск.
        Assert.Contains("<RunLevel>LeastPrivilege</RunLevel>", Xml);
        Assert.Contains("<LogonType>InteractiveToken</LogonType>", Xml);
    }

    [Fact]
    public void TaskHasNoExecutionTimeLimit()
    {
        // Программа живёт в трее часами. Лимит по умолчанию — трое суток,
        // после чего планировщик её просто прибьёт.
        Assert.Contains("<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>", Xml);
    }

    [Fact]
    public void PathWithSpecialCharactersIsEscaped()
    {
        var xml = Autostart.BuildTaskXml(@"C:\Users\A&B\oops.exe");
        Assert.Contains(@"C:\Users\A&amp;B\oops.exe", xml);
        Assert.DoesNotContain(@"A&B", xml);
    }

    [Fact]
    public void TaskNameMatchesTheOneTheInstallerLooksFor()
    {
        // installer\Oops.iss спрашивает планировщик про это же имя, чтобы не
        // дописать вторую точку автозапуска поверх задачи.
        Assert.Equal("Oops Autostart", Autostart.TaskName);
        Assert.Contains($"<URI>\\{Autostart.TaskName}</URI>", Xml);
    }
}
