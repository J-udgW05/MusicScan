using MusicScanIntegrity.Core.Analysis;
using Xunit;

namespace MusicScanIntegrity.Core.Tests;

public sealed class TextIntegrityTests
{
    [Theory]
    [InlineData("Привет, мир")]
    [InlineData("Nirvana - Smells Like Teen Spirit")]
    [InlineData("Café del Mar")]
    [InlineData("Motörhead")]
    [InlineData("Björk — Jóga")]
    [InlineData("Ça va bien")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("AC/DC")]
    public void Нормальный_текст_кракозябрами_не_считается(string? value)
    {
        Assert.False(TextIntegrity.LooksBroken(value), $"текст «{value}» назван сломанным");
    }

    [Fact]
    public void Кириллица_прочитанная_западной_таблицей_ловится()
    {
        // «Привет» в CP1251, прочитанное как западноевропейская таблица.
        Assert.True(TextIntegrity.LooksBroken("Ïðèâåò"));
        Assert.Contains("западноевропейской", TextIntegrity.Describe("Ïðèâåò")!, StringComparison.Ordinal);
    }

    [Fact]
    public void Побайтно_прочитанный_UTF8_ловится()
    {
        // «Привет» в UTF-8, прочитанное побайтно.
        string broken = "ÐŸÑ€Ð¸Ð²ÐµÑ‚";

        Assert.True(TextIntegrity.LooksBroken(broken));
        Assert.Contains("UTF-8", TextIntegrity.Describe(broken)!, StringComparison.Ordinal);
    }

    [Fact]
    public void Знаки_замены_считаются_поломкой()
    {
        Assert.True(TextIntegrity.LooksBroken("Пес�ня"));
    }

    [Fact]
    public void Слишком_короткий_текст_не_разбирается()
    {
        // На двух буквах ошибиться проще, чем угадать.
        Assert.False(TextIntegrity.LooksBroken("Ïð"));
    }

    [Fact]
    public void Сломанные_поля_перечисляются()
    {
        IReadOnlyList<string> broken = TextIntegrity.BrokenFields(
            ("Название", "Ïåñíÿ"),
            ("Исполнитель", "Queen"),
            ("Альбом", "Ãðóïïà êðîâè"));

        Assert.Equal(["название", "альбом"], broken);
    }
}
