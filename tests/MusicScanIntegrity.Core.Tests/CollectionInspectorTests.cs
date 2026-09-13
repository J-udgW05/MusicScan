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
    public void Пропуск_в_нумерации_замечается()
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

    /// <summary>
    /// Номер дорожки вне разумных пределов — сломанный тег, а не альбом на
    /// миллион дорожек.
    /// </summary>
    /// <remarks>
    /// Считать по такому номеру пропуски значило бы выписать человеку список
    /// из миллиона недостающих дорожек, а до этого сложить миллион чисел в
    /// память. На настоящей коллекции с испорченными тегами так и вылетала бы
    /// вся программа: разбор идёт уже после проверки, когда результаты собраны.
    /// </remarks>
    [Fact]
    public void Невозможный_номер_дорожки_не_считается_краем_альбома()
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

    /// <summary>
    /// Граница разумного: сотни дорожек в сборнике — ещё альбом.
    /// </summary>
    [Fact]
    public void Большой_но_возможный_номер_дорожки_разбирается_как_обычно()
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
    public void Папка_без_номеров_пропуском_не_считается()
    {
        // Номеров нет вовсе — судить не о чем.
        IReadOnlyList<CollectionFinding> findings = CollectionInspector.Inspect(
        [
            File(@"C:\Музыка\Сборник\a.flac"),
            File(@"C:\Музыка\Сборник\b.flac"),
            File(@"C:\Музыка\Сборник\c.flac"),
        ]);

        Assert.DoesNotContain(findings, f => f.Kind == CollectionFindingKind.MissingTracks);
    }

    [Fact]
    public void Разные_форматы_в_одной_папке_замечаются()
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
    public void Разные_альбомы_в_папке_замечаются()
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
    public void Повтор_трека_находится_по_тегам_и_длительности()
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

    /// <summary>
    /// Число мест склоняется: «в 1 месте», но «в 3 местах».
    /// </summary>
    [Theory]
    [InlineData(2, "ещё в 1 месте")]
    [InlineData(3, "ещё в 2 местах")]
    [InlineData(6, "ещё в 5 местах")]
    [InlineData(22, "ещё в 21 месте")]
    public void Число_мест_с_повтором_склоняется(int copies, string expected)
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
    public void Разная_длительность_повтором_не_считается()
    {
        // Концертная версия называется так же, а трек другой.
        IReadOnlyList<CollectionFinding> findings = CollectionInspector.Inspect(
        [
            File(@"C:\Музыка\Студия\01.flac", title: "Песня", duration: 214),
            File(@"C:\Музыка\Концерт\01.flac", title: "Песня", duration: 305),
        ]);

        Assert.DoesNotContain(findings, f => f.Kind == CollectionFindingKind.Duplicate);
    }

    [Fact]
    public void Отсутствие_обложки_замечается()
    {
        // Папка настоящая: проверка ищет в ней ещё и файл обложки рядом с музыкой.
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
    public void Обложка_отдельным_файлом_замечаний_не_вызывает()
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
    public void Маленькая_папка_альбомом_не_считается()
    {
        IReadOnlyList<CollectionFinding> findings = CollectionInspector.Inspect(
        [
            File(@"C:\Музыка\Пара\01.flac", format: "FLAC", cover: false),
            File(@"C:\Музыка\Пара\02.mp3", format: "MP3", cover: false),
        ]);

        Assert.Empty(findings);
    }

    [Fact]
    public void Повторы_можно_не_искать()
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
