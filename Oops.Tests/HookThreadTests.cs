using Oops.Hooks;
using Xunit;

namespace Oops.Tests;

public class HookThreadTests
{
    [Fact]
    public void InvokeRunsOnTheInputThreadNotTheCaller()
    {
        // Весь смысл потока ввода: хук ставится и колбэк выполняется НЕ в
        // вызывающем (UI) потоке, который может спать в Sender.
        using var thread = new HookThread("test input");
        int caller = Environment.CurrentManagedThreadId;
        int inside = caller;
        bool current = false;

        thread.Invoke(() =>
        {
            inside = Environment.CurrentManagedThreadId;
            current = thread.IsCurrent;
        });

        Assert.NotEqual(caller, inside);
        Assert.True(current);
        Assert.False(thread.IsCurrent);
    }

    [Fact]
    public void InvokeFromInsideTheThreadDoesNotDeadlock()
    {
        // Revive вызывает InstallCore через тот же Invoke; если Invoke из самого
        // потока ввода ждал бы сам себя, сторож повесил бы программу.
        using var thread = new HookThread("test input");
        bool reached = false;

        var task = Task.Run(() => thread.Invoke(() => thread.Invoke(() => reached = true)));

        Assert.True(task.Wait(TimeSpan.FromSeconds(5)), "вложенный Invoke завис");
        Assert.True(reached);
    }

    [Fact]
    public void ExceptionsReachTheCaller()
    {
        // Отказ SetWindowsHookEx бросается внутри потока ввода. Потеряйся он там —
        // программа стартовала бы без хука и без единого сообщения об этом.
        using var thread = new HookThread("test input");

        var ex = Record.Exception(() =>
            thread.Invoke(() => throw new InvalidOperationException("boom")));

        Assert.NotNull(ex);
        Assert.Contains("boom", (ex!.InnerException ?? ex).Message);
    }

    [Fact]
    public void DisposeStopsTheThreadAndRefusesFurtherWork()
    {
        var thread = new HookThread("test input");
        thread.Invoke(() => { });

        thread.Dispose();

        Assert.Throws<ObjectDisposedException>(() => thread.Invoke(() => { }));
        thread.Dispose(); // повторный Dispose безопасен
    }
}
