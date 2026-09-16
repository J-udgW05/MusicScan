using System.Globalization;
using System.Resources;
using System.Xml.Linq;
using MusicScanIntegrity.Core.Resources;
using MusicScanIntegrity.Core.Settings;
using Xunit;

namespace MusicScanIntegrity.Core.Tests;

/// <summary>Interface language selection and the string catalogues.</summary>
public sealed class AppLanguageTests
{
    [Theory]
    [InlineData("ru", "RU", AppLanguage.Russian)]
    [InlineData("uk", "UA", AppLanguage.Russian)]
    [InlineData("be", "BY", AppLanguage.Russian)]
    [InlineData("uk", null, AppLanguage.Russian)]
    [InlineData("en", "RU", AppLanguage.Russian)]
    [InlineData("en", "UA", AppLanguage.Russian)]
    [InlineData("de", "BY", AppLanguage.Russian)]
    [InlineData("en", "US", AppLanguage.English)]
    [InlineData("de", "DE", AppLanguage.English)]
    [InlineData("kk", "KZ", AppLanguage.English)]
    [InlineData(null, null, AppLanguage.English)]
    public void First_run_derives_language_from_system(string? language, string? region, string expected)
    {
        Assert.Equal(expected, AppLanguage.Resolve(null, language, region));
    }

    [Theory]
    [InlineData("en", "ru", "RU")]
    [InlineData("ru", "en", "US")]
    [InlineData("EN", "ru", "RU")]
    public void Stored_choice_overrides_system(string stored, string systemLanguage, string systemRegion)
    {
        Assert.Equal(stored.ToLowerInvariant(), AppLanguage.Resolve(stored, systemLanguage, systemRegion));
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("")]
    [InlineData("russian")]
    public void Unknown_stored_value_counts_as_unset(string stored)
    {
        Assert.Equal(AppLanguage.Russian, AppLanguage.Resolve(stored, "ru", "RU"));
        Assert.Equal(AppLanguage.English, AppLanguage.Resolve(stored, "en", "US"));
    }

    [Fact]
    public async Task Chosen_language_survives_round_trip()
    {
        using TempDirectory folder = new();
        string path = Path.Combine(folder.Path, "settings.json");

        JsonSettingsService first = new(path);
        await first.LoadAsync();
        Assert.Null(first.Current.Language);

        AppSettings changed = first.Current.Clone();
        changed.Language = AppLanguage.English;
        await first.ApplyAsync(changed);

        JsonSettingsService second = new(path);
        await second.LoadAsync();
        Assert.Equal(AppLanguage.English, second.Current.Language);
    }

    [Fact]
    public void Sanitising_drops_unknown_language()
    {
        AppSettings settings = new() { Language = "fr" };
        Assert.Null(JsonSettingsService.Sanitize(settings).Language);

        settings.Language = "EN";
        Assert.Equal(AppLanguage.English, JsonSettingsService.Sanitize(settings).Language);
    }

    [Fact]
    public void Clone_carries_language()
    {
        AppSettings settings = new() { Language = AppLanguage.English };
        Assert.Equal(AppLanguage.English, settings.Clone().Language);
    }

    [Fact]
    public void Lookup_follows_current_ui_culture()
    {
        CultureInfo saved = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = AppLanguage.ToCulture(AppLanguage.English);
            Assert.Equal("Language", Strings.Settings_Language);

            CultureInfo.CurrentUICulture = AppLanguage.ToCulture(AppLanguage.Russian);
            Assert.Equal("Язык", Strings.Settings_Language);
        }
        finally
        {
            CultureInfo.CurrentUICulture = saved;
        }
    }

    [Fact]
    public void Both_catalogues_define_the_same_keys()
    {
        Dictionary<string, string> russian = ReadCatalogue("Strings.resx");
        Dictionary<string, string> english = ReadCatalogue("Strings.en.resx");

        Assert.Empty(russian.Keys.Except(english.Keys));
        Assert.Empty(english.Keys.Except(russian.Keys));
    }

    [Fact]
    public void English_catalogue_has_no_cyrillic()
    {
        string[] offenders = [.. ReadCatalogue("Strings.en.resx")
            .Where(pair => pair.Value.Any(c => c is >= '\u0400' and <= '\u04FF'))
            .Select(pair => pair.Key)];

        Assert.Empty(offenders);
    }

    [Fact]
    public void Every_english_string_resolves_from_satellite()
    {
        ResourceSet? set = Strings.ResourceManager.GetResourceSet(
            AppLanguage.ToCulture(AppLanguage.English), createIfNotExists: true, tryParents: false);

        Assert.NotNull(set);
        Assert.Equal(ReadCatalogue("Strings.en.resx").Count, set.Cast<object>().Count());
    }

    /// <summary>Reads a catalogue from the source tree.</summary>
    internal static Dictionary<string, string> ReadCatalogue(string fileName)
    {
        string path = Path.Combine(RepositoryRoot(), "src", "MusicScanIntegrity.Core", "Resources", fileName);
        return XDocument.Load(path).Root!
            .Elements("data")
            .ToDictionary(e => (string)e.Attribute("name")!, e => (string?)e.Element("value") ?? string.Empty);
    }

    /// <summary>The repository root, found by walking up to the solution file.</summary>
    internal static string RepositoryRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MusicScanIntegrity.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
