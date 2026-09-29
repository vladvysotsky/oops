using System.Windows.Forms;

namespace Oops.Hooks;

/// <summary>
/// Отдельный поток с собственным циклом сообщений — дом для низкоуровневых
/// хуков приложения.
///
/// Колбэк WH_KEYBOARD_LL/WH_MOUSE_LL вызывается в том потоке, который поставил
/// хук, и только когда этот поток качает сообщения. Раньше это был UI-поток, а
/// он же ждёт отпускания модификаторов, печатает текст пачками со Sleep между
/// ними и ждёт возврата фокуса. Всё это время колбэк не мог выполниться — а
/// через наш хук проходят в том числе НАШИ ЖЕ нажатия из SendInput. Windows
/// ждёт ответа LowLevelHooksTimeout (300 мс) и молча снимает хук. Снаружи:
/// «программа в какой-то момент перестала работать». Сторож раз в 20 секунд
/// это замечал и ставил хук заново — то есть лечил последствие.
///
/// Здесь поток не делает ничего, кроме приёма ввода: решение «проглотить или
/// пропустить» принимается сразу, а вся тяжёлая работа уходит в UI-поток через
/// Post. Замолчать на 300 мс ему нечем.
///
/// Цикл — обычный Application.Run с WindowsFormsSynchronizationContext: это
/// проверенная машинерия WinForms, а не самописный GetMessage, и через тот же
/// контекст можно выполнить код в этом потоке (<see cref="Invoke"/>). Это
/// нужно для SetWindowsHookEx: хук ставится только из своего потока.
///
/// Никогда не ждите из этого потока UI-поток синхронно (Send/Invoke): UI-поток
/// сам ждёт этот через <see cref="Invoke"/>, и получилась бы взаимная блокировка.
/// Только Post.
/// </summary>
public sealed class HookThread : IDisposable
{
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private SynchronizationContext? _context;
    private bool _disposed;

    public HookThread(string name)
    {
        _thread = new Thread(Run)
        {
            Name = name,
            // Фоновый: зависший по какой-то причине поток ввода не должен
            // держать процесс живым после выхода из программы.
            IsBackground = true,
        };
        // Контексту WinForms нужен STA-поток: он создаёт скрытый контрол для
        // маршалинга вызовов.
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait();
    }

    /// <summary>Выполняется ли код прямо сейчас в потоке ввода.</summary>
    public bool IsCurrent => Thread.CurrentThread == _thread;

    private void Run()
    {
        var context = new WindowsFormsSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(context);
        _context = context;
        _ready.Set();

        // Без формы: цикл сообщений ради хуков и вызовов через контекст, и
        // ничего больше. Выход — Application.ExitThread из Dispose.
        Application.Run();
    }

    /// <summary>
    /// Выполняет действие в потоке ввода и ждёт его завершения. Исключение
    /// пробрасывается вызывающему: WindowsFormsSynchronizationContext.Send
    /// маршалит его обратно, так что отказ SetWindowsHookEx не потеряется.
    /// </summary>
    public void Invoke(Action action)
    {
        if (IsCurrent) { action(); return; }
        ObjectDisposedException.ThrowIf(_disposed, this);
        _context!.Send(_ => action(), null);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _context?.Post(_ => Application.ExitThread(), null);
        // Не ждём вечно: при выходе из программы лучше потерять аккуратное
        // завершение фонового потока, чем повиснуть на нём.
        _thread.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }
}
