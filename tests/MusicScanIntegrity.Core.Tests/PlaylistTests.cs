using System.Text;
using MusicScanIntegrity.Core.Models;
using MusicScanIntegrity.Core.Playlists;
using Xunit;

namespace MusicScanIntegrity.Core.Tests;

public sealed class M3uPlaylistParserTests
{
    [Fact]
    public void Skips_directives_and_returns_only_paths()
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
    public void Handles_windows_line_endings_and_bom()
    {
        M3uPlaylistParser parser = new();

        IReadOnlyList<string> paths = parser.Parse("\uFEFF#EXTM3U\r\ntrack.mp3\r\n");

        Assert.Equal(["track.mp3"], paths);
    }
}

public sealed class PlsPlaylistParserTests
{
    [Fact]
    public void Takes_only_file_keys_ordered_by_number()
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
    public void Ignores_comments_and_empty_values()
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
    public void Takes_path_from_quoted_file_directive()
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
    public void Handles_unquoted_file_directive()
    {
        CuePlaylistParser parser = new();

        Assert.Equal(["album.wav"], parser.Parse("FILE album.wav WAVE"));
    }

    [Fact]
    public void Tracks_are_not_separate_paths()
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
    public void Relative_path_resolves_from_playlist_folder()
    {
        string resolved = PlaylistService.ResolvePath(@"sub\track.mp3", @"D:\Music\Lists")!;

        Assert.Equal(@"D:\Music\Lists\sub\track.mp3", resolved);
    }

    [Fact]
    public void Absolute_path_is_kept()
    {
        string resolved = PlaylistService.ResolvePath(@"D:\Other\track.mp3", @"D:\Music\Lists")!;

        Assert.Equal(@"D:\Other\track.mp3", resolved);
    }

    [Fact]
    public void Unix_slashes_are_normalised()
    {
        string resolved = PlaylistService.ResolvePath("sub/track.mp3", @"D:\Music")!;

        Assert.Equal(@"D:\Music\sub\track.mp3", resolved);
    }

    [Fact]
    public void Stream_url_is_not_a_path()
    {
        Assert.Null(PlaylistService.ResolvePath("https://radio.example/stream", @"D:\Music"));
    }

    [Fact]
    public async Task Missing_file_is_marked_not_found()
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
    public async Task Main_scan_status_is_reused_not_duplicated()
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
    /// Older .m3u and .pls files without a BOM use the system ANSI code page. The
    /// file name is chosen for the code page of the test machine: hard-coded
    /// Cyrillic only works on Russian Windows and would fail on an English build
    /// agent (code page 1252) for no reason.
    /// </summary>
    [Fact]
    public async Task Non_unicode_playlist_reads_in_system_code_page()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding ansi = Encoding.GetEncoding(0);

        string? name = new[] { "Песня.mp3", "Chanson-eté.mp3", "Gruße.mp3" }
            .FirstOrDefault(candidate => RoundTrips(candidate, ansi));

        if (name is null)
        {
            // The system code page cannot hold any of the non-ASCII candidates; nothing
            // to test, and nothing to fail.
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

    /// <summary>The name survives a round trip through the code page losslessly.</summary>
    private static bool RoundTrips(string value, Encoding encoding) =>
        encoding.GetString(encoding.GetBytes(value)) == value;

    [Fact]
    public async Task Invalid_unicode_bytes_do_not_break_reading()
    {
        using TempDirectory temp = new();

        // 0x80 is a continuation byte with no lead byte — impossible in UTF-8 and not
        // a BOM. Reading must not fail but fall back to the single-byte encoding.
        string playlist = temp.WriteBytes("list.m3u", 0x80, 0x41, 0x2E, 0x6D, 0x70, 0x33, 0x0A);

        PlaylistService service = new();
        PlaylistCheckResult result = await service.CheckAsync(playlist);

        Assert.Null(result.ParseIssue);
    }

    [Fact]
    public async Task Unsupported_extension_gives_clear_error_not_exception()
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
    /// The DI container passes an empty collection instead of the default value,
    /// which used to leave the service without parsers, silently treating every
    /// playlist as unsupported.
    /// </summary>
    [Fact]
    public async Task Empty_parser_set_does_not_leave_service_without_parsers()
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
    public void Empty_exporter_set_does_not_leave_service_without_formats()
    {
        Reporting.ReportService service = new([]);

        Assert.Equal(3, service.Exporters.Count);
        Assert.EndsWith(".html", service.SuggestFileName(Settings.ReportFormat.Html, DateTimeOffset.Now));
        Assert.EndsWith(".csv", service.SuggestFileName(Settings.ReportFormat.Csv, DateTimeOffset.Now));
        Assert.EndsWith(".txt", service.SuggestFileName(Settings.ReportFormat.Text, DateTimeOffset.Now));
    }
}
