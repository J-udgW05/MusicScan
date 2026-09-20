namespace MusicScanIntegrity.Core.Integrity;

/// <summary>
/// Sizes of the tag blocks at the start and end of a file.
/// </summary>
/// <remarks>
/// Tags belong to the file but not to the audio stream. Ignoring them would
/// make a leading ID3 look like garbage before the first frame and a trailing
/// APEv2 like leftovers after the last, so the bounds are measured before the
/// frames are walked.
/// </remarks>
internal static class TagBoundaries
{
    /// <summary>Length of a leading ID3v2 tag including its header; 0 if absent.</summary>
    /// <param name="header">First bytes of the file; at least ten are needed.</param>
    public static long LeadingId3(ReadOnlySpan<byte> header)
    {
        if (header.Length < 10 || header[0] != 'I' || header[1] != 'D' || header[2] != '3')
        {
            return 0;
        }

        // The size is synch-safe: only the low seven bits of each byte count.
        long size = ((long)(header[6] & 0x7F) << 21)
            | ((long)(header[7] & 0x7F) << 14)
            | ((long)(header[8] & 0x7F) << 7)
            | (long)(header[9] & 0x7F);

        bool hasFooter = (header[5] & 0x10) != 0;
        return 10 + size + (hasFooter ? 10 : 0);
    }

    /// <summary>
    /// Size of the trailing tag block: ID3v1, APEv2 or both.
    /// </summary>
    /// <param name="stream">Seekable stream.</param>
    public static long TrailingTags(Stream stream, long fileLength)
    {
        if (!stream.CanSeek)
        {
            return 0;
        }

        long savedPosition = stream.Position;
        long trailing = 0;

        try
        {
            // Tags sit back to back in no fixed order, so they are peeled off
            // one at a time while a known one is found at the end.
            while (true)
            {
                long end = fileLength - trailing;

                if (end >= 128 && ReadTag(stream, end - 128, 3) is [(byte)'T', (byte)'A', (byte)'G'])
                {
                    trailing += 128;
                    continue;
                }

                if (end >= 32)
                {
                    byte[] footer = ReadTag(stream, end - 32, 32);
                    if (footer.Length == 32 && IsApeFooter(footer))
                    {
                        long size = BitConverter.ToUInt32(footer, 12);
                        uint flags = BitConverter.ToUInt32(footer, 20);

                        // Flag bit 31 means the tag also carries a header.
                        long total = size + ((flags & 0x80000000) != 0 ? 32 : 0);

                        if (total > 0 && total <= end)
                        {
                            trailing += total;
                            continue;
                        }
                    }
                }

                return trailing;
            }
        }
        catch (IOException)
        {
            return trailing;
        }
        finally
        {
            stream.Position = savedPosition;
        }
    }

    private static bool IsApeFooter(ReadOnlySpan<byte> footer) =>
        footer.StartsWith("APETAGEX"u8);

    private static byte[] ReadTag(Stream stream, long offset, int count)
    {
        if (offset < 0)
        {
            return [];
        }

        stream.Position = offset;
        byte[] buffer = new byte[count];
        int read = stream.ReadAtLeast(buffer, count, throwOnEndOfStream: false);

        return read == count ? buffer : [];
    }
}
