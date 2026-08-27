using System.Windows.Forms;
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

        // Single-instance guard
        using var mutex = new System.Threading.Mutex(initiallyOwned: true,
            name: "Global\\Oops_SingleInstance", out var createdNew);
        if (!createdNew)
        {
            Notice.Info(null, "oops уже запущен",
                "Программа работает и сейчас — просто не показывается окном.",
                "Ищите иконку в трее, у часов. Двойной клик по ней откроет настройки.");
            return;
        }

        // Гарантируем UI SynchronizationContext до создания App
        WindowsFormsSynchronizationContext.AutoInstall = true;
        _ = new Control(); // принудительно создаёт SyncContext в этом потоке

        var settings = AppSettings.Load();

        // Файл настроек был, но не прочитался. Молча вернуть дефолты — значит
        // отобрать настроенные хоткеи без единого слова, и человек решит, что
        // программа сломалась сама по себе.
        if (settings.LoadError != null)
            Notice.Warn(null, "Настройки не прочитались",
                "Файл настроек повреждён, программа запустилась с настройками по умолчанию.",
                $"Проверьте хоткеи в настройках — возможно, их придётся назначить заново.\n"
                + $"Файл: {AppSettings.Location}");

        App app;
        try
        {
            app = new App(settings);
        }
        catch (Exception ex)
        {
            // Без клавиатурного хука программа не делает вообще ничего, так что
            // это не «работаем дальше», а честный отказ запуститься.
            Notice.Error(null, "Не удалось перехватить клавиатуру",
                "Без этого горячие клавиши работать не могут, поэтому программа не запустится.",
                "Чаще всего мешает другая программа с глобальными хоткеями "
                + "(Punto Switcher, менеджер раскладок, античит). Закройте её и попробуйте снова.",
                ex.ToString(), reportContext: "Не удалось установить клавиатурный хук");
            return;
        }

        // Мастер первого запуска — до трея: программа с виду ничего не делает,
        // и без объяснения модели «расширяющейся области» её принимают за сломанную.
        // Показываем модально; ShowDialog крутит свой цикл сообщений, поэтому
        // Application.Run ещё не нужен.
        WelcomeForm.ShowIfFirstRun(app);

        var ctx = new TrayContext(app);
        Application.Run(ctx);
    }
}
