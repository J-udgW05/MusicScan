using MusicScanIntegrity.App.Converters;
using Xunit;

namespace MusicScanIntegrity.App.Tests;

/// <summary>The status bar shows only when enabled and holding something.</summary>
public sealed class StatusBarTests
{
    [Theory]
    [InlineData(false, "", "", false)]
    [InlineData(false, "5 files · 0 checked", @"D:\Music", false)]
    [InlineData(true, "", "", false)]
    [InlineData(true, "   ", null, false)]
    [InlineData(true, "5 files · 0 checked", "", true)]
    [InlineData(true, "", @"D:\Music", true)]
    [InlineData(true, "5 files · 0 checked", @"D:\Music", true)]
    public void Bar_is_visible_only_when_enabled_and_not_empty(bool enabled, string? counts, string? path, bool expected)
    {
        Assert.Equal(expected, StatusBarVisibilityConverter.IsVisible(enabled, counts, path));
    }

    [Theory]
    [InlineData("", "", false)]
    [InlineData("5 files · 0 checked", "", false)]
    [InlineData("", @"D:\Music", false)]
    [InlineData("5 files · 0 checked", @"D:\Music", true)]
    public void Separator_needs_both_sides(string counts, string path, bool expected)
    {
        Assert.Equal(expected, AllNotEmptyToVisibilityConverter.AllPresent(counts, path));
    }
}
