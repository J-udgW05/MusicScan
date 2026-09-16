using MusicScanIntegrity.Core.Analysis;
using MusicScanIntegrity.Core.Models;
using Xunit;

namespace MusicScanIntegrity.Core.Tests;

public sealed class CollectionInspectorTests
{
    private static FileCheckResult File(
        string path,
        string format = "FLAC",
        int track = 0,
        string? album = "Альбом",
        string? title = null,
        string? artist = "Исполнитель",
        double duration = 200,
        bool cover = true) =>
        new()
        {
            FullPath = path,
            FileName = Path.GetFileName(path),
            DirectoryPath = Path.GetDirectoryName(path) ?? string.Empty,
            SizeBytes = 1024,
            Status = CheckStatus.Ok,
            Issues = [],
            Format = format,
            DurationSeconds = duration,
            Metadata = new TrackMetadata(
                title ?? Path.GetFileNameWithoutExtension(path),
                artist,
                album,
                duration,
                track,
                cover),
        };

    [Fact]
    public void Numbering_gap_is_reported()
    {
        IReadOnlyList<CollectionFinding> findings = CollectionInspector.Inspect(
        [
            File(@"C:\Музыка\Альбом\01.flac", track: 1),
            File(@"C:\Музыка\Альбом\02.flac", track: 2),
            File(@"C:\Музыка\Альбом\04.flac", track: 4),
            File(@"C:\Музыка\Альбом\05.flac", track: 5),
        ]);

        CollectionFinding finding = Assert.Single(findings, f => f.Kind == CollectionFindingKind.MissingTracks);
        Assert.Contains("3", finding.Message, StringComparison.Ordinal);
    }

    /// <summary>An out-of-range track number is a broken tag, not an album of a million tracks.</summary>
    /// <remarks>
    /// Honouring it would list a million missing tracks after allocating a million
    /// integers. On a real collection with damaged tags this crashed the app, since
    /// the inspection runs after the scan with all results in memory.
    /// </remarks>
    [Fact]
    public void Impossible_track_number_is_not_album_end()
    {
        IReadOnlyList<CollectionFinding> findings = CollectionInspector.Inspect(
            [
                File(@"C:\Музыка\Альбом\1.flac", track: 1),
                File(@"C:\Музыка\Альбом\2.flac", track: 2),
                File(@"C:\Музыка\Альбом\3.flac", track: 1_000_000),
            ],
            findDuplicates: false);

        Assert.DoesNotContain(findings, f => f.Kind == CollectionFindingKind.MissingTracks);
    }

    /// <summary>Within bounds: hundreds of tracks on a compilation is still an album.</summary>
    [Fact]
    public void Large_but_possible_track_number_is_handled_normally()
    {
        IReadOnlyList<CollectionFinding> findings = CollectionInspector.Inspect(
            [
                File(@"C:\Музыка\Сборник\1.flac", track: 1),
                File(@"C:\Музыка\Сборник\2.flac", track: 2),
                File(@"C:\Музыка\Сборник\3.flac", track: 5),
            ],
            findDuplicates: false);

        CollectionFinding missing = Assert.Single(
            findings,
            f => f.Kind == CollectionFindingKind.MissingTracks);

        Assert.Contains("3, 4", missing.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unnumbered_folder_is_not_a_gap()
    {
        // No numbers at all; nothing to judge.
        IReadOnlyList<CollectionFinding> findings = CollectionInspector.Inspect(
        [
            File(@"C:\Музыка\Сборник\a.flac"),
            File(@"C:\Музыка\Сборник\b.flac"),
            File(@"C:\Музыка\Сборник\c.flac"),
        ]);

        Assert.DoesNotContain(findings, f => f.Kind == CollectionFindingKind.MissingTracks);
    }

    [Fact]
    public void Mixed_formats_in_folder_are_reported()
    {
        IReadOnlyList<CollectionFinding> findings = CollectionInspector.Inspect(
        [
            File(@"C:\Музыка\Альбом\01.flac", format: "FLAC", track: 1),
            File(@"C:\Музыка\Альбом\02.mp3", format: "MP3", track: 2),
            File(@"C:\Музыка\Альбом\03.flac", format: "FLAC", track: 3),
        ]);

        CollectionFinding finding = Assert.Single(findings, f => f.Kind == CollectionFindingKind.MixedFormats);
        Assert.Contains("FLAC", finding.Message, StringComparison.Ordinal);
        Assert.Contains("MP3", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mixed_albums_in_folder_are_reported()
    {
        IReadOnlyList<CollectionFinding> findings = CollectionInspector.Inspect(
        [
            File(@"C:\Музыка\Куча\01.flac", album: "Первый", track: 1),
            File(@"C:\Музыка\Куча\02.flac", album: "Второй", track: 2),
            File(@"C:\Музыка\Куча\03.flac", album: "Первый", track: 3),
        ]);

        Assert.Single(findings, f => f.Kind == CollectionFindingKind.MixedAlbums);
    }

    [Fact]
    public void Duplicate_is_found_by_tags_and_duration()
    {
        IReadOnlyList<CollectionFinding> findings = CollectionInspector.Inspect(
        [
            File(@"C:\Музыка\Альбом\01.flac", title: "Песня", duration: 214, track: 1),
            File(@"C:\Музыка\Альбом\02.flac", title: "Другая", duration: 180, track: 2),
            File(@"C:\Музыка\Альбом\03.flac", title: "Третья", duration: 150, track: 3),
            File(@"D:\Копии\песня.flac", title: "Песня", duration: 214),
        ]);

        CollectionFinding finding = Assert.Single(findings, f => f.Kind == CollectionFindingKind.Duplicate);
        Assert.Contains(@"D:\Копии\песня.flac", finding.Detail!, StringComparison.Ordinal);
    }

    /// <summary>The place count agrees in number: "в 1 месте" but "в 3 местах".</summary>
    [Theory]
    [InlineData(2, "ещё в 1 месте")]
    [InlineData(3, "ещё в 2 местах")]
    [InlineData(6, "ещё в 5 местах")]
    [InlineData(22, "ещё в 21 месте")]
    public void Duplicate_place_count_agrees_in_number(int copies, string expected)
    {
        List<FileCheckResult> files = [];
        for (int i = 0; i < copies; i++)
        {
            files.Add(File($@"C:\Музыка\{i}\трек.flac", title: "Песня"));
        }

        CollectionFinding duplicate = Assert.Single(
            CollectionInspector.Inspect(files),
            f => f.Kind == CollectionFindingKind.Duplicate);

        Assert.Contains(expected, duplicate.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Different_duration_is_not_a_duplicate()
    {
        // A live version shares the title but is a different recording.
        IReadOnlyList<CollectionFinding> findings = CollectionInspector.Inspect(
        [
            File(@"C:\Музыка\Студия\01.flac", title: "Песня", duration: 214),
            File(@"C:\Музыка\Концерт\01.flac", title: "Песня", duration: 305),
        ]);

        Assert.DoesNotContain(findings, f => f.Kind == CollectionFindingKind.Duplicate);
    }

    [Fact]
    public void Missing_cover_is_reported()
    {
        // A real folder: the check also looks for a cover image next to the music.
        using TempDirectory temp = new();

        IReadOnlyList<CollectionFinding> findings = CollectionInspector.Inspect(
        [
            File(Path.Combine(temp.Path, "01.flac"), track: 1, cover: false),
            File(Path.Combine(temp.Path, "02.flac"), track: 2, cover: false),
            File(Path.Combine(temp.Path, "03.flac"), track: 3, cover: false),
        ]);

        Assert.Single(findings, f => f.Kind == CollectionFindingKind.NoCover);
    }

    [Fact]
    public void Cover_image_file_raises_no_finding()
    {
        using TempDirectory temp = new();
        temp.WriteBytes("cover.jpg", new byte[16]);

        IReadOnlyList<CollectionFinding> findings = CollectionInspector.Inspect(
        [
            File(Path.Combine(temp.Path, "01.flac"), track: 1, cover: false),
            File(Path.Combine(temp.Path, "02.flac"), track: 2, cover: false),
            File(Path.Combine(temp.Path, "03.flac"), track: 3, cover: false),
        ]);

        Assert.DoesNotContain(findings, f => f.Kind == CollectionFindingKind.NoCover);
    }

    [Fact]
    public void Small_folder_is_not_an_album()
    {
        IReadOnlyList<CollectionFinding> findings = CollectionInspector.Inspect(
        [
            File(@"C:\Музыка\Пара\01.flac", format: "FLAC", cover: false),
            File(@"C:\Музыка\Пара\02.mp3", format: "MP3", cover: false),
        ]);

        Assert.Empty(findings);
    }

    [Fact]
    public void Duplicate_search_can_be_skipped()
    {
        IReadOnlyList<CollectionFinding> findings = CollectionInspector.Inspect(
            [
                File(@"C:\Музыка\Альбом\01.flac", title: "Песня", duration: 214),
                File(@"D:\Копии\песня.flac", title: "Песня", duration: 214),
            ],
            findDuplicates: false);

        Assert.Empty(findings);
    }
}
