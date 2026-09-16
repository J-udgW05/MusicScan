using MusicScanIntegrity.Core.Integrity;
using Xunit;

namespace MusicScanIntegrity.Core.Tests.Integrity;

public sealed class FlacValidatorTests
{
    private static ContainerValidation Validate(byte[] file)
    {
        using MemoryStream stream = new(file, writable: false);
        return new FlacValidator().Validate(stream, ContainerBounds.Measure(stream, file.Length), CancellationToken.None);
    }

    [Fact]
    public void Healthy_file_passes_checksum_validation()
    {
        byte[] file = FlacFileBuilder.Build(frames: 5);

        ContainerValidation result = Validate(file);

        Assert.Equal(ContainerVerdict.Verified, result.Verdict);
        Assert.Equal(5, result.UnitsChecked);
        Assert.False(result.Truncated);
    }

    [Fact]
    public void Leading_and_trailing_tags_do_not_interfere()
    {
        byte[] file = FlacFileBuilder.Build(frames: 3, leadingId3: true, trailingId3: true);

        ContainerValidation result = Validate(file);

        Assert.Equal(ContainerVerdict.Verified, result.Verdict);
        Assert.Equal(3, result.UnitsChecked);
    }

    [Fact]
    public void Corrupted_frame_byte_is_caught_by_checksum()
    {
        byte[] file = FlacFileBuilder.Build(frames: 4);

        // Corrupt the middle of the third frame so its checksum no longer matches.
        int thirdFrame = FlacFileBuilder.FirstFrameOffset() + (2 * FlacFileBuilder.FrameLength());
        file[thirdFrame + 20] ^= 0x40;

        ContainerValidation result = Validate(file);

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.Equal(2, result.UnitsChecked);
        Assert.Equal(thirdFrame, result.ErrorOffset);
    }

    [Fact]
    public void Cut_inside_last_frame_is_truncation()
    {
        byte[] file = FlacFileBuilder.Build(frames: 4);
        byte[] cut = file[..^20];

        ContainerValidation result = Validate(cut);

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.True(result.Truncated);
        Assert.Equal(3, result.UnitsChecked);
    }

    [Fact]
    public void Missing_frames_show_in_declared_sample_count()
    {
        // STREAMINFO promises six frames but four are present: the file was cut exactly
        // at a frame boundary, so the remaining checksums all match.
        byte[] file = FlacFileBuilder.Build(frames: 4, declaredSamples: 6L * FlacFileBuilder.BlockSize);

        ContainerValidation result = Validate(file);

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.True(result.Truncated);
        Assert.Equal(4, result.UnitsChecked);
        Assert.Contains("обрывается", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Garbage_after_last_frame_is_reported()
    {
        byte[] file = [.. FlacFileBuilder.Build(frames: 3), .. new byte[64]];

        ContainerValidation result = Validate(file);

        // The last frame "does not end": the validator sees data after it that should
        // not be there.
        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.Equal(2, result.UnitsChecked);
    }

    [Fact]
    public void Wrong_file_signature_is_damage()
    {
        byte[] file = FlacFileBuilder.Build();
        file[0] = (byte)'x';

        ContainerValidation result = Validate(file);

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.Equal(0, result.UnitsChecked);
    }

    [Fact]
    public void File_without_frames_is_truncation()
    {
        byte[] file = FlacFileBuilder.Build(frames: 0, declaredSamples: 0);

        ContainerValidation result = Validate(file);

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void File_is_identified_by_signature()
    {
        FlacValidator validator = new();

        Assert.True(validator.Matches("fLaC"u8));
        Assert.False(validator.Matches("OggS"u8));
    }
}
