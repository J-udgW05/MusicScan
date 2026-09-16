using MusicScanIntegrity.Core.Integrity;
using Xunit;

namespace MusicScanIntegrity.Core.Tests.Integrity;

/// <summary>Tags around the audio must not look like damage.</summary>
/// <remarks>
/// Tags are part of the file but not of the stream. A validator measuring from
/// byte zero would read a leading ID3 as a broken header and a trailing ID3v1
/// as garbage after the last frame — false alarms on healthy files, the worst
/// thing this program can say about a collection.
/// </remarks>
public sealed class ContainerBoundsTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("bounds").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    /// <summary>Healthy files of every format that can carry tags.</summary>
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
    public void Leading_tag_does_not_make_file_damaged(string extension, byte[] body)
    {
        ContainerValidation result = Check(extension, [.. SyntheticFiles.LeadingId3(300), .. body]);

        Assert.NotEqual(ContainerVerdict.Damaged, result.Verdict);
        Assert.NotEqual(ContainerVerdict.NotSupported, result.Verdict);
    }

    [Theory]
    [MemberData(nameof(Healthy))]
    public void Trailing_tag_does_not_make_file_damaged(string extension, byte[] body)
    {
        ContainerValidation result = Check(extension, [.. body, .. SyntheticFiles.TrailingId3v1()]);

        Assert.NotEqual(ContainerVerdict.Damaged, result.Verdict);
        Assert.NotEqual(ContainerVerdict.NotSupported, result.Verdict);
    }

    [Theory]
    [MemberData(nameof(Healthy))]
    public void Tags_on_both_ends_do_not_make_file_damaged(string extension, byte[] body)
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

    /// <summary>The flip side: skipping tags must not blind the check.</summary>
    [Fact]
    public void Damage_behind_tag_is_still_found()
    {
        byte[] ogg = SyntheticFiles.Ogg(pages: 3);
        ogg[SyntheticFiles.OggPageOffset(1) + 40] ^= 0xFF;

        ContainerValidation result = Check(".ogg", [.. SyntheticFiles.LeadingId3(), .. ogg]);

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.Equal(ContainerDamage.Checksum, result.Damage);
    }

    /// <summary>
    /// The error offset is looked up in the file, not the stream behind the tag, so
    /// it is measured from the start of the file.
    /// </summary>
    [Fact]
    public void Error_offset_is_measured_from_file_start()
    {
        byte[] tag = SyntheticFiles.LeadingId3(300);
        byte[] ogg = SyntheticFiles.Ogg(pages: 3);
        ogg[SyntheticFiles.OggPageOffset(2) + 40] ^= 0xFF;

        ContainerValidation result = Check(".ogg", [.. tag, .. ogg]);

        Assert.Equal(tag.Length + SyntheticFiles.OggPageOffset(2), result.ErrorOffset);
    }

    [Fact]
    public void Format_is_identified_behind_tag_not_by_tag()
    {
        ContainerValidation result = Check(".ogg", [.. SyntheticFiles.LeadingId3(), .. SyntheticFiles.Ogg()]);

        Assert.Equal("Ogg", result.Format);
    }

    // ── Bounds themselves ────────────────────────────────────────────────

    [Fact]
    public void Without_tags_bounds_match_file()
    {
        ContainerBounds bounds = Measure(SyntheticFiles.Ogg());

        Assert.Equal(0, bounds.AudioStart);
        Assert.Equal(bounds.FileLength, bounds.AudioEnd);
    }

    [Fact]
    public void Leading_tag_moves_audio_start()
    {
        byte[] tag = SyntheticFiles.LeadingId3(300);
        ContainerBounds bounds = Measure([.. tag, .. SyntheticFiles.Ogg()]);

        Assert.Equal(tag.Length, bounds.AudioStart);
        Assert.Equal(bounds.FileLength, bounds.AudioEnd);
    }

    [Fact]
    public void All_trailing_tags_are_peeled()
    {
        byte[] body = SyntheticFiles.Ogg();
        ContainerBounds bounds = Measure(
            [.. body, .. SyntheticFiles.TrailingApev2(), .. SyntheticFiles.TrailingId3v1()]);

        Assert.Equal(0, bounds.AudioStart);
        Assert.Equal(body.Length, bounds.AudioEnd);
        Assert.Equal(body.Length, bounds.AudioLength);
    }

    /// <summary>A tag claiming to be larger than the file leaves the bounds undetermined.</summary>
    /// <remarks>
    /// Bounds then span the whole file: guessing where the tag really ends would be
    /// invention. No validator takes such a file and no verdict is given — "no
    /// validator" is more honest than parsing the wrong place. The file is still
    /// read by the decoder, which reports the failure.
    /// </remarks>
    [Fact]
    public void Tag_longer_than_file_leaves_whole_file_bounds()
    {
        byte[] file = [.. SyntheticFiles.LeadingId3(50, declaredPayloadBytes: 1_000_000), .. SyntheticFiles.Ogg()];
        ContainerBounds bounds = Measure(file);

        Assert.Equal(0, bounds.AudioStart);
        Assert.Equal(file.Length, bounds.AudioEnd);

        // There must be no false "healthy" verdict here.
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
