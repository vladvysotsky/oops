using System.Text;

namespace KeyLangSwitcher.Core;

/// <summary>
/// Накапливает символы, набранные пользователем с момента последнего сброса.
/// Сброс происходит на Enter/Tab/Esc/стрелках/смене окна/таймауте.
/// </summary>
public sealed class TypingBuffer
{
    private readonly StringBuilder _buffer = new();
    private DateTime _lastInputUtc = DateTime.UtcNow;
    private readonly object _gate = new();

    /// <summary>Таймаут бездействия, после которого буфер автоматически сбрасывается.</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public string Snapshot()
    {
        lock (_gate) return _buffer.ToString();
    }

    public int Length
    {
        get { lock (_gate) return _buffer.Length; }
    }

    public void Append(char c)
    {
        lock (_gate)
        {
            EvictIfIdleLocked();
            _buffer.Append(c);
            _lastInputUtc = DateTime.UtcNow;
        }
    }

    public void Backspace()
    {
        lock (_gate)
        {
            if (_buffer.Length > 0) _buffer.Length--;
            _lastInputUtc = DateTime.UtcNow;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _buffer.Clear();
            _lastInputUtc = DateTime.UtcNow;
        }
    }

    private void EvictIfIdleLocked()
    {
        if (DateTime.UtcNow - _lastInputUtc > IdleTimeout)
            _buffer.Clear();
    }
}
