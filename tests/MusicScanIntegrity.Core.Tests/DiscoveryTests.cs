using MusicScanIntegrity.Core.Discovery;
using MusicScanIntegrity.Core.Models;
using MusicScanIntegrity.Core.Settings;
using Xunit;

namespace MusicScanIntegrity.Core.Tests;

public sealed class AudioFormatsTests
{
    [Theory]
    [InlineData(".flac")]
    [InlineData(".mp3")]
    [InlineData(".wav")]
    [InlineData(".aiff")]
    [InlineData(".m4a")]
    [InlineData(".ape")]
    [InlineData(".wv")]
    [InlineData(".aac")]
    [InlineData(".ogg")]
    [InlineData(".opus")]
    [InlineData(".wma")]
    [InlineData(".mid")]
    [InlineData(".mod")]
    [InlineData(".xm")]
    [InlineData(".it")]
    [InlineData(".s3m")]
    [InlineData(".dsf")]
    [InlineData(".dff")]
    public void Все_форматы_из_спецификации_поддерживаются(string extension)
    {
        Assert.Contains(extension, AudioFormats.AllAudio);
    }

    [Fact]
    public void Iso_по_умолчанию_не_считается_аудио()
    {
        FormatSelection formats = AudioFormats.ForSettings(new AppSettings());

        Assert.Null(formats.Classify(@"D:\Music\album.iso"));
        Assert.DoesNotContain(".iso", formats.Audio);
    }

    [Fact]
    public void Iso_включается_только_экспериментальной_настройкой()
    {
        FormatSelection formats = AudioFormats.ForSettings(new AppSettings { EnableIsoSacd = true });

        Assert.Equal(ScanItemKind.DiscImage, formats.Classify(@"D:\Music\album.iso"));
    }

    [Fact]
    public void Выключенное_расширение_перестаёт_попадать_в_обход()
    {
        AppSettings settings = new();
        settings.DisabledExtensions.Add(".wma");

        FormatSelection formats = AudioFormats.ForSettings(settings);

        Assert.Null(formats.Classify(@"D:\Music\track.wma"));
        Assert.Equal(ScanItemKind.Audio, formats.Classify(@"D:\Music\track.mp3"));
    }

    [Fact]
    public void Пользовательское_расширение_добавляется_к_обходу()
    {
        AppSettings settings = new();
        settings.CustomExtensions.Add(".mpc");

        FormatSelection formats = AudioFormats.ForSettings(settings);

        Assert.Equal(ScanItemKind.Audio, formats.Classify(@"D:\Music\track.mpc"));
    }

    [Fact]
    public void Плейлисты_не_попадают_в_аудио()
    {
        FormatSelection formats = AudioFormats.ForSettings(new AppSettings());

        Assert.Equal(ScanItemKind.Playlist, formats.Classify(@"D:\Music\list.m3u8"));
        Assert.Equal(ScanItemKind.Playlist, formats.Classify(@"D:\Music\album.cue"));
    }

    [Theory]
    [InlineData(".flac", "FLAC")]
    [InlineData(".mid", "MIDI")]
    [InlineData(".midi", "MIDI")]
    [InlineData(".aif", "AIFF")]
    [InlineData(".oga", "OGG")]
    public void Название_формата_читается_по_человечески(string extension, string expected)
    {
        Assert.Equal(expected, AudioFormats.DisplayName(extension));
    }
}

public sealed class FileDiscoveryServiceTests
{
    private static FileDiscoveryService CreateService() => new();

    [Fact]
    public async Task Находит_аудио_и_плейлисты_и_игнорирует_прочее()
    {
        using TempDirectory temp = new();
        temp.WriteBytes("a.flac", 1);
        temp.WriteBytes("b.mp3", 1);
        temp.WriteText("list.m3u", "a.flac");
        temp.WriteText("cover.jpg", "not audio");
        temp.WriteText("notes.txt", "not audio");

        DiscoveryResult result = await CreateService().DiscoverAsync(temp.Path, new AppSettings());

        Assert.Equal(2, result.AudioItems.Count);
        Assert.Single(result.Playlists);
        Assert.Equal(3, result.TotalCount);
    }

    [Fact]
    public async Task Рекурсия_включена_по_умолчанию()
    {
        using TempDirectory temp = new();
        temp.WriteBytes("root.flac", 1);
        temp.WriteBytes(Path.Combine("sub", "deep", "nested.flac"), 1);

        DiscoveryResult result = await CreateService().DiscoverAsync(temp.Path, new AppSettings());

        Assert.Equal(2, result.AudioItems.Count);
    }

    [Fact]
    public async Task Без_рекурсии_подпапки_не_обходятся()
    {
        using TempDirectory temp = new();
        temp.WriteBytes("root.flac", 1);
        temp.WriteBytes(Path.Combine("sub", "nested.flac"), 1);

        DiscoveryResult result = await CreateService()
            .DiscoverAsync(temp.Path, new AppSettings { Recursive = false });

        Assert.Single(result.AudioItems);
        Assert.EndsWith("root.flac", result.AudioItems[0].FullPath);
    }

    [Fact]
    public async Task Несуществующая_папка_отмечается_честно_а_не_падает()
    {
        string missing = Path.Combine(Path.GetTempPath(), "нет-такой-папки-" + Guid.NewGuid().ToString("N"));

        DiscoveryResult result = await CreateService().DiscoverAsync(missing, new AppSettings());

        Assert.Empty(result.AudioItems);
        Assert.Single(result.InaccessibleFolders);
    }

    [Fact]
    public async Task Плейлисты_не_обходятся_если_их_проверка_выключена()
    {
        using TempDirectory temp = new();
        temp.WriteBytes("a.flac", 1);
        temp.WriteText("list.m3u", "a.flac");

        DiscoveryResult result = await CreateService()
            .DiscoverAsync(temp.Path, new AppSettings { CheckPlaylists = false });

        Assert.Empty(result.Playlists);
        Assert.Single(result.AudioItems);
    }

    [Fact]
    public async Task Размер_файла_попадает_в_результат_обхода()
    {
        using TempDirectory temp = new();
        temp.WriteBytes("a.flac", 1, 2, 3, 4, 5);

        DiscoveryResult result = await CreateService().DiscoverAsync(temp.Path, new AppSettings());

        Assert.Equal(5, result.AudioItems[0].SizeBytes);
    }
}
