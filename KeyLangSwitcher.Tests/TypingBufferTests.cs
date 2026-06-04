using KeyLangSwitcher.Core;
using Xunit;

namespace KeyLangSwitcher.Tests;

public class TypingBufferTests
{
    [Fact]
    public void Append_AddsAtCursor_AndAdvancesIt()
    {
        var b = new TypingBuffer();
        b.Append('a'); b.Append('b'); b.Append('c');
        Assert.Equal("abc", b.Snapshot());
        Assert.Equal(3, b.CursorPosition);
    }

    [Fact]
    public void Backspace_DeletesLeftOfCursor()
    {
        var b = new TypingBuffer();
        b.Append('a'); b.Append('b'); b.Append('c');
        b.Backspace();
        Assert.Equal("ab", b.Snapshot());
        Assert.Equal(2, b.CursorPosition);
    }

    [Fact]
    public void Delete_RemovesAtCursor_DoesNotMoveIt()
    {
        var b = new TypingBuffer();
        b.Append('a'); b.Append('b'); b.Append('c');
        b.MoveLeft(); b.MoveLeft(); // cursor at 1
        b.Delete(); // removes 'b'
        Assert.Equal("ac", b.Snapshot());
        Assert.Equal(1, b.CursorPosition);
    }

    [Fact]
    public void InlineEdit_FixesTypoInMiddle()
    {
        var b = new TypingBuffer();
        foreach (var c in "vfvfxrf") b.Append(c);
        // Move cursor to index 4, replace 'f' at index 3 with 'j'
        b.MoveLeft(); b.MoveLeft(); b.MoveLeft();
        b.Backspace(); // removes char at index 2 ('v')? No, at cursor-1 = 3 ('f')
        b.Append('j');
        Assert.Equal("vfvjxrf", b.Snapshot());
    }

    [Fact]
    public void MoveLeft_PastStart_ClearsBuffer()
    {
        var b = new TypingBuffer();
        b.Append('a');
        b.MoveLeft();      // cursor at 0
        b.MoveLeft();      // would go negative — clear
        Assert.Equal(0, b.Length);
    }

    [Fact]
    public void MoveRight_PastEnd_ClearsBuffer()
    {
        var b = new TypingBuffer();
        b.Append('a');
        // cursor already at 1 == Length
        b.MoveRight();
        Assert.Equal(0, b.Length);
    }

    [Fact]
    public void Home_End_RepositionCursor_WithoutLoss()
    {
        var b = new TypingBuffer();
        foreach (var c in "abc") b.Append(c);
        b.MoveHome();
        Assert.Equal(0, b.CursorPosition);
        Assert.Equal("abc", b.Snapshot());
        b.MoveEnd();
        Assert.Equal(3, b.CursorPosition);
        Assert.Equal("abc", b.Snapshot());
    }

    [Fact]
    public void BackspaceAtStart_ClearsBuffer()
    {
        var b = new TypingBuffer();
        b.Append('a');
        b.MoveHome();
        b.Backspace(); // would delete BEFORE our zone → clear
        Assert.Equal(0, b.Length);
    }

    [Fact]
    public void DeleteAtEnd_ClearsBuffer()
    {
        var b = new TypingBuffer();
        b.Append('a');
        // cursor at end
        b.Delete();
        Assert.Equal(0, b.Length);
    }

    [Fact]
    public void Append_InMiddle_InsertsAtCursor()
    {
        var b = new TypingBuffer();
        foreach (var c in "ac") b.Append(c);
        b.MoveLeft();
        b.Append('b');
        Assert.Equal("abc", b.Snapshot());
        Assert.Equal(2, b.CursorPosition);
    }
}
