using Oops.UI;
using Xunit;

namespace Oops.Tests;

public class PrettifyNotesTests
{
    [Fact]
    public void GithubGeneratedNotes_BecomeReadableText()
    {
        // Ровно то, что генерирует action-gh-release с generate_release_notes.
        const string raw =
            "## What's Changed\n"
            + "* 1.2.0: пакетная вставка, окна ошибок, SHA-256 у обновлений "
            + "by @vladvysotsky in https://github.com/vladvysotsky/oops/pull/4\n"
            + "\n"
            + "\n"
            + "**Full Changelog**: https://github.com/vladvysotsky/oops/compare/v1.1.0...v1.2.0";

        var pretty = UpdateDialog.PrettifyNotes(raw);

        Assert.Equal(
            "What's Changed\n"
            + "• 1.2.0: пакетная вставка, окна ошибок, SHA-256 у обновлений (#4)",
            pretty);
    }

    [Fact]
    public void MarkdownSyntax_IsStripped()
    {
        var pretty = UpdateDialog.PrettifyNotes(
            "### Заголовок\n- **жирный** пункт с [ссылкой](https://example.com) и `кодом`");
        Assert.Equal("Заголовок\n• жирный пункт с ссылкой и кодом", pretty);
    }

    [Fact]
    public void EmptyOrWhitespace_GivesEmpty()
    {
        Assert.Equal(string.Empty, UpdateDialog.PrettifyNotes(null));
        Assert.Equal(string.Empty, UpdateDialog.PrettifyNotes("  \n \n"));
    }

    [Fact]
    public void PlainText_PassesThroughUnchanged()
    {
        Assert.Equal("Просто описание релиза.", UpdateDialog.PrettifyNotes("Просто описание релиза."));
    }
}
