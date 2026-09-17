using System.Buffers.Binary;
using MusicScanIntegrity.Core.Resources;

namespace MusicScanIntegrity.Core.Integrity;

/// <summary>
/// Validates WavPack by walking its block chain.
/// </summary>
/// <remarks>
/// Every WavPack block carries a checksum, but it covers the
/// <em>decompressed</em> samples and cannot be verified without writing a
/// decoder. What is checked instead is the chain: each block declares a length
/// and the blocks must land exactly at the end of the file. That catches
/// truncation and garbage but is weaker than real checksum verification, and
/// the verdict says so.
/// </remarks>
internal sealed class WavPackValidator : IContainerValidator
{
    /// <summary>Block header: signature, length and control fields.</summary>
    private const int BlockHeaderSize = 32;

    /// <inheritdoc />
    public string Format => "WavPack";

    /// <inheritdoc />
    public bool Matches(ReadOnlySpan<byte> header) => header.StartsWith("wvpk"u8);

    /// <inheritdoc />
    public ContainerValidation Validate(Stream stream, ContainerBounds bounds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        long audioEnd = bounds.AudioEnd;
        stream.Position = 0;
        StreamWindow window = new(stream, 8192);

        if (!window.Skip(bounds.AudioStart))
        {
            return ContainerValidation.Damaged(
                Format,
                Strings.Valid_TruncatedInLeadingTag,
                Common.Format.Text(Strings.Valid_TagLongerThanFile, bounds.AudioStart),
                truncated: true);
        }

        int blocks = 0;

        while (window.Position < audioEnd)
        {
            cancellationToken.ThrowIfCancellationRequested();

            long blockPosition = window.Position;
            long remaining = audioEnd - blockPosition;

            if (!window.Ensure(BlockHeaderSize))
            {
                return ContainerValidation.Damaged(
                    Format,
                    Strings.Mp4_TruncatedBoxHeader,
                    Common.Format.Text(Strings.Mp4_TruncatedBoxHeader_Detail, blockPosition, remaining),
                    blocks,
                    blockPosition,
                    truncated: true);
            }

            ReadOnlySpan<byte> header = window.Peek(BlockHeaderSize);

            if (!header.StartsWith("wvpk"u8))
            {
                return ContainerValidation.Damaged(
                    Format,
                    blocks == 0
                        ? Strings.WavPack_BadStart
                        : Common.Format.Text(Strings.WavPack_NotABlock, blocks),
                    Common.Format.Text(Strings.WavPack_NotABlock_Detail, blockPosition),
                    blocks,
                    blockPosition);
            }

            // The length excludes the first eight bytes of the header itself.
            long size = BinaryPrimitives.ReadUInt32LittleEndian(header[4..8]) + 8L;

            if (size < BlockHeaderSize)
            {
                return ContainerValidation.Damaged(
                    Format,
                    Strings.Mp4_ImpossibleLength,
                    Common.Format.Text(Strings.WavPack_ImpossibleLength_Detail, blockPosition, size),
                    blocks,
                    blockPosition);
            }

            if (size > remaining)
            {
                return ContainerValidation.Damaged(
                    Format,
                    Common.Format.Text(Strings.WavPack_BlockUnfinished, blocks + 1),
                    Common.Format.Text(Strings.Mp4_BoxShort_Detail, blockPosition, size, remaining),
                    blocks,
                    blockPosition,
                    truncated: true);
            }

            window.Skip(size);
            blocks++;
        }

        if (blocks == 0)
        {
            return ContainerValidation.Damaged(
                Format,
                Strings.WavPack_NoBlocks,
                Strings.WavPack_NoBlocks_Detail,
                truncated: true);
        }

        return ContainerValidation.StructureOnly(
            Format,
            blocks,
            Common.Format.Text(Strings.WavPack_StructureOnly, blocks));
    }
}
