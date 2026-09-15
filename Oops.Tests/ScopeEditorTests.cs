using System;
using Oops.Core;
using Xunit;

namespace Oops.Tests;

public class ScopeEditorTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void FirstPress_ConvertsOnlyLastWord()
    {
        var s = new ScopeEditor();
        // "привет ghbdtn" — правильное русское слово и одно в EN-раскладке.
        var e = s.NextLayoutStep("привет ghbdtn", T0);

        Assert.Equal(6, e.EraseCount);            // длина "ghbdtn"
        Assert.Equal("привет", e.Text);           // ghbdtn -> привет
        Assert.Equal("привет привет", e.NewBufferContent);
    }

    [Fact]
    public void SecondPress_CoversWholeBuffer()
    {
        var s = new ScopeEditor();
        const string typed = "ghjdthrf njuj rfr 'nj hf,jnftn";

        var first = s.NextLayoutStep(typed, T0);
        Assert.Equal("работает", first.Text);
        Assert.Equal("hf,jnftn".Length, first.EraseCount);

        // Второе нажатие в пределах окна — сразу весь набранный текст.
        var second = s.NextLayoutStep(first.NewBufferContent, T0.AddMilliseconds(400));
        Assert.Equal("проверка того как это работает", second.Text);
        Assert.Equal(typed.Length, second.EraseCount);
        Assert.Equal("проверка того как это работает", second.NewBufferContent);
    }

    [Fact]
    public void ThirdPress_DoesNothing_ScopeAlreadyCoversEverything()
    {
        var s = new ScopeEditor();
        const string typed = "ghbdtn rfr ltkf";

        var a = s.NextLayoutStep(typed, T0);
        var b = s.NextLayoutStep(a.NewBufferContent, T0.AddMilliseconds(300));
        Assert.Equal("привет как дела", b.Text);

        var c = s.NextLayoutStep(b.NewBufferContent, T0.AddMilliseconds(600));
        Assert.True(c.IsEmpty);
    }

    [Fact]
    public void SlowSecondPress_ContinuesInsteadOfUndoingTheFirst()
    {
        // Регрессия и жалоба пользователя: человек нажимает, СМОТРИТ на
        // результат, нажимает снова — а окно в две секунды уже вышло. Раньше
        // начиналась новая сессия, брала только что исправленное последнее
        // слово и возвращала его назад: второе нажатие уходило впустую, до
        // цели требовалось три.
        //
        // Если после нашей правки ничего не набирали, буфер ровно такой, каким
        // мы его оставили, — это продолжение, сколько бы времени ни прошло.
        var s = new ScopeEditor { ExpandWindow = TimeSpan.FromSeconds(2) };
        var first = s.NextLayoutStep("ghbdtn rfr ltkf", T0);
        Assert.Equal("дела", first.Text);

        var later = s.NextLayoutStep(first.NewBufferContent, T0.AddSeconds(5));
        Assert.Equal("привет как дела", later.Text);
    }

    [Fact]
    public void TypingAfterOurEdit_StartsAFreshScope()
    {
        // Обратная сторона: буфер отличается от того, что мы оставили, —
        // значит человек печатал, и область начинается заново.
        var s = new ScopeEditor { ExpandWindow = TimeSpan.FromSeconds(2) };
        var first = s.NextLayoutStep("ghbdtn rfr ltkf", T0);
        Assert.Equal("дела", first.Text);

        // «ytn», а не «tot»: «tot» — настоящее английское слово, и модель
        // языка оставляет его в покое совершенно правильно. Тест про область,
        // а не про выбор слов.
        var typedMore = first.NewBufferContent + " ytn";
        var next = s.NextLayoutStep(typedMore, T0.AddSeconds(5));
        Assert.Equal("нет", next.Text);           // снова одно последнее слово
        Assert.Equal(3, next.EraseCount);
    }

    [Fact]
    public void ExpandingBeyondWordCount_DoesNothingMore()
    {
        var s = new ScopeEditor();
        var a = s.NextLayoutStep("vfvf", T0);
        Assert.Equal("мама", a.Text);

        var b = s.NextLayoutStep(a.NewBufferContent, T0.AddMilliseconds(200));
        Assert.True(b.IsEmpty); // слово было одно — второй шаг захватил бы то же самое
    }

    [Fact]
    public void CaseStep_UsesSameScopeModel()
    {
        var s = new ScopeEditor();
        var a = s.NextCaseStep("привет мир", T0);
        Assert.Equal("МИР", a.Text);

        // Второй шаг берёт весь текст, пересчитывая от замороженного оригинала
        // ("привет мир"), а не от того, что уже на экране.
        var b = s.NextCaseStep(a.NewBufferContent, T0.AddMilliseconds(300));
        Assert.Equal("ПРИВЕТ МИР", b.Text);
        Assert.Equal("привет мир".Length, b.EraseCount);
    }

    [Fact]
    public void SwitchingHotkeyKind_StartsFreshScope()
    {
        var s = new ScopeEditor();
        var layout = s.NextLayoutStep("ghbdtn rfr", T0);
        Assert.Equal("как", layout.Text);

        // Другой хоткей — новая сессия, снова последнее слово.
        var casing = s.NextCaseStep(layout.NewBufferContent, T0.AddMilliseconds(200));
        Assert.Equal("КАК", casing.Text);
    }

    [Fact]
    public void ExpansionSurvivesSlowSteps_WhenPressesAreTimedByKeyDown()
    {
        // Регрессия: момент нажатия должен фиксироваться в обработчике клавиши,
        // а не после ожидания модификаторов и посимвольной печати. Иначе на
        // длинных словах наша собственная задержка съедает окно расширения.
        var s = new ScopeEditor { ExpandWindow = TimeSpan.FromSeconds(2) };
        const string typed = "ghbdtn rfr ltkf";

        var a = s.NextLayoutStep(typed, T0);
        Assert.Equal("дела", a.Text);

        var b = s.NextLayoutStep(a.NewBufferContent, T0.AddMilliseconds(1500));
        Assert.Equal("привет как дела", b.Text);
    }

    [Fact]
    public void ResetSession_ForcesNextPressToStartOver()
    {
        var s = new ScopeEditor();
        var a = s.NextLayoutStep("ghbdtn rfr ltkf", T0);
        s.ResetSession();

        var b = s.NextLayoutStep(a.NewBufferContent, T0.AddMilliseconds(100));
        Assert.Equal(4, b.EraseCount);   // снова одно слово, не два
    }

    [Fact]
    public void HammeringExhaustedScope_DoesNotFreezeTheExpandWindow()
    {
        // Регрессия: пустой шаг не должен продлевать окно расширения. Иначе
        // человек, который в сердцах жмёт хоткей раз в секунду, бесконечно
        // попадает в «расширять некуда» и не видит вообще никакой реакции.
        var s = new ScopeEditor { ExpandWindow = TimeSpan.FromSeconds(2) };

        var a = s.NextCaseStep("привет мир", T0);
        Assert.Equal("МИР", a.Text);

        var b = s.NextCaseStep(a.NewBufferContent, T0.AddMilliseconds(300));
        Assert.Equal("ПРИВЕТ МИР", b.Text);

        // Область развёрнута до конца — эти нажатия молчат…
        Assert.True(s.NextCaseStep(b.NewBufferContent, T0.AddMilliseconds(900)).IsEmpty);
        Assert.True(s.NextCaseStep(b.NewBufferContent, T0.AddMilliseconds(1600)).IsEmpty);

        // …но отсчёт идёт от последнего РЕЗУЛЬТАТИВНОГО шага (300 мс), поэтому
        // через две секунды после него начинается новая сессия.
        var again = s.NextCaseStep(b.NewBufferContent, T0.AddMilliseconds(2400));
        Assert.Equal("мир", again.Text);   // "МИР" обратно в нижний регистр
    }

    [Theory]
    [InlineData("Hello", "hello")]
    [InlineData("hello", "HELLO")]
    [InlineData("Привет Мир", "привет мир")]
    [InlineData("привет мир", "ПРИВЕТ МИР")]
    public void ToggleCase_LowersWhenAnyUpper(string input, string expected)
    {
        Assert.Equal(expected, ScopeEditor.ToggleCase(input));
    }

    [Fact]
    public void MixedLayoutText_EachWordGoesItsOwnWay()
    {
        // Жалоба пользователя: часть слов набрана в английской раскладке вместо
        // русской, последнее — наоборот. При одном направлении на весь кусок
        // побеждало большинство букв, и кириллическое слово оставалось как есть.
        var s = new ScopeEditor();
        const string typed = "Z djn [jxe pfgecnbnm ЬщвудКшыл";

        var first = s.NextLayoutStep(typed, T0);
        Assert.Equal("ModelRisk", first.Text);    // последнее слово — RU→EN

        var second = s.NextLayoutStep(first.NewBufferContent, T0.AddMilliseconds(300));
        Assert.Equal("Я вот хочу запустить ModelRisk", second.Text);
        Assert.Equal(typed.Length, second.EraseCount);
    }

    [Fact]
    public void ConversionPreservesLength_SoEraseCountStaysCorrect()
    {
        // На этом свойстве держится вся арифметика стирания.
        const string typed = "crf;b ult dpznm";
        var s = new ScopeEditor();
        var e = s.NextLayoutStep(typed, T0);
        Assert.Equal(e.EraseCount, e.Text.Length);
    }
}
