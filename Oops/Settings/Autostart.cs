using System.Diagnostics;
using System.Text;
using Microsoft.Win32;
using Oops.Core;

namespace Oops.Settings;

/// <summary>Каким способом программа запускается при входе в систему.</summary>
public enum AutostartMode
{
    /// <summary>Запись в HKCU\...\Run. Просто и надёжно, но поздно.</summary>
    Registry,

    /// <summary>
    /// Задача в планировщике с триггером «при входе» и нулевой задержкой.
    /// Запускается раньше записей в Run: Windows 8+ намеренно растягивает их
    /// старт (StartupDelayInMSec, около десяти секунд), а планировщик этой
    /// растяжке не подчиняется.
    /// </summary>
    Scheduler,
}

/// <summary>
/// Автозапуск. Поддерживает два способа, но включённым может быть только ОДИН:
/// два — это две попытки запуска, из которых вторая упрётся в single-instance
/// mutex и покажет окно «программа уже запущена» при каждом входе в систему.
///
/// **Состояние живёт в системе, а не в settings.json.** Правило старое и
/// выстраданное: копия флага в настройках затирала запись, сделанную
/// инсталлятором, и первый пользователь получил «галочка в установщике ничего
/// не делает». Способ определяется тем же образом — по тому, что реально есть:
/// задача в планировщике важнее записи в реестре.
/// </summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Oops";

    /// <summary>Имя задачи. Менять нельзя: по нему находится уже созданная.</summary>
    public const string TaskName = "Oops Autostart";

    public static bool IsEnabled() => HasTask() || HasRegistryEntry();

    /// <summary>
    /// Каким способом сейчас включён автозапуск. Если не включён вовсе —
    /// возвращает <see cref="AutostartMode.Registry"/> как значение по
    /// умолчанию для переключателя в настройках.
    /// </summary>
    public static AutostartMode CurrentMode =>
        HasTask() ? AutostartMode.Scheduler : AutostartMode.Registry;

    /// <summary>
    /// Включает автозапуск выбранным способом и убирает второй. Возвращает
    /// текст ошибки или null.
    ///
    /// Ошибку отдаём наружу, а не проглатываем: человек снял галочку, закрыл
    /// окно и уверен, что программа больше не будет запускаться сама.
    /// </summary>
    public static string? Set(bool enabled, AutostartMode mode)
    {
        try
        {
            if (!enabled)
            {
                SetRegistryEntry(false);
                return RemoveTask();
            }

            if (mode == AutostartMode.Scheduler)
            {
                var error = CreateTask();
                if (error != null) return error;
                // Только ПОСЛЕ успешного создания задачи: иначе при отказе
                // планировщика человек остался бы вообще без автозапуска.
                SetRegistryEntry(false);
                return null;
            }

            var removal = RemoveTask();
            SetRegistryEntry(true);
            return removal;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    // ------------------------------------------------------------- реестр

    private static bool HasRegistryEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(ValueName) != null;
    }

    private static void SetRegistryEntry(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (key == null) return;
        if (enabled) key.SetValue(ValueName, $"\"{ExePath}\"");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    // -------------------------------------------------------- планировщик

    private static string ExePath => Environment.ProcessPath
        ?? Path.Combine(AppContext.BaseDirectory, "oops.exe");

    private static bool HasTask() => RunSchtasks($"/Query /TN \"{TaskName}\"", out _);

    private static string? CreateTask()
    {
        var xml = Path.Combine(Path.GetTempPath(), $"oops-task-{Guid.NewGuid():N}.xml");
        try
        {
            // Unicode с BOM: schtasks /XML читает файл только так, в UTF-8 без
            // метки он спотыкается о кириллицу в описании.
            File.WriteAllText(xml, BuildTaskXml(ExePath), new UnicodeEncoding(false, true));

            // /F перезаписывает уже существующую задачу — иначе смена пути к exe
            // после переустановки оставила бы задачу, указывающую в пустоту.
            return RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{xml}\" /F", out var output)
                ? null
                : $"schtasks: {output}";
        }
        finally
        {
            try { File.Delete(xml); } catch { }
        }
    }

    private static string? RemoveTask()
    {
        if (!HasTask()) return null;
        return RunSchtasks($"/Delete /TN \"{TaskName}\" /F", out var output)
            ? null
            : $"schtasks: {output}";
    }

    private static bool RunSchtasks(string arguments, out string output)
    {
        output = string.Empty;
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (process == null) return false;

            output = (process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd()).Trim();
            // Планировщик может задуматься, но не на минуты: без потолка отказ
            // службы подвесил бы окно настроек.
            if (!process.WaitForExit(10_000)) { try { process.Kill(); } catch { } return false; }
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            output = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// XML задачи. Вынесено отдельно и публично, чтобы проверять тестом: в этом
    /// файле четыре настройки, каждая из которых молча ломает автозапуск.
    ///
    /// Схема ЗАЯВЛЕНА как 1.2, и добавлять сюда элементы из 1.3 нельзя:
    /// schtasks отказывает целиком, «The task XML contains an unexpected node»,
    /// и автозапуска не появляется вовсе. Так ушли `UseUnifiedSchedulingEngine`
    /// и `DisallowStartOnRemoteAppSession` — оба задавали своё же значение по
    /// умолчанию и не стоили ни отказа, ни поднятия версии схемы.
    /// </summary>
    public static string BuildTaskXml(string exePath)
    {
        var user = $"{Environment.UserDomainName}\\{Environment.UserName}";
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>{Escape(L10n.T("autostart.task.description"))}</Description>
                <URI>\{TaskName}</URI>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{Escape(user)}</UserId>
                  <Delay>PT0S</Delay>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{Escape(user)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>LeastPrivilege</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>false</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <WakeToRun>false</WakeToRun>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>5</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{Escape(exePath)}</Command>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private static string Escape(string s) => s
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
        .Replace("\"", "&quot;").Replace("'", "&apos;");
}
