using System.Text;

namespace KeyLangSwitcher.Core;

/// <summary>
/// Накапливает символы, набранные пользователем, и поддерживает позицию курсора,
/// чтобы зеркально отражать редактирование (Backspace/Delete/Left/Right) в активном поле.
///
/// Сброс происходит, если курсор пытается выйти за границы накопленного буфера —
/// значит пользователь редактирует текст, которого мы не видели.
/// </summary>
public sealed class TypingBuffer
{
    private readonly StringBuilder _buffer = new();
    private int _cursor; // позиция курсора: 0..Length
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

    public int CursorPosition
    {
        get { lock (_gate) return _cursor; }
    }

    public void Append(char c)
    {
        lock (_gate)
        {
            EvictIfIdleLocked();
            _buffer.Insert(_cursor, c);
            _cursor++;
            _lastInputUtc = DateTime.UtcNow;
        }
    }

    /// <summary>Удаление слева от курсора (как Backspace в текстовом поле).</summary>
    public void Backspace()
    {
        lock (_gate)
        {
            if (_cursor > 0)
            {
                _buffer.Remove(_cursor - 1, 1);
                _cursor--;
            }
            else
            {
                // курсор уже в начале наших данных — Backspace стёр символ ДО нашей зоны,
                // буфер больше не соответствует тому, что на экране
                ClearLocked();
            }
            _lastInputUtc = DateTime.UtcNow;
        }
    }

    /// <summary>Удаление справа от курсора (как Delete).</summary>
    public void Delete()
    {
        lock (_gate)
        {
            if (_cursor < _buffer.Length)
            {
                _buffer.Remove(_cursor, 1);
            }
            else
            {
                // курсор у конца наших данных — Delete стёр символ ПОСЛЕ нашей зоны
                ClearLocked();
            }
            _lastInputUtc = DateTime.UtcNow;
        }
    }

    public void MoveLeft()
    {
        lock (_gate)
        {
            if (_cursor > 0) _cursor--;
            else ClearLocked(); // ушли за начало — мы потеряли контекст
            _lastInputUtc = DateTime.UtcNow;
        }
    }

    public void MoveRight()
    {
        lock (_gate)
        {
            if (_cursor < _buffer.Length) _cursor++;
            else ClearLocked();
            _lastInputUtc = DateTime.UtcNow;
        }
    }

    public void MoveHome()
    {
        lock (_gate)
        {
            _cursor = 0;
            _lastInputUtc = DateTime.UtcNow;
        }
    }

    public void MoveEnd()
    {
        lock (_gate)
        {
            _cursor = _buffer.Length;
            _lastInputUtc = DateTime.UtcNow;
        }
    }

    public void Clear()
    {
        lock (_gate) ClearLocked();
    }

    private void ClearLocked()
    {
        _buffer.Clear();
        _cursor = 0;
        _lastInputUtc = DateTime.UtcNow;
    }

    private void EvictIfIdleLocked()
    {
        if (DateTime.UtcNow - _lastInputUtc > IdleTimeout)
            ClearLocked();
    }
}
