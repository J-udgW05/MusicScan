namespace MusicScanIntegrity.Core.Integrity;

/// <summary>
/// Validates the Ogg container (Vorbis, Opus, FLAC-in-Ogg) by page checksums.
/// </summary>
/// <remarks>
/// Ogg divides everything into pages, each carrying a CRC-32 over the whole
/// page. Verifying needs neither decompression nor knowledge of the stream
/// inside: pages are read in order, the checksum recomputed and compared. It
/// also reveals truncation — the last page of each stream must carry the
/// end-of-stream flag, and a missing one means the file was never finished.
/// </remarks>
internal sealed class OggValidator : IContainerValidator
{
    /// <summary>Page header without the segment table.</summary>
    private const int HeaderSize = 27;

    /// <summary>Largest possible page: header, table and 255 segments of 255 bytes.</summary>
    private const int MaxPageSize = HeaderSize + 255 + (255 * 255);

    /// <inheritdoc />
    public string Format => "Ogg";

    /// <inheritdoc />
    public bool Matches(ReadOnlySpan<byte> header) => header.StartsWith("OggS"u8);

    /// <inheritdoc />
    public ContainerValidation Validate(Stream stream, ContainerBounds bounds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        stream.Position = 0;
        StreamWindow window = new(stream, MaxPageSize + 1024);

        if (!window.Skip(bounds.AudioStart))
        {
            return ContainerValidation.Damaged(
                Format,
                "Файл обрывается внутри тега в начале.",
                $"Тег занимает {bounds.AudioStart} Б, а файл короче",
                truncated: true);
        }

        Dictionary<uint, StreamState> streams = [];
        int pages = 0;

        while (window.Position < bounds.AudioEnd)
        {
            cancellationToken.ThrowIfCancellationRequested();

            long pagePosition = window.Position;

            if (!window.Ensure(HeaderSize))
            {
                return ContainerValidation.Damaged(
                    Format,
                    "Файл обрывается на середине страницы.",
                    $"Смещение {pagePosition} Б, страниц проверено: {pages}",
                    pages,
                    pagePosition,
                    truncated: true);
            }

            ReadOnlySpan<byte> header = window.Peek(HeaderSize);

            if (!header.StartsWith("OggS"u8))
            {
                return ContainerValidation.Damaged(
                    Format,
                    pages == 0
                        ? "Файл не начинается страницей Ogg — заголовок разрушен."
                        : $"После страницы {pages} идут данные, которые страницей не являются.",
                    $"Смещение {pagePosition} Б, ожидалась подпись «OggS»",
                    pages,
                    pagePosition);
            }

            if (header[4] != 0)
            {
                return ContainerValidation.Damaged(
                    Format,
                    "Неизвестная версия формата Ogg — файл повреждён или собран неверно.",
                    $"Версия страницы {header[4]}, смещение {pagePosition} Б",
                    pages,
                    pagePosition);
            }

            int segments = header[26];

            if (!window.Ensure(HeaderSize + segments))
            {
                return ContainerValidation.Damaged(
                    Format,
                    "Файл обрывается на таблице отрезков страницы.",
                    $"Смещение {pagePosition} Б",
                    pages,
                    pagePosition,
                    truncated: true);
            }

            int payload = 0;
            ReadOnlySpan<byte> table = window.Peek(HeaderSize + segments)[HeaderSize..];
            foreach (byte length in table)
            {
                payload += length;
            }

            int pageSize = HeaderSize + segments + payload;

            if (!window.Ensure(pageSize))
            {
                return ContainerValidation.Damaged(
                    Format,
                    "Файл обрывается внутри страницы: данных меньше, чем обещано заголовком.",
                    $"Смещение {pagePosition} Б, нужно {pageSize} Б",
                    pages,
                    pagePosition,
                    truncated: true);
            }

            ReadOnlySpan<byte> page = window.Peek(pageSize);
            uint stored = ReadUInt32(page[22..26]);
            uint actual = ComputeCrc(page);

            if (stored != actual)
            {
                return ContainerValidation.Damaged(
                    Format,
                    $"Контрольная сумма страницы {pages + 1} не сошлась — файл повреждён.",
                    $"Смещение {pagePosition} Б, записано {stored:X8}, получилось {actual:X8}",
                    pages,
                    pagePosition,
                    damage: ContainerDamage.Checksum);
            }

            uint serial = ReadUInt32(page[14..18]);
            uint sequence = ReadUInt32(page[18..22]);
            byte flags = page[5];

            if (Track(streams, serial, sequence, flags, pagePosition) is { } orderFailure)
            {
                return orderFailure with { UnitsChecked = pages };
            }

            window.Advance(pageSize);
            pages++;
        }

        if (pages == 0)
        {
            return ContainerValidation.Damaged(
                Format,
                "В файле нет ни одной страницы Ogg.",
                "Файл пуст или это не Ogg",
                truncated: true);
        }

        // The end-of-stream flag sits on the last page of each stream. Its
        // absence means exactly one thing: the file was never finished.
        foreach ((uint serial, StreamState state) in streams)
        {
            if (!state.SawEnd)
            {
                return ContainerValidation.Damaged(
                    Format,
                    "Файл обрывается: у потока нет страницы с признаком конца.",
                    $"Поток {serial:X8}, страниц {state.Pages}",
                    pages,
                    truncated: true);
            }
        }

        return ContainerValidation.Verified(Format, pages);
    }

    /// <summary>Tracks page ordering within each stream.</summary>
    private ContainerValidation? Track(
        Dictionary<uint, StreamState> streams,
        uint serial,
        uint sequence,
        byte flags,
        long position)
    {
        bool isBegin = (flags & 0x02) != 0;
        bool isEnd = (flags & 0x04) != 0;

        if (!streams.TryGetValue(serial, out StreamState? state))
        {
            state = new StreamState();
            streams[serial] = state;

            if (!isBegin)
            {
                // The stream does not start at page one; the beginning is lost.
                return ContainerValidation.Damaged(
                    Format,
                    "Начало файла потеряно: поток начинается не с первой страницы.",
                    $"Поток {serial:X8}, номер страницы {sequence}, смещение {position} Б",
                    offset: position);
            }
        }
        else if (sequence != state.Expected)
        {
            return ContainerValidation.Damaged(
                Format,
                "Страницы идут не подряд — часть файла потеряна.",
                $"Поток {serial:X8}, ожидалась страница {state.Expected}, встретилась {sequence}",
                offset: position);
        }

        state.Expected = sequence + 1;
        state.Pages++;
        state.SawEnd |= isEnd;

        return null;
    }

    /// <summary>Computes the page CRC-32 with the stored checksum field zeroed.</summary>
    private static uint ComputeCrc(ReadOnlySpan<byte> page)
    {
        uint crc = 0;

        for (int i = 0; i < page.Length; i++)
        {
            // The four checksum bytes count as zero, or the result would
            // depend on what is already stored there.
            byte value = i is >= 22 and < 26 ? (byte)0 : page[i];
            crc = Crc.Ogg32Update(crc, value);
        }

        return crc;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data) =>
        (uint)(data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24));

    /// <summary>State of one logical stream inside the container.</summary>
    private sealed class StreamState
    {
        /// <summary>Sequence number expected next.</summary>
        public uint Expected { get; set; }

        /// <summary>How many pages of this stream were seen.</summary>
        public int Pages { get; set; }

        /// <summary>A page carrying the end-of-stream flag was seen.</summary>
        public bool SawEnd { get; set; }
    }
}
