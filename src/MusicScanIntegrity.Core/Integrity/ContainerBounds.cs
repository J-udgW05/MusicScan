namespace MusicScanIntegrity.Core.Integrity;

/// <summary>
/// Where the audio data sits inside a file, excluding leading and trailing tags.
/// </summary>
/// <remarks>
/// <para>
/// Tags are a legitimate part of a file but not of the stream. A validator
/// measuring from byte zero would read a leading ID3 as a broken header and a
/// trailing APEv2 as garbage after the last frame — both false alarms on a
/// healthy file, which is the worst thing this program can say.
/// </para>
/// <para>
/// Computed once, before a validator is chosen: every format locates its tags
/// the same way, and repeating it per validator would mean six copies that
/// eventually drift apart.
/// </para>
/// </remarks>
/// <param name="AudioStart">First byte after the leading tags.</param>
/// <param name="AudioEnd">First byte of the trailing tags, or the end of the file.</param>
internal readonly record struct ContainerBounds(long FileLength, long AudioStart, long AudioEnd)
{
    /// <summary>Length of the stream itself.</summary>
    public long AudioLength => AudioEnd - AudioStart;

    /// <summary>Bounds spanning the whole file, when tags need not be found.</summary>
    public static ContainerBounds Whole(long fileLength) => new(fileLength, 0, fileLength);

    /// <summary>Locates the audio data in an open file.</summary>
    /// <param name="stream">Seekable stream; the position is restored.</param>
    /// <returns>The bounds, or the whole file when the values look wrong.</returns>
    public static ContainerBounds Measure(Stream stream, long fileLength)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (fileLength <= 0 || !stream.CanSeek)
        {
            return Whole(Math.Max(0, fileLength));
        }

        long savedPosition = stream.Position;

        try
        {
            long start = LeadingTagLength(stream, fileLength);
            long end = fileLength - TagBoundaries.TrailingTags(stream, fileLength);

            // A tag claiming to be larger than the file is damage in itself,
            // and diagnosing it belongs to the validator rather than here.
            // Hand over the whole file and let it say what is wrong.
            if (start < 0 || end <= start || start >= fileLength || end > fileLength)
            {
                return Whole(fileLength);
            }

            return new ContainerBounds(fileLength, start, end);
        }
        catch (IOException)
        {
            return Whole(fileLength);
        }
        finally
        {
            stream.Position = savedPosition;
        }
    }

    private static long LeadingTagLength(Stream stream, long fileLength)
    {
        if (fileLength < 10)
        {
            return 0;
        }

        stream.Position = 0;
        Span<byte> header = stackalloc byte[10];

        return stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length
            ? 0
            : TagBoundaries.LeadingId3(header);
    }
}
