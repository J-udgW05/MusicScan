using MusicScanIntegrity.Core.Integrity;
using Xunit;

namespace MusicScanIntegrity.Core.Tests.Integrity;

public sealed class OggValidatorTests
{
    private static ContainerValidation Validate(byte[] file)
    {
        using MemoryStream stream = new(file, writable: false);
        return new OggValidator().Validate(stream, ContainerBounds.Measure(stream, file.Length), CancellationToken.None);
    }

    [Fact]
    public void Healthy_file_passes_checksum_validation()
    {
        ContainerValidation result = Validate(SyntheticFiles.Ogg(pages: 4));

        Assert.Equal(ContainerVerdict.Verified, result.Verdict);
        Assert.Equal(4, result.UnitsChecked);
    }

    [Fact]
    public void Corrupted_page_byte_is_caught_by_checksum()
    {
        byte[] file = SyntheticFiles.Ogg(pages: 3);
        int second = SyntheticFiles.OggPageOffset(1);
        file[second + 40] ^= 0x20;

        ContainerValidation result = Validate(file);

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.Equal(1, result.UnitsChecked);
        Assert.Equal(second, result.ErrorOffset);
    }

    [Fact]
    public void Missing_end_of_stream_flag_is_truncation()
    {
        ContainerValidation result = Validate(SyntheticFiles.Ogg(pages: 3, withEndFlag: false));

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void Page_sequence_gap_means_lost_data()
    {
        ContainerValidation result = Validate(SyntheticFiles.Ogg(pages: 3, skipSequence: true));

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.Contains("не подряд", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Cut_file_is_reported_as_truncated()
    {
        byte[] file = SyntheticFiles.Ogg(pages: 3);

        ContainerValidation result = Validate(file[..^40]);

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.True(result.Truncated);
    }
}

public sealed class Mp3ValidatorTests
{
    private static ContainerValidation Validate(byte[] file)
    {
        using MemoryStream stream = new(file, writable: false);
        return new Mp3Validator().Validate(stream, ContainerBounds.Measure(stream, file.Length), CancellationToken.None);
    }

    [Fact]
    public void Frame_chain_without_checksums_is_structure_checked()
    {
        ContainerValidation result = Validate(SyntheticFiles.Mp3(frames: 6));

        Assert.Equal(ContainerVerdict.StructureOnly, result.Verdict);
        Assert.Equal(6, result.UnitsChecked);
    }

    [Fact]
    public void Protected_frames_are_checksum_verified()
    {
        ContainerValidation result = Validate(SyntheticFiles.Mp3(frames: 4, withCrc: true));

        Assert.Equal(ContainerVerdict.Verified, result.Verdict);
        Assert.Equal(4, result.UnitsChecked);
    }

    [Fact]
    public void Corrupted_side_info_of_protected_frame_is_caught()
    {
        byte[] file = SyntheticFiles.Mp3(frames: 4, withCrc: true);
        file[SyntheticFiles.Mp3FrameOffset(2) + 10] ^= 0x11;

        ContainerValidation result = Validate(file);

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.Equal(2, result.UnitsChecked);
    }

    [Fact]
    public void Leading_tag_does_not_interfere()
    {
        ContainerValidation result = Validate(SyntheticFiles.Mp3(frames: 3, leadingId3: true));

        Assert.Equal(ContainerVerdict.StructureOnly, result.Verdict);
        Assert.Equal(3, result.UnitsChecked);
    }

    [Fact]
    public void Missing_frames_show_in_info_header()
    {
        // The header promises twenty frames; the file holds five.
        ContainerValidation result = Validate(SyntheticFiles.Mp3(frames: 5, declaredFrames: 20));

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void Incomplete_last_frame_is_truncation()
    {
        byte[] file = SyntheticFiles.Mp3(frames: 4);

        ContainerValidation result = Validate(file[..^200]);

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.True(result.Truncated);
        Assert.Equal(3, result.UnitsChecked);
    }

    [Fact]
    public void Garbage_in_middle_marks_file_damaged()
    {
        byte[] file = SyntheticFiles.Mp3(frames: 6);
        byte[] junk = new byte[4096];
        byte[] mixed = [.. file[..SyntheticFiles.Mp3FrameOffset(3)], .. junk, .. file[SyntheticFiles.Mp3FrameOffset(3)..]];

        ContainerValidation result = Validate(mixed);

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.Contains("мусор", result.Message, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class Mp4ValidatorTests
{
    private static ContainerValidation Validate(byte[] file)
    {
        using MemoryStream stream = new(file, writable: false);
        return new Mp4Validator().Validate(stream, ContainerBounds.Measure(stream, file.Length), CancellationToken.None);
    }

    [Fact]
    public void Whole_file_passes_structure_check()
    {
        ContainerValidation result = Validate(SyntheticFiles.Mp4());

        Assert.Equal(ContainerVerdict.StructureOnly, result.Verdict);
        Assert.Equal(3, result.UnitsChecked);
    }

    [Fact]
    public void Box_longer_than_remainder_means_truncation()
    {
        ContainerValidation result = Validate(SyntheticFiles.Mp4(mediaBytes: 128, declaredMediaBytes: 4096));

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void Missing_track_description_is_reported()
    {
        ContainerValidation result = Validate(SyntheticFiles.Mp4(withMovie: false));

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.Contains("дорожек", result.Message, StringComparison.Ordinal);
    }
}

public sealed class RiffValidatorTests
{
    private static ContainerValidation Validate(byte[] file)
    {
        using MemoryStream stream = new(file, writable: false);
        return new RiffValidator().Validate(stream, ContainerBounds.Measure(stream, file.Length), CancellationToken.None);
    }

    [Fact]
    public void Whole_file_passes_structure_check()
    {
        ContainerValidation result = Validate(SyntheticFiles.Wav());

        Assert.Equal(ContainerVerdict.StructureOnly, result.Verdict);
    }

    [Fact]
    public void Cut_file_is_caught_by_declared_length()
    {
        byte[] file = SyntheticFiles.Wav(dataBytes: 512);

        ContainerValidation result = Validate(file[..^128]);

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.True(result.Truncated);
    }

    /// <summary>Zero and all-ones in the length field mean "length unknown".</summary>
    /// <remarks>
    /// That is how a header is written by anything streaming audio that cannot
    /// seek back to fill in the size — live recording, piping. The file is whole,
    /// and calling it truncated would be a false alarm.
    /// </remarks>
    [Theory]
    [InlineData(0x00u)]
    [InlineData(0xFFu)]
    public void Unset_header_length_is_not_truncation(uint fill)
    {
        byte[] file = SyntheticFiles.Wav();
        for (int i = 4; i < 8; i++)
        {
            file[i] = (byte)fill;
        }

        ContainerValidation result = Validate(file);

        Assert.Equal(ContainerVerdict.StructureOnly, result.Verdict);
    }

    /// <summary>The flip side: a real truncation is still found.</summary>
    [Fact]
    public void Declared_length_beyond_file_is_truncation()
    {
        byte[] file = SyntheticFiles.Wav();
        file[4] = 0xFF;
        file[5] = 0xFF;

        ContainerValidation result = Validate(file);

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void File_without_format_chunk_is_damaged()
    {
        ContainerValidation result = Validate(SyntheticFiles.Wav(withFormat: false));

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
    }
}

public sealed class WavPackValidatorTests
{
    private static ContainerValidation Validate(byte[] file)
    {
        using MemoryStream stream = new(file, writable: false);
        return new WavPackValidator().Validate(stream, ContainerBounds.Measure(stream, file.Length), CancellationToken.None);
    }

    [Fact]
    public void Block_chain_is_structure_checked()
    {
        ContainerValidation result = Validate(SyntheticFiles.WavPack(blocks: 4));

        Assert.Equal(ContainerVerdict.StructureOnly, result.Verdict);
        Assert.Equal(4, result.UnitsChecked);
        Assert.Contains("распакованному", result.TechnicalDetail!, StringComparison.Ordinal);
    }

    [Fact]
    public void Box_longer_than_remainder_means_truncation()
    {
        ContainerValidation result = Validate(SyntheticFiles.WavPack(blocks: 2, declaredExtra: 500));

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.True(result.Truncated);
    }
}

public sealed class ApeValidatorTests
{
    private static ContainerValidation Validate(byte[] file)
    {
        using MemoryStream stream = new(file, writable: false);
        return new ApeValidator().Validate(stream, ContainerBounds.Measure(stream, file.Length), CancellationToken.None);
    }

    [Fact]
    public void Matching_lengths_pass_structure_check()
    {
        ContainerValidation result = Validate(SyntheticFiles.Ape());

        Assert.Equal(ContainerVerdict.StructureOnly, result.Verdict);
    }

    [Fact]
    public void Data_shorter_than_descriptor_means_truncation()
    {
        ContainerValidation result = Validate(SyntheticFiles.Ape(audioBytes: 4096, actualBytes: 256));

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.True(result.Truncated);
    }
}

public sealed class ContainerIntegrityCheckerTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "msi-integrity-" + Guid.NewGuid().ToString("N"));

    public ContainerIntegrityCheckerTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private ContainerValidation Check(string name, byte[] content)
    {
        string path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, content);
        return new ContainerIntegrityChecker().Check(path, CancellationToken.None);
    }

    [Fact]
    public void Format_comes_from_contents_not_extension()
    {
        // FLAC inside while the name promises MP3; FLAC rules must apply.
        ContainerValidation result = Check("подделка.mp3", FlacFileBuilder.Build(frames: 3));

        Assert.Equal("FLAC", result.Format);
        Assert.Equal(ContainerVerdict.Verified, result.Verdict);
    }

    [Fact]
    public void Unknown_format_gets_no_verdict()
    {
        ContainerValidation result = Check("непонятно.bin", new byte[512]);

        Assert.Equal(ContainerVerdict.NotSupported, result.Verdict);
    }

    [Fact]
    public void Mp3_behind_id3_tag_is_identified()
    {
        ContainerValidation result = Check("трек.mp3", SyntheticFiles.Mp3(frames: 3, leadingId3: true));

        Assert.Equal("MP3", result.Format);
        Assert.Equal(ContainerVerdict.StructureOnly, result.Verdict);
    }

    [Fact]
    public void Missing_file_does_not_throw()
    {
        ContainerValidation result = new ContainerIntegrityChecker()
            .Check(Path.Combine(_folder, "нет-такого.flac"), CancellationToken.None);

        Assert.Equal(ContainerVerdict.Unreadable, result.Verdict);
    }
}
