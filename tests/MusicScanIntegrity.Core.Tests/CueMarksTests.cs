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
    public void Marks_are_parsed_with_track_numbers()
    {
        IReadOnlyList<CueMark> marks = CuePlaylistParser.ParseMarks(Cue);

        Assert.Equal(3, marks.Count);
        Assert.Equal(1, marks[0].Track);
        Assert.Equal(0, marks[0].Seconds);
        Assert.Equal(200, marks[1].Seconds);

        // Third mark: 7 minutes 45 seconds and 37 frames of 1/75 s.
        Assert.Equal(465 + (37 / 75.0), marks[2].Seconds, 3);
    }

    [Fact]
    public void Garbage_timestamps_are_skipped()
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
    public async Task Mark_past_end_of_file_is_reported()
    {
        using TempDirectory temp = new();
        string audio = temp.WriteBytes("альбом.flac", new byte[1024]);
        string cue = temp.WriteText("альбом.cue", Cue);

        PlaylistService service = new();

        // The file is five minutes long, but the last track starts at eight.
        PlaylistCheckResult result = await service.CheckAsync(
            cue,
            knownStatuses: null,
            knownDurations: new Dictionary<string, double> { [audio] = 300 });

        Assert.NotNull(result.ContentIssue);
        Assert.Equal(IssueCode.CueMarksBeyondFile, result.ContentIssue!.Code);
        Assert.Equal(CheckStatus.Warning, result.Status);
    }

    [Fact]
    public async Task Marks_within_file_raise_no_finding()
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
    public async Task Marks_are_not_checked_without_known_duration()
    {
        using TempDirectory temp = new();
        temp.WriteBytes("альбом.flac", new byte[1024]);
        string cue = temp.WriteText("альбом.cue", Cue);

        PlaylistService service = new();

        PlaylistCheckResult result = await service.CheckAsync(cue);

        Assert.Null(result.ContentIssue);
    }
}
