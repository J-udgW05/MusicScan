using System.Buffers.Binary;
using MusicScanIntegrity.Core.Resources;

namespace MusicScanIntegrity.Core.Integrity;

/// <summary>
/// Validates the RIFF container (WAV and AIFF) by walking its chunks.
/// </summary>
/// <remarks>
/// WAV has no checksums — it is raw samples written back to back — but the
/// lengths are declared twice, in the file header and in the data chunk header.
/// Those catch the commonest failure of uncompressed audio: a file copied only
/// part way, where the declared length stayed but the data did not.
/// </remarks>
internal sealed class RiffValidator : IContainerValidator
{
    /// <inheritdoc />
    public string Format => "WAV";

    /// <inheritdoc />
    public bool Matches(ReadOnlySpan<byte> header) =>
        header.Length >= 12
        && (header.StartsWith("RIFF"u8) || header.StartsWith("FORM"u8));

    /// <inheritdoc />
    public ContainerValidation Validate(Stream stream, ContainerBounds bounds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        stream.Position = 0;
        StreamWindow window = new(stream, 4096);

        if (!window.Skip(bounds.AudioStart) || !window.Ensure(12))
        {
            return ContainerValidation.Damaged(
                Format,
                Strings.Riff_TooShort,
                Common.Format.Text(Strings.Valid_Size, bounds.FileLength),
                truncated: true);
        }

        long riffStart = window.Position;
        ReadOnlySpan<byte> header = window.Peek(12);
        bool isRiff = header.StartsWith("RIFF"u8);
        string format = isRiff ? "WAV" : "AIFF";

        long declared = isRiff
            ? BinaryPrimitives.ReadUInt32LittleEndian(header[4..8])
            : BinaryPrimitives.ReadUInt32BigEndian(header[4..8]);

        // Zero and all-ones mean "length unknown": that is how the header is
        // written by anything streaming audio that cannot seek back to fill the
        // size in. It is not a truncation, and the chunk chain verifies without
        // a declared length anyway.
        bool declaredKnown = declared is not (0 or uint.MaxValue);

        // The declared length is measured from byte nine, hence the plus eight.
        long declaredEnd = riffStart + declared + 8;

        if (declaredKnown && declaredEnd > bounds.FileLength)
        {
            return ContainerValidation.Damaged(
                format,
                Strings.Riff_ShorterThanDeclared,
                Common.Format.Text(Strings.Riff_ShorterThanDeclared_Detail, declaredEnd, bounds.FileLength),
                truncated: true);
        }

        window.Advance(12);

        long limit = declaredKnown ? Math.Min(bounds.FileLength, declaredEnd) : bounds.FileLength;
        int chunks = 0;
        bool sawFormat = false;
        bool sawData = false;

        while (window.Position + 8 <= limit)
        {
            cancellationToken.ThrowIfCancellationRequested();

            long chunkPosition = window.Position;

            if (!window.Ensure(8))
            {
                break;
            }

            ReadOnlySpan<byte> chunk = window.Peek(8);
            string id = System.Text.Encoding.ASCII.GetString(chunk[..4]);
            long size = isRiff
                ? BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..8])
                : BinaryPrimitives.ReadUInt32BigEndian(chunk[4..8]);

            sawFormat |= id is "fmt " or "COMM";
            bool isData = id is "data" or "SSND";
            sawData |= isData;

            if (chunkPosition + 8 + size > bounds.FileLength)
            {
                return ContainerValidation.Damaged(
                    format,
                    isData
                        ? Strings.Riff_DataShort
                        : Common.Format.Text(Strings.Riff_TruncatedInChunk, id),
                    Common.Format.Text(Strings.Riff_TruncatedInChunk_Detail, chunkPosition, size, bounds.FileLength - chunkPosition - 8),
                    chunks,
                    chunkPosition,
                    truncated: true);
            }

            // Chunks are aligned to an even boundary.
            long step = 8 + size + (size % 2);
            if (!window.Skip(step))
            {
                break;
            }

            chunks++;
        }

        if (!sawFormat)
        {
            return ContainerValidation.Damaged(
                format,
                Strings.Riff_NoFmt,
                Strings.Riff_NoFmt_Detail,
                chunks);
        }

        if (!sawData)
        {
            return ContainerValidation.Damaged(
                format,
                Strings.Riff_NoData,
                Strings.Riff_NoData_Detail,
                chunks,
                truncated: true);
        }

        return ContainerValidation.StructureOnly(
            format,
            chunks,
            Common.Format.Text(Strings.Riff_StructureOnly, chunks));
    }
}
