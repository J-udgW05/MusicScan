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
    public void Normal_text_is_not_mojibake(string? value)
    {
        Assert.False(TextIntegrity.LooksBroken(value), $"текст «{value}» назван сломанным");
    }

    [Fact]
    public void Cyrillic_read_as_western_table_is_detected()
    {
        // "Привет" in CP1251 read through the Western European table.
        Assert.True(TextIntegrity.LooksBroken("Ïðèâåò"));
        Assert.Contains("западноевропейской", TextIntegrity.Describe("Ïðèâåò")!, StringComparison.Ordinal);
    }

    [Fact]
    public void Utf8_read_byte_by_byte_is_detected()
    {
        // "Привет" in UTF-8 read byte by byte.
        string broken = "ÐŸÑ€Ð¸Ð²ÐµÑ‚";

        Assert.True(TextIntegrity.LooksBroken(broken));
        Assert.Contains("UTF-8", TextIntegrity.Describe(broken)!, StringComparison.Ordinal);
    }

    [Fact]
    public void Replacement_characters_count_as_broken()
    {
        Assert.True(TextIntegrity.LooksBroken("Пес�ня"));
    }

    [Fact]
    public void Too_short_text_is_not_judged()
    {
        // Two letters are easier to misjudge than to get right.
        Assert.False(TextIntegrity.LooksBroken("Ïð"));
    }

    [Fact]
    public void Broken_fields_are_listed()
    {
        IReadOnlyList<string> broken = TextIntegrity.BrokenFields(
            ("Название", "Ïåñíÿ"),
            ("Исполнитель", "Queen"),
            ("Альбом", "Ãðóïïà êðîâè"));

        Assert.Equal(["название", "альбом"], broken);
    }
}
