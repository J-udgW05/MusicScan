namespace MusicScanIntegrity.Core.Integrity;

/// <summary>
/// Границы тегов в начале и в конце файла.
/// </summary>
/// <remarks>
/// Теги — законная часть файла, но не часть аудиопотока. Без их учёта разборщик
/// принял бы ID3 в начале за мусор перед первым кадром, а APEv2 в конце — за
/// хвост после последнего. Оба вывода были бы ложной тревогой, поэтому границы
/// считаются до обхода кадров.
/// </remarks>
internal static class TagBoundaries
{
    /// <summary>Длина тега ID3v2 в начале файла; 0, если его там нет.</summary>
    /// <param name="header">Первые байты файла (нужно не меньше десяти).</param>
    /// <returns>Сколько байт занимает тег вместе с заголовком.</returns>
    public static long LeadingId3(ReadOnlySpan<byte> header)
    {
        if (header.Length < 10 || header[0] != 'I' || header[1] != 'D' || header[2] != '3')
        {
            return 0;
        }

        // Размер записан «синхробезопасно»: в каждом байте значащие только младшие семь бит.
        long size = ((long)(header[6] & 0x7F) << 21)
            | ((long)(header[7] & 0x7F) << 14)
            | ((long)(header[8] & 0x7F) << 7)
            | (long)(header[9] & 0x7F);

        bool hasFooter = (header[5] & 0x10) != 0;
        return 10 + size + (hasFooter ? 10 : 0);
    }

    /// <summary>
    /// Сколько байт в конце файла занимают теги: ID3v1, APEv2 и их сочетание.
    /// </summary>
    /// <param name="stream">Поток с возможностью перемотки.</param>
    /// <param name="fileLength">Длина файла.</param>
    /// <returns>Размер хвоста из тегов.</returns>
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
            // Теги идут вплотную друг к другу, порядок не задан жёстко —
            // поэтому снимаем их по одному, пока с конца находится знакомый.
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

                        // Бит 31 флагов означает, что у тега есть ещё и заголовок.
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
