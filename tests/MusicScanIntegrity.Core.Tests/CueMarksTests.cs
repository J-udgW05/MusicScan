using MusicScanIntegrity.Core.Models;
using MusicScanIntegrity.Core.Playlists;
using Xunit;

namespace MusicScanIntegrity.Core.Tests;

public sealed class CueMarksTests
{
    private const string Cue = """
        FILE "альбом.flac" WAVE
          TRACK 01 AUDIO
            TITLE "Первая"
            INDEX 01 00:00:00
          TRACK 02 AUDIO
            TITLE "Вторая"
            INDEX 01 03:20:00
          TRACK 03 AUDIO
            TITLE "Третья"
            INDEX 01 07:45:37
        """;

    [Fact]
    public void Метки_разбираются_вместе_с_номерами_дорожек()
    {
        IReadOnlyList<CueMark> marks = CuePlaylistParser.ParseMarks(Cue);

        Assert.Equal(3, marks.Count);
        Assert.Equal(1, marks[0].Track);
        Assert.Equal(0, marks[0].Seconds);
        Assert.Equal(200, marks[1].Seconds);

        // Третья метка: 7 минут 45 секунд и 37 кадров по одной семьдесятпятой.
        Assert.Equal(465 + (37 / 75.0), marks[2].Seconds, 3);
    }

    [Fact]
    public void Мусор_вместо_времени_пропускается()
    {
        IReadOnlyList<CueMark> marks = CuePlaylistParser.ParseMarks("""
            TRACK 01 AUDIO
              INDEX 01 неизвестно
              INDEX 01 01:00:00
            """);

        Assert.Single(marks);
        Assert.Equal(60, marks[0].Seconds);
    }

    [Fact]
    public async Task Метка_за_пределом_файла_замечается()
    {
        using TempDirectory temp = new();
        string audio = temp.WriteBytes("альбом.flac", new byte[1024]);
        string cue = temp.WriteText("альбом.cue", Cue);

        PlaylistService service = new();

        // Файл длиной пять минут, а последняя дорожка начинается на восьмой.
        PlaylistCheckResult result = await service.CheckAsync(
            cue,
            knownStatuses: null,
            knownDurations: new Dictionary<string, double> { [audio] = 300 });

        Assert.NotNull(result.ContentIssue);
        Assert.Equal(IssueCode.CueMarksBeyondFile, result.ContentIssue!.Code);
        Assert.Equal(CheckStatus.Warning, result.Status);
    }

    [Fact]
    public async Task Разметка_по_размеру_файла_замечаний_не_вызывает()
    {
        using TempDirectory temp = new();
        string audio = temp.WriteBytes("альбом.flac", new byte[1024]);
        string cue = temp.WriteText("альбом.cue", Cue);

        PlaylistService service = new();

        PlaylistCheckResult result = await service.CheckAsync(
            cue,
            knownStatuses: null,
            knownDurations: new Dictionary<string, double> { [audio] = 720 });

        Assert.Null(result.ContentIssue);
        Assert.Equal(CheckStatus.Ok, result.Status);
    }

    [Fact]
    public async Task Без_известной_длительности_разметка_не_проверяется()
    {
        using TempDirectory temp = new();
        temp.WriteBytes("альбом.flac", new byte[1024]);
        string cue = temp.WriteText("альбом.cue", Cue);

        PlaylistService service = new();

        PlaylistCheckResult result = await service.CheckAsync(cue);

        Assert.Null(result.ContentIssue);
    }
}
