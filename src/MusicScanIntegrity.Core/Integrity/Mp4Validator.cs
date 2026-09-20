using System.Buffers.Binary;
using MusicScanIntegrity.Core.Resources;

namespace MusicScanIntegrity.Core.Integrity;

/// <summary>
/// Validates the MP4 container (M4A, ALAC, AAC) by its box structure.
/// </summary>
/// <remarks>
/// MP4 has no checksums, so something else is verified: the file is a series
/// of boxes, each declaring its length, and those lengths must land exactly at
/// the end. A partial download fails here — the last box promises more data
/// than remains. Missing mandatory boxes mean the start is damaged.
/// </remarks>
internal sealed class Mp4Validator : IContainerValidator
{
    /// <inheritdoc />
    public string Format => "MP4";

    /// <inheritdoc />
    public bool Matches(ReadOnlySpan<byte> header) =>
        header.Length >= 12 && header[4..8].SequenceEqual("ftyp"u8);

    /// <inheritdoc />
    public ContainerValidation Validate(Stream stream, ContainerBounds bounds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        stream.Position = 0;
        StreamWindow window = new(stream, 4096);

        if (!window.Skip(bounds.AudioStart))
        {
            return ContainerValidation.Damaged(
                Format,
                Strings.Valid_TruncatedInLeadingTag,
                Common.Format.Text(Strings.Valid_TagLongerThanFile, bounds.AudioStart),
                truncated: true);
        }

        int boxes = 0;
        bool sawFileType = false;
        bool sawMovie = false;
        bool sawMedia = false;

        while (window.Position < bounds.AudioEnd)
        {
            cancellationToken.ThrowIfCancellationRequested();

            long boxPosition = window.Position;
            long remaining = bounds.AudioEnd - boxPosition;

            if (!window.Ensure(8))
            {
                return ContainerValidation.Damaged(
                    Format,
                    Strings.Mp4_TruncatedBoxHeader,
                    Common.Format.Text(Strings.Mp4_TruncatedBoxHeader_Detail, boxPosition, remaining),
                    boxes,
                    boxPosition,
                    truncated: true);
            }

            ReadOnlySpan<byte> header = window.Peek(8);
            long size = BinaryPrimitives.ReadUInt32BigEndian(header);
            string type = System.Text.Encoding.ASCII.GetString(header[4..8]);
            int headerSize = 8;

            switch (size)
            {
                case 1 when window.Ensure(16):
                    // A length of 1 means the real 64-bit length follows.
                    size = (long)BinaryPrimitives.ReadUInt64BigEndian(window.Peek(16)[8..]);
                    headerSize = 16;
                    break;

                case 0:
                    // Zero means "to the end of the file".
                    size = remaining;
                    break;
            }

            if (size < headerSize)
            {
                return ContainerValidation.Damaged(
                    Format,
                    Strings.Mp4_ImpossibleLength,
                    Common.Format.Text(Strings.Mp4_ImpossibleLength_Detail, type, boxPosition, size),
                    boxes,
                    boxPosition);
            }

            if (size > remaining)
            {
                return ContainerValidation.Damaged(
                    Format,
                    Common.Format.Text(Strings.Mp4_BoxShort, type),
                    Common.Format.Text(Strings.Mp4_BoxShort_Detail, boxPosition, size, remaining),
                    boxes,
                    boxPosition,
                    truncated: true);
            }

            sawFileType |= type == "ftyp";
            sawMovie |= type == "moov";
            sawMedia |= type is "mdat" or "mdia";

            if (!window.Skip(size))
            {
                return ContainerValidation.Damaged(
                    Format,
                    Common.Format.Text(Strings.Mp4_TruncatedInBox, type),
                    Common.Format.Text(Strings.Valid_Offset, boxPosition),
                    boxes,
                    boxPosition,
                    truncated: true);
            }

            boxes++;
        }

        if (!sawFileType)
        {
            return ContainerValidation.Damaged(
                Format,
                Strings.Mp4_NoFtyp,
                Strings.Mp4_NoFtyp_Detail,
                boxes);
        }

        if (!sawMovie)
        {
            return ContainerValidation.Damaged(
                Format,
                Strings.Mp4_NoMoov,
                Strings.Mp4_NoMoov_Detail,
                boxes);
        }

        if (!sawMedia)
        {
            return ContainerValidation.Damaged(
                Format,
                Strings.Mp4_NoMdat,
                Strings.Mp4_NoMdat_Detail,
                boxes,
                truncated: true);
        }

        return ContainerValidation.StructureOnly(
            Format,
            boxes,
            Common.Format.Text(Strings.Mp4_StructureOnly, boxes));
    }
}
