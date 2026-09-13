using System.Text;
using MusicScanIntegrity.Core.Models;
using MusicScanIntegrity.Core.Playlists;
using Xunit;

namespace MusicScanIntegrity.Core.Tests;

public sealed class M3uPlaylistParserTests
{
    [Fact]
    public void Пропускает_служебные_строки_и_возвращает_только_пути()
    {
        M3uPlaylistParser parser = new();

        IReadOnlyList<string> paths = parser.Parse("""
            #EXTM3U
            #EXTINF:214,Nick Drake - Pink Moon
            Nick Drake\Pink Moon\01 - Pink Moon.flac

            #EXTINF:180,Second
            D:\Music\second.mp3
            """);

        Assert.Equal(
            [@"Nick Drake\Pink Moon\01 - Pink Moon.flac", @"D:\Music\second.mp3"],
            paths);
    }

    [Fact]
    public void Понимает_переносы_строк_Windows_и_BOM()
    {
        M3uPlaylistParser parser = new();

        IReadOnlyList<string> paths = parser.Parse("\uFEFF#EXTM3U\r\ntrack.mp3\r\n");

        Assert.Equal(["track.mp3"], paths);
    }
}

public sealed class PlsPlaylistParserTests
{
    [Fact]
    public void Берёт_только_ключи_File_и_упорядочивает_их_по_номеру()
    {
        PlsPlaylistParser parser = new();

        IReadOnlyList<string> paths = parser.Parse("""
            [playlist]
            NumberOfEntries=2
            File2=second.mp3
            Title2=Second
            File1=first.flac
            Title1=First
            Length1=214
            Version=2
            """);

        Assert.Equal(["first.flac", "second.mp3"], paths);
    }

    [Fact]
    public void Игнорирует_комментарии_и_пустые_значения()
    {
        PlsPlaylistParser parser = new();

        IReadOnlyList<string> paths = parser.Parse("""
            ; комментарий
            [playlist]
            File1=
            File2=track.ogg
            """);

        Assert.Equal(["track.ogg"], paths);
    }
}

public sealed class CuePlaylistParserTests
{
    [Fact]
    public void Берёт_путь_из_директивы_FILE_в_кавычках()
    {
        CuePlaylistParser parser = new();

        IReadOnlyList<string> paths = parser.Parse("""
            REM GENRE Folk
            PERFORMER "Nick Drake"
            FILE "Pink Moon.flac" WAVE
              TRACK 01 AUDIO
                TITLE "Pink Moon"
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                INDEX 01 02:04:00
            """);

        Assert.Equal(["Pink Moon.flac"], paths);
    }

    [Fact]
    public void Понимает_FILE_без_кавычек()
    {
        CuePlaylistParser parser = new();

        Assert.Equal(["album.wav"], parser.Parse("FILE album.wav WAVE"));
    }

    [Fact]
    public void Дорожки_не_считаются_отдельными_путями()
    {
        CuePlaylistParser parser = new();

        IReadOnlyList<string> paths = parser.Parse("""
            FILE "disc1.flac" WAVE
              TRACK 01 AUDIO
              TRACK 02 AUDIO
            FILE "disc2.flac" WAVE
              TRACK 03 AUDIO
            """);

        Assert.Equal(["disc1.flac", "disc2.flac"], paths);
    }
}

public sealed class PlaylistServiceTests
{
    [Fact]
    public void Относительный_путь_разворачивается_от_папки_плейлиста()
    {
        string resolved = PlaylistService.ResolvePath(@"sub\track.mp3", @"D:\Music\Lists")!;

        Assert.Equal(@"D:\Music\Lists\sub\track.mp3", resolved);
    }

    [Fact]
    public void Абсолютный_путь_остаётся_как_есть()
    {
        string resolved = PlaylistService.ResolvePath(@"D:\Other\track.mp3", @"D:\Music\Lists")!;

        Assert.Equal(@"D:\Other\track.mp3", resolved);
    }

    [Fact]
    public void Косые_черты_в_стиле_Unix_приводятся_к_Windows()
    {
        string resolved = PlaylistService.ResolvePath("sub/track.mp3", @"D:\Music")!;

        Assert.Equal(@"D:\Music\sub\track.mp3", resolved);
    }

    [Fact]
    public void Ссылка_на_сетевой_поток_путём_не_считается()
    {
        Assert.Null(PlaylistService.ResolvePath("https://radio.example/stream", @"D:\Music"));
    }

    [Fact]
    public async Task Отсутствующий_файл_отмечается_как_ненайденный()
    {
        using TempDirectory temp = new();
        temp.WriteBytes("present.mp3", 1, 2, 3);
        string playlist = temp.WriteText("list.m3u", "present.mp3\nmissing.mp3\n");

        PlaylistService service = new();
        PlaylistCheckResult result = await service.CheckAsync(playlist);

        Assert.Equal(2, result.Entries.Count);
        Assert.True(result.Entries[0].Exists);
        Assert.False(result.Entries[1].Exists);
        Assert.Equal(1, result.MissingCount);
        Assert.Equal(CheckStatus.Warning, result.Status);
    }

    [Fact]
    public async Task Статус_из_основного_сканирования_переиспользуется_а_не_дублируется()
    {
        using TempDirectory temp = new();
        string track = temp.WriteBytes("track.flac", 1, 2, 3);
        string playlist = temp.WriteText("list.m3u", "track.flac\n");

        Dictionary<string, CheckStatus> known = new(StringComparer.OrdinalIgnoreCase)
        {
            [track] = CheckStatus.Corrupted,
        };

        PlaylistService service = new();
        PlaylistCheckResult result = await service.CheckAsync(playlist, known);

        Assert.Equal(CheckStatus.Corrupted, result.Entries[0].KnownStatus);
        Assert.Equal(0, result.MissingCount);
    }

    /// <summary>
    /// Старые .m3u и .pls без BOM записаны в системной ANSI-кодировке.
    /// Имя файла подбирается под кодовую страницу той машины, где идёт тест:
    /// жёстко зашитая кириллица работает только на русской Windows, а на
    /// англоязычном сборщике (страница 1252) тест падал бы на пустом месте.
    /// </summary>
    [Fact]
    public async Task Не_юникодный_плейлист_читается_в_системной_кодировке()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding ansi = Encoding.GetEncoding(0);

        string? name = new[] { "Песня.mp3", "Chanson-eté.mp3", "Gruße.mp3" }
            .FirstOrDefault(candidate => RoundTrips(candidate, ansi));

        if (name is null)
        {
            // Системная кодировка не держит ни одного не-ASCII имени из набора —
            // проверять нечего, но и падать не за что.
            return;
        }

        using TempDirectory temp = new();
        temp.WriteBytes(name, 1, 2, 3);
        string playlist = temp.WriteText("list.m3u", name + "\n", ansi);

        PlaylistService service = new();
        PlaylistCheckResult result = await service.CheckAsync(playlist);

        Assert.Single(result.Entries);
        Assert.True(result.Entries[0].Exists, $"не нашёлся файл «{name}» в кодировке {ansi.WebName}");
    }

    /// <summary>Имя переживает круговой перевод через кодировку без потерь.</summary>
    private static bool RoundTrips(string value, Encoding encoding) =>
        encoding.GetString(encoding.GetBytes(value)) == value;

    [Fact]
    public async Task Байты_недопустимые_в_юникоде_не_роняют_чтение()
    {
        using TempDirectory temp = new();

        // 0x80 — байт продолжения без ведущего: в UTF-8 такого быть не может,
        // и это не метка порядка байт. Разбор обязан не упасть, а прочитать
        // строку запасной однобайтовой кодировкой.
        string playlist = temp.WriteBytes("list.m3u", 0x80, 0x41, 0x2E, 0x6D, 0x70, 0x33, 0x0A);

        PlaylistService service = new();
        PlaylistCheckResult result = await service.CheckAsync(playlist);

        Assert.Null(result.ParseIssue);
    }

    [Fact]
    public async Task Неподдерживаемое_расширение_даёт_понятную_ошибку_а_не_исключение()
    {
        using TempDirectory temp = new();
        string file = temp.WriteText("list.xspf", "<playlist/>");

        PlaylistService service = new();
        PlaylistCheckResult result = await service.CheckAsync(file);

        Assert.NotNull(result.ParseIssue);
        Assert.Equal(IssueCode.PlaylistUnreadable, result.ParseIssue.Code);
    }
}

public sealed class ServiceCompositionTests
{
    /// <summary>
    /// Контейнер внедрения зависимостей подставляет пустую коллекцию вместо
    /// значения по умолчанию. Раньше из-за этого служба оставалась без
    /// разборщиков и молча считала все плейлисты неподдерживаемыми.
    /// </summary>
    [Fact]
    public async Task Пустой_набор_разборщиков_не_оставляет_службу_без_разборщиков()
    {
        using TempDirectory temp = new();
        temp.WriteBytes("track.wav", 1, 2, 3);
        string playlist = temp.WriteText("list.m3u", "track.wav\nmissing.wav\n");

        PlaylistService service = new([]);
        PlaylistCheckResult result = await service.CheckAsync(playlist);

        Assert.Null(result.ParseIssue);
        Assert.Equal(2, result.Entries.Count);
        Assert.Equal(1, result.MissingCount);
    }

    [Fact]
    public void Пустой_набор_экспортёров_не_оставляет_службу_без_форматов()
    {
        Reporting.ReportService service = new([]);

        Assert.Equal(3, service.Exporters.Count);
        Assert.EndsWith(".html", service.SuggestFileName(Settings.ReportFormat.Html, DateTimeOffset.Now));
        Assert.EndsWith(".csv", service.SuggestFileName(Settings.ReportFormat.Csv, DateTimeOffset.Now));
        Assert.EndsWith(".txt", service.SuggestFileName(Settings.ReportFormat.Text, DateTimeOffset.Now));
    }
}
