using MusicScanIntegrity.Core.Resources;

namespace MusicScanIntegrity.Core.Integrity;

/// <summary>
/// Validates MP3 by walking frames, checking their lengths and any checksums.
/// </summary>
/// <remarks>
/// <para>
/// MP3 has no whole-file checksum. What it has is a per-frame length derived
/// from the header: if it is right, the next frame begins exactly where
/// promised. A break in that chain is a reliable damage signal, and it catches
/// what actually happens — partial downloads and garbage in the middle.
/// </para>
/// <para>
/// Some frames are protected and carry a CRC-16 over the header and side info,
/// which can be verified for real. Those are counted separately: a file whose
/// checksums matched has been checked more strictly than one with nothing to
/// verify.
/// </para>
/// <para>
/// The leading Xing/Info header stores the frame count. Fewer frames than
/// promised means truncation, even when every remaining frame is intact.
/// </para>
/// </remarks>
internal sealed class Mp3Validator : IContainerValidator
{
    /// <summary>How much junk between frames is still tolerated.</summary>
    /// <remarks>
    /// Zero will not do: real files carry short stubs between the tag and the
    /// first frame, or after the last one, and condemning a file over those
    /// would be a false alarm.
    /// </remarks>
    private const int JunkTolerance = 2048;

    private static readonly int[][] Bitrates =
    [
        // MPEG 1: layer I, layer II, layer III
        [0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448, 0],
        [0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384, 0],
        [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0],

        // MPEG 2 and 2.5: layer I, layers II and III
        [0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256, 0],
        [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0],
    ];

    private static readonly int[][] SampleRates =
    [
        [11025, 12000, 8000, 0],   // MPEG 2.5
        [0, 0, 0, 0],              // reserved
        [22050, 24000, 16000, 0],  // MPEG 2
        [44100, 48000, 32000, 0],  // MPEG 1
    ];

    /// <inheritdoc />
    public string Format => "MP3";

    /// <inheritdoc />
    public bool Matches(ReadOnlySpan<byte> header) =>
        header.Length >= 2 && header[0] == 0xFF && (header[1] & 0xE0) == 0xE0;

    /// <inheritdoc />
    public ContainerValidation Validate(Stream stream, ContainerBounds bounds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        long audioEnd = bounds.AudioEnd;
        stream.Position = 0;
        StreamWindow window = new(stream);

        if (!window.Skip(bounds.AudioStart))
        {
            return ContainerValidation.Damaged(
                Format,
                Strings.Valid_TruncatedInLeadingTag,
                Common.Format.Text(Strings.Valid_TagLongerThanFile, bounds.AudioStart),
                truncated: true);
        }

        int frames = 0;
        int checkedSums = 0;
        long junkBytes = 0;
        int declaredFrames = 0;
        long firstErrorOffset = -1;

        while (window.Position < audioEnd)
        {
            cancellationToken.ThrowIfCancellationRequested();

            long framePosition = window.Position;
            long remaining = audioEnd - framePosition;

            if (!window.Ensure(4) || remaining < 4)
            {
                // A tail shorter than a frame header is a stub, not a frame.
                junkBytes += remaining;
                break;
            }

            if (!TryParseHeader(window.Peek(4), out FrameHeader header))
            {
                if (firstErrorOffset < 0)
                {
                    firstErrorOffset = framePosition;
                }

                junkBytes++;
                window.Advance(1);
                continue;
            }

            if (header.Length > remaining)
            {
                return ContainerValidation.Damaged(
                    Format,
                    Common.Format.Text(Strings.Mp3_FrameUnfinished, frames + 1),
                    Common.Format.Text(Strings.Mp3_FrameUnfinished_Detail, framePosition, header.Length, remaining),
                    frames,
                    framePosition,
                    truncated: true);
            }

            if (frames == 0)
            {
                declaredFrames = ReadDeclaredFrames(window, header);
            }

            if (header.CanCheckCrc && window.Ensure(header.CrcCoverage + 6))
            {
                if (!CrcMatches(window.Peek(header.CrcCoverage + 6), header))
                {
                    return ContainerValidation.Damaged(
                        Format,
                        Common.Format.Text(Strings.Mp3_FrameChecksum, frames + 1),
                        Common.Format.Text(Strings.Valid_Offset, framePosition),
                        frames,
                        framePosition,
                        damage: ContainerDamage.Checksum);
                }

                checkedSums++;
            }

            window.Skip(header.Length);
            frames++;
        }

        if (frames == 0)
        {
            return ContainerValidation.Damaged(
                Format,
                Strings.Mp3_NoFrames,
                Common.Format.Text(Strings.Mp3_NoFrames_Detail, junkBytes),
                truncated: true);
        }

        if (junkBytes > JunkTolerance)
        {
            return ContainerValidation.Damaged(
                Format,
                Strings.Mp3_Junk,
                Common.Format.Text(Strings.Mp3_Junk_Detail, junkBytes, frames),
                frames,
                firstErrorOffset < 0 ? null : firstErrorOffset);
        }

        // The Xing/Info header counts itself among the frames, so compare
        // within a tolerance of one.
        if (declaredFrames > 0 && frames < declaredFrames - 1)
        {
            return ContainerValidation.Damaged(
                Format,
                Common.Format.Text(Strings.Mp3_MissingFrames, declaredFrames, frames),
                Common.Format.Text(Strings.Mp3_MissingFrames_Detail, declaredFrames),
                frames,
                truncated: true);
        }

        return checkedSums > 0
            ? ContainerValidation.Verified(Format, frames) with
            {
                TechnicalDetail = Common.Format.Text(Strings.Mp3_Verified_Detail, frames, checkedSums),
            }
            : ContainerValidation.StructureOnly(
                Format,
                frames,
                Common.Format.Text(Strings.Mp3_StructureOnly, frames));
    }

    /// <summary>Reads the frame count from the Xing/Info header when present.</summary>
    private static int ReadDeclaredFrames(StreamWindow window, FrameHeader header)
    {
        int offset = 4 + (header.HasCrc ? 2 : 0) + header.SideInfoSize;

        if (!window.Ensure(offset + 12))
        {
            return 0;
        }

        ReadOnlySpan<byte> frame = window.Peek(offset + 12);
        ReadOnlySpan<byte> marker = frame[offset..(offset + 4)];

        if (!marker.SequenceEqual("Xing"u8) && !marker.SequenceEqual("Info"u8))
        {
            return 0;
        }

        uint flags = ReadBigEndian(frame[(offset + 4)..]);

        // The low flag bit means a frame count follows.
        return (flags & 0x01) == 0 ? 0 : (int)ReadBigEndian(frame[(offset + 8)..]);
    }

    private static bool CrcMatches(ReadOnlySpan<byte> frame, FrameHeader header)
    {
        ushort stored = (ushort)((frame[4] << 8) | frame[5]);

        // The checksum covers the last two header bytes and the side info, but
        // not the checksum field itself.
        Span<byte> covered = stackalloc byte[2 + header.CrcCoverage];
        frame[2..4].CopyTo(covered);
        frame[6..(6 + header.CrcCoverage)].CopyTo(covered[2..]);

        return Crc.Mpeg16(covered) == stored;
    }

    private static bool TryParseHeader(ReadOnlySpan<byte> data, out FrameHeader header)
    {
        header = default;

        if (data.Length < 4 || data[0] != 0xFF || (data[1] & 0xE0) != 0xE0)
        {
            return false;
        }

        int versionCode = (data[1] >> 3) & 0x03;
        int layerCode = (data[1] >> 1) & 0x03;
        bool hasCrc = (data[1] & 0x01) == 0;
        int bitrateIndex = data[2] >> 4;
        int sampleRateIndex = (data[2] >> 2) & 0x03;
        int padding = (data[2] >> 1) & 0x01;
        int channelMode = data[3] >> 6;

        // Version 1 and layer 0 are declared invalid by the format itself.
        if (versionCode == 1 || layerCode == 0 || sampleRateIndex == 3 || bitrateIndex is 0 or 15)
        {
            return false;
        }

        bool isMpeg1 = versionCode == 3;
        int layer = 4 - layerCode;

        int bitrateRow = isMpeg1 ? layer - 1 : (layer == 1 ? 3 : 4);
        int bitrate = Bitrates[bitrateRow][bitrateIndex] * 1000;
        int sampleRate = SampleRates[versionCode][sampleRateIndex];

        if (bitrate == 0 || sampleRate == 0)
        {
            return false;
        }

        int samplesPerFrame = layer switch
        {
            1 => 384,
            2 => 1152,
            _ => isMpeg1 ? 1152 : 576,
        };

        int length = layer == 1
            ? (((12 * bitrate / sampleRate) + padding) * 4)
            : ((samplesPerFrame / 8 * bitrate / sampleRate) + padding);

        if (length < 24)
        {
            return false;
        }

        bool isMono = channelMode == 3;
        int sideInfo = layer == 3
            ? (isMpeg1 ? (isMono ? 17 : 32) : (isMono ? 9 : 17))
            : 0;

        header = new FrameHeader(length, hasCrc, sideInfo, layer);
        return true;
    }

    private static uint ReadBigEndian(ReadOnlySpan<byte> data) =>
        ((uint)data[0] << 24) | ((uint)data[1] << 16) | ((uint)data[2] << 8) | data[3];

    /// <summary>A parsed frame header.</summary>
    /// <param name="Length">Total frame length in bytes.</param>
    /// <param name="SideInfoSize">Size of the side info before the audio.</param>
    /// <param name="Layer">MPEG layer: 1, 2 or 3.</param>
    private readonly record struct FrameHeader(int Length, bool HasCrc, int SideInfoSize, int Layer)
    {
        /// <summary>
        /// How many bytes after the checksum it covers. Layers I and II cover
        /// a different range, so their checksums are not verified.
        /// </summary>
        public int CrcCoverage => Layer == 3 ? SideInfoSize : 0;

        /// <summary>This frame's checksum can be verified.</summary>
        public bool CanCheckCrc => HasCrc && Layer == 3 && SideInfoSize > 0;
    }
}
