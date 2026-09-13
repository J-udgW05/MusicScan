using MusicScanIntegrity.Core.Integrity;
using Xunit;

namespace MusicScanIntegrity.Core.Tests.Integrity;

/// <summary>
/// Теги вокруг звука не должны выглядеть повреждением.
/// </summary>
/// <remarks>
/// Тег — законная часть файла, но не часть потока. Разборщик, считающий
/// границы от нулевого байта, принял бы ID3 в начале за разрушенный заголовок,
/// а ID3v1 в конце — за мусор после последнего кадра. Оба вывода — ложная
/// тревога на исправном файле, и это худшее, что программа может сказать
/// про здоровую коллекцию.
/// </remarks>
public sealed class ContainerBoundsTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("bounds").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    /// <summary>Исправные файлы всех форматов, у которых бывают теги.</summary>
    public static TheoryData<string, byte[]> Healthy => new()
    {
        { ".flac", FlacFileBuilder.Build(frames: 4) },
        { ".ogg", SyntheticFiles.Ogg(pages: 3) },
        { ".mp3", SyntheticFiles.Mp3(frames: 4) },
        { ".m4a", SyntheticFiles.Mp4() },
        { ".wav", SyntheticFiles.Wav() },
        { ".wv", SyntheticFiles.WavPack() },
        { ".ape", SyntheticFiles.Ape() },
    };

    [Theory]
    [MemberData(nameof(Healthy))]
    public void Тег_в_начале_не_делает_файл_повреждённым(string extension, byte[] body)
    {
        ContainerValidation result = Check(extension, [.. SyntheticFiles.LeadingId3(300), .. body]);

        Assert.NotEqual(ContainerVerdict.Damaged, result.Verdict);
        Assert.NotEqual(ContainerVerdict.NotSupported, result.Verdict);
    }

    [Theory]
    [MemberData(nameof(Healthy))]
    public void Тег_в_конце_не_делает_файл_повреждённым(string extension, byte[] body)
    {
        ContainerValidation result = Check(extension, [.. body, .. SyntheticFiles.TrailingId3v1()]);

        Assert.NotEqual(ContainerVerdict.Damaged, result.Verdict);
        Assert.NotEqual(ContainerVerdict.NotSupported, result.Verdict);
    }

    [Theory]
    [MemberData(nameof(Healthy))]
    public void Теги_с_обеих_сторон_не_делают_файл_повреждённым(string extension, byte[] body)
    {
        byte[] file =
        [
            .. SyntheticFiles.LeadingId3(),
            .. body,
            .. SyntheticFiles.TrailingApev2(),
            .. SyntheticFiles.TrailingId3v1(),
        ];

        ContainerValidation result = Check(extension, file);

        Assert.NotEqual(ContainerVerdict.Damaged, result.Verdict);
        Assert.NotEqual(ContainerVerdict.NotSupported, result.Verdict);
    }

    /// <summary>
    /// Обратная сторона того же: пропуская теги, проверка не должна ослепнуть.
    /// </summary>
    [Fact]
    public void Повреждение_за_тегом_всё_равно_находится()
    {
        byte[] ogg = SyntheticFiles.Ogg(pages: 3);
        ogg[SyntheticFiles.OggPageOffset(1) + 40] ^= 0xFF;

        ContainerValidation result = Check(".ogg", [.. SyntheticFiles.LeadingId3(), .. ogg]);

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.Equal(ContainerDamage.Checksum, result.Damage);
    }

    /// <summary>
    /// Смещение ошибки человек ищет в файле, а не в потоке за тегом, — значит
    /// считать его надо от начала файла.
    /// </summary>
    [Fact]
    public void Смещение_ошибки_считается_от_начала_файла()
    {
        byte[] tag = SyntheticFiles.LeadingId3(300);
        byte[] ogg = SyntheticFiles.Ogg(pages: 3);
        ogg[SyntheticFiles.OggPageOffset(2) + 40] ^= 0xFF;

        ContainerValidation result = Check(".ogg", [.. tag, .. ogg]);

        Assert.Equal(tag.Length + SyntheticFiles.OggPageOffset(2), result.ErrorOffset);
    }

    [Fact]
    public void Формат_опознаётся_по_данным_за_тегом_а_не_по_самому_тегу()
    {
        ContainerValidation result = Check(".ogg", [.. SyntheticFiles.LeadingId3(), .. SyntheticFiles.Ogg()]);

        Assert.Equal("Ogg", result.Format);
    }

    // ── Сами границы ─────────────────────────────────────────────────────

    [Fact]
    public void Без_тегов_границы_совпадают_с_файлом()
    {
        ContainerBounds bounds = Measure(SyntheticFiles.Ogg());

        Assert.Equal(0, bounds.AudioStart);
        Assert.Equal(bounds.FileLength, bounds.AudioEnd);
    }

    [Fact]
    public void Тег_в_начале_сдвигает_начало_звука()
    {
        byte[] tag = SyntheticFiles.LeadingId3(300);
        ContainerBounds bounds = Measure([.. tag, .. SyntheticFiles.Ogg()]);

        Assert.Equal(tag.Length, bounds.AudioStart);
        Assert.Equal(bounds.FileLength, bounds.AudioEnd);
    }

    [Fact]
    public void Теги_в_конце_снимаются_все_подряд()
    {
        byte[] body = SyntheticFiles.Ogg();
        ContainerBounds bounds = Measure(
            [.. body, .. SyntheticFiles.TrailingApev2(), .. SyntheticFiles.TrailingId3v1()]);

        Assert.Equal(0, bounds.AudioStart);
        Assert.Equal(body.Length, bounds.AudioEnd);
        Assert.Equal(body.Length, bounds.AudioLength);
    }

    /// <summary>
    /// Тег, объявивший себя больше файла, оставляет мерку без ответа.
    /// </summary>
    /// <remarks>
    /// Границы в этом случае отдаются по всему файлу — гадать, где на самом
    /// деле кончается тег, значило бы придумывать. Разборщик за такой файл не
    /// берётся и вердикта не выносит: «разборщика нет» честнее, чем разбор
    /// заведомо не того места. Сам файл при этом не остаётся без внимания —
    /// его читает декодер, и о неудаче скажет он.
    /// </remarks>
    [Fact]
    public void Тег_длиннее_файла_оставляет_границы_по_всему_файлу()
    {
        byte[] file = [.. SyntheticFiles.LeadingId3(50, declaredPayloadBytes: 1_000_000), .. SyntheticFiles.Ogg()];
        ContainerBounds bounds = Measure(file);

        Assert.Equal(0, bounds.AudioStart);
        Assert.Equal(file.Length, bounds.AudioEnd);

        // Ложного вердикта об исправности здесь быть не должно.
        ContainerValidation result = Check(".ogg", file);
        Assert.Equal(ContainerVerdict.NotSupported, result.Verdict);
    }

    private static ContainerBounds Measure(byte[] file)
    {
        using MemoryStream stream = new(file, writable: false);
        return ContainerBounds.Measure(stream, file.Length);
    }

    private ContainerValidation Check(string extension, byte[] file)
    {
        string path = Path.Combine(_folder, $"{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, file);
        return new ContainerIntegrityChecker().Check(path, CancellationToken.None);
    }
}
