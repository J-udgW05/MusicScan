using System.Globalization;
using MusicScanIntegrity.Core.Common;
using MusicScanIntegrity.Core.Settings;
using Xunit;

namespace MusicScanIntegrity.Core.Tests;

/// <summary>Number, size and plural formatting in both interface languages.</summary>
public sealed class FormatLanguageTests
{
    private const char Nbsp = Format.NarrowSpace;

    [Theory]
    [InlineData(1, "One")]
    [InlineData(2, "Few")]
    [InlineData(4, "Few")]
    [InlineData(5, "Many")]
    [InlineData(11, "Many")]
    [InlineData(14, "Many")]
    [InlineData(21, "One")]
    [InlineData(22, "Few")]
    [InlineData(111, "Many")]
    [InlineData(0, "Many")]
    public void Russian_plural_categories(long count, string expected)
    {
        using CultureScope _ = new(AppLanguage.Russian);
        Assert.Equal(expected, Format.PluralForm(count));
    }

    [Theory]
    [InlineData(1, "One")]
    [InlineData(2, "Many")]
    [InlineData(21, "Many")]
    [InlineData(0, "Many")]
    public void English_plural_categories(long count, string expected)
    {
        using CultureScope _ = new(AppLanguage.English);
        Assert.Equal(expected, Format.PluralForm(count));
    }

    [Fact]
    public void Counted_phrases_agree_in_both_languages()
    {
        using (new CultureScope(AppLanguage.Russian))
        {
            Assert.Equal("1 файл", Format.Files(1));
            Assert.Equal("3 файла", Format.Files(3));
            Assert.Equal($"12{Nbsp}480 файлов", Format.Files(12480));
        }

        using (new CultureScope(AppLanguage.English))
        {
            Assert.Equal("1 file", Format.Files(1));
            Assert.Equal("3 files", Format.Files(3));
            Assert.Equal("12,480 files", Format.Files(12480));
        }
    }

    [Fact]
    public void English_numbers_use_comma_groups_and_decimal_point()
    {
        using CultureScope _ = new(AppLanguage.English);

        Assert.Equal("12,480", Format.Number(12480));
        Assert.Equal($"24.1{Nbsp}MB", Format.Size(25_260_032));
        Assert.Equal($"612{Nbsp}KB", Format.Size(626_688));
        Assert.Equal("0.6%", Format.Percent(0.006));
    }

    [Fact]
    public void Russian_numbers_keep_their_conventions()
    {
        using CultureScope _ = new(AppLanguage.Russian);

        Assert.Equal($"12{Nbsp}480", Format.Number(12480));
        Assert.Equal($"24,1{Nbsp}МБ", Format.Size(25_260_032));
        Assert.Equal($"0,6{Nbsp}%", Format.Percent(0.006));
    }

    [Fact]
    public void Duration_words_follow_language()
    {
        TimeSpan span = TimeSpan.FromMinutes(18) + TimeSpan.FromSeconds(42);

        using (new CultureScope(AppLanguage.Russian))
        {
            Assert.Equal("18 минут 42 секунды", Format.DurationWords(span));
        }

        using (new CultureScope(AppLanguage.English))
        {
            Assert.Equal("18 minutes 42 seconds", Format.DurationWords(span));
            Assert.Equal("less than a second", Format.DurationWords(TimeSpan.FromMilliseconds(200)));
        }
    }
}

/// <summary>Switches the UI culture for the lifetime of the scope.</summary>
internal sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _previous = CultureInfo.CurrentUICulture;

    public CultureScope(string language)
    {
        CultureInfo.CurrentUICulture = AppLanguage.ToCulture(language);
    }

    public void Dispose() => CultureInfo.CurrentUICulture = _previous;
}
