using System.Windows.Forms;
using Oops.Core;
using Oops.Settings;
using Oops.UI;

namespace Oops;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Ловим всё, что не поймали по дороге. Без этого Windows показывает
        // системное окно .NET со стеком: пользователю оно не говорит ничего,
        // а нам не приносит ничего — человек просто закрывает его и уходит.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Notice.Crash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Notice.Crash(ex);
        };

        // Single-instance guard.
        // Local\, а не Global\: глобальное имя видно всем сессиям машины, и
        // любой другой пользователь (или процесс с низкими правами) мог бы
        // создать mutex заранее — программа навсегда считала бы себя «уже
        // запущенной». Приложение per-user, и границы сессии ему достаточно.
        using var mutex = new System.Threading.Mutex(initiallyOwned: true,
            name: "Local\\Oops_SingleInstance", out var createdNew);
        if (!createdNew)
        {
            // L10n ещё не инициализирован (настройки не читали) — берём язык
            // системы: до чтения настроек это единственный доступный источник.
            L10n.Init(L10n.Auto);
            Notice.Info(null, L10n.T("running.title"),
                L10n.T("running.body"), L10n.T("running.hint"));
            return;
        }

        // Гарантируем UI SynchronizationContext до создания App
        WindowsFormsSynchronizationContext.AutoInstall = true;
        _ = new Control(); // принудительно создаёт SyncContext в этом потоке

        var settings = AppSettings.Load();
        // Лог включаем ДО всего остального: разбирать «не запускается» по логу,
        // который начинается после запуска, бессмысленно.
        if (settings.VerboseLog) Log.Start();
        L10n.Init(settings.Language);
        Theme.Init(settings.Theme);

        // Файл настроек был, но не прочитался. Молча вернуть дефолты — значит
        // отобрать настроенные хоткеи без единого слова, и человек решит, что
        // программа сломалась сама по себе.
        if (settings.LoadError != null)
            Notice.Warn(null, L10n.T("settings.unreadable.title"),
                L10n.T("settings.unreadable.body"),
                L10n.T("settings.unreadable.hint", AppSettings.Location));

        App app;
        try
        {
            app = new App(settings);
        }
        catch (Exception ex)
        {
            // Без клавиатурного хука программа не делает вообще ничего, так что
            // это не «работаем дальше», а честный отказ запуститься.
            Notice.Error(null, L10n.T("hook.failed.title"),
                L10n.T("hook.failed.body"), L10n.T("hook.failed.hint"),
                ex.ToString(), reportContext: "Не удалось установить клавиатурный хук");
            return;
        }

        // Мастер первого запуска — до трея: программа с виду ничего не делает,
        // и без объяснения модели «расширяющейся области» её принимают за сломанную.
        // Показываем модально; ShowDialog крутит свой цикл сообщений, поэтому
        // Application.Run ещё не нужен.
        WelcomeForm.ShowIfFirstRun(app);

        // «Что нового» — сразу после мастера и до трея: обновление проходит
        // молча, и без этого окна о новой функции узнают случайно, а хоткей,
        // о котором не знают, ничем не отличается от отсутствующего.
        WhatsNewForm.ShowIfUpdated(settings);

        // Занятое кем-то сочетание — вторая по частоте причина «ничего не
        // происходит» после того, как программа просто не запущена.
        var taken = HotkeyConflicts.Find(new[]
        {
            (L10n.T("hotkey.layout"), settings.ConvertHotkey),
            (L10n.T("hotkey.case"), settings.ChangeCaseHotkey),
            (L10n.T("hotkey.translate"), settings.TranslateHotkey),
            (L10n.T("hotkey.voice"), settings.VoiceHotkey),
            (L10n.T("hotkey.replace"), settings.ReplaceHotkey),
        });
        if (taken.Count > 0)
            Notice.Warn(null, L10n.T("conflict.title"),
                L10n.T("conflict.body", string.Join("\n", taken)),
                L10n.T("conflict.hint"));

        // Сколько прошло от старта процесса до готовности. Именно эта цифра
        // отвечает на вопрос «почему программа появляется через десять секунд
        // после входа»: если здесь 200 мс, тормозим не мы, а растяжка запуска
        // Windows — и лечится она ранним автозапуском через планировщик.
        // Если здесь секунды — виновата распаковка single-file exe, и
        // планировщик не поможет.
        if (Log.Enabled)
        {
            try
            {
                using var self = System.Diagnostics.Process.GetCurrentProcess();
                Log.Write($"готов через {(DateTime.Now - self.StartTime).TotalMilliseconds:F0} мс после старта процесса");
            }
            catch { }
        }

        var ctx = new TrayContext(app);
        Application.Run(ctx);
    }
}
