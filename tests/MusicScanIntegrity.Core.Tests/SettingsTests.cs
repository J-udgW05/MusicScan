using System.Text.Json;
using MusicScanIntegrity.Core.Settings;
using Xunit;

namespace MusicScanIntegrity.Core.Tests;

public sealed class AppSettingsDefaultsTests
{
    [Fact]
    public void Defaults_match_documented_values()
    {
        AppSettings settings = new();

        Assert.True(settings.Recursive);
        Assert.False(settings.CheckMetadata);
        Assert.Equal(500, settings.LargeFileThresholdMb);
        Assert.Equal(60, settings.FileTimeoutSeconds);
        Assert.True(settings.AutoParallelism);
        Assert.Equal(LockedFileAction.Ask, settings.LockedFileAction);
        Assert.True(settings.WarnAboutLargeFiles);
        Assert.True(settings.OfferStartAfterFolderSelected);
        Assert.False(settings.EnableIsoSacd);
        Assert.Equal(AppTheme.System, settings.Theme);
    }

    [Fact]
    public void Auto_parallelism_is_capped_at_eight()
    {
        AppSettings settings = new() { AutoParallelism = true };

        Assert.InRange(settings.EffectiveParallelism, 1, 8);
        Assert.Equal(Math.Clamp(Environment.ProcessorCount, 1, 8), settings.EffectiveParallelism);
    }

    [Fact]
    public void Manual_parallelism_comes_from_setting()
    {
        AppSettings settings = new() { AutoParallelism = false, ManualParallelism = 16 };

        Assert.Equal(16, settings.EffectiveParallelism);
    }

    [Fact]
    public void Zero_threshold_means_no_limit()
    {
        Assert.Null(new AppSettings { LargeFileThresholdMb = 0 }.LargeFileThresholdBytes);
        Assert.Equal(500L * 1024 * 1024, new AppSettings { LargeFileThresholdMb = 500 }.LargeFileThresholdBytes);
    }

    [Fact]
    public void Clone_does_not_share_lists()
    {
        AppSettings original = new();
        original.CustomExtensions.Add(".mpc");

        AppSettings copy = original.Clone();
        copy.CustomExtensions.Add(".tta");
        copy.StatusColors.Ok = "#123456";

        Assert.Single(original.CustomExtensions);
        Assert.Null(original.StatusColors.Ok);
    }
}

public sealed class JsonSettingsServiceTests
{
    [Fact]
    public async Task First_run_creates_file_with_defaults()
    {
        using TempDirectory temp = new();
        string path = Path.Combine(temp.Path, "settings.json");

        JsonSettingsService service = new(path);
        bool firstRun = await service.LoadAsync();

        Assert.True(firstRun);
        Assert.True(File.Exists(path));
        Assert.Equal(500, service.Current.LargeFileThresholdMb);
    }

    [Fact]
    public async Task Saved_settings_survive_restart()
    {
        using TempDirectory temp = new();
        string path = Path.Combine(temp.Path, "settings.json");

        JsonSettingsService first = new(path);
        await first.LoadAsync();

        AppSettings changed = first.Current.Clone();
        changed.CheckMetadata = true;
        changed.LargeFileThresholdMb = 2048;
        changed.Theme = AppTheme.Dark;
        changed.LastFolder = @"D:\Music\Коллекция";
        await first.ApplyAsync(changed);

        JsonSettingsService second = new(path);
        bool firstRun = await second.LoadAsync();

        Assert.False(firstRun);
        Assert.True(second.Current.CheckMetadata);
        Assert.Equal(2048, second.Current.LargeFileThresholdMb);
        Assert.Equal(AppTheme.Dark, second.Current.Theme);
        Assert.Equal(@"D:\Music\Коллекция", second.Current.LastFolder);
    }

    [Fact]
    public async Task Damaged_file_does_not_block_startup()
    {
        using TempDirectory temp = new();
        string path = Path.Combine(temp.Path, "settings.json");
        await File.WriteAllTextAsync(path, "{ это не json ");

        JsonSettingsService service = new(path);
        bool firstRun = await service.LoadAsync();

        Assert.True(firstRun);
        Assert.Equal(500, service.Current.LargeFileThresholdMb);
        Assert.True(File.Exists(path + ".corrupted"));

        // The file was rewritten with valid JSON.
        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.Equal(500, document.RootElement.GetProperty("LargeFileThresholdMb").GetInt32());
    }

    [Fact]
    public async Task Reset_restores_defaults()
    {
        using TempDirectory temp = new();
        string path = Path.Combine(temp.Path, "settings.json");

        JsonSettingsService service = new(path);
        await service.LoadAsync();

        AppSettings changed = service.Current.Clone();
        changed.CheckMetadata = true;
        changed.FileTimeoutSeconds = 5;
        await service.ApplyAsync(changed);

        await service.ResetAsync();

        Assert.False(service.Current.CheckMetadata);
        Assert.Equal(60, service.Current.FileTimeoutSeconds);
    }

    [Theory]
    [InlineData(-100, 0)]
    [InlineData(0, 0)]
    [InlineData(500, 500)]
    public void Negative_threshold_is_clamped_to_zero(int input, int expected)
    {
        AppSettings settings = JsonSettingsService.Sanitize(new AppSettings { LargeFileThresholdMb = input });

        Assert.Equal(expected, settings.LargeFileThresholdMb);
    }

    [Fact]
    public void Hand_edited_values_are_clamped_to_sane_ranges()
    {
        AppSettings settings = JsonSettingsService.Sanitize(new AppSettings
        {
            FileTimeoutSeconds = 999_999,
            ManualParallelism = 0,
            LockedRetryCount = -3,
        });

        Assert.Equal(3600, settings.FileTimeoutSeconds);
        Assert.Equal(1, settings.ManualParallelism);
        Assert.Equal(1, settings.LockedRetryCount);
    }

    [Theory]
    [InlineData("flac", ".flac")]
    [InlineData(".FLAC", ".flac")]
    [InlineData("*.mpc", ".mpc")]
    [InlineData("  .tta  ", ".tta")]
    public void Extensions_are_normalised(string input, string expected)
    {
        Assert.Equal(expected, JsonSettingsService.NormalizeExtension(input));
    }
}
