using System.Buffers.Binary;
using MusicScanIntegrity.Core.Integrity;

namespace MusicScanIntegrity.Core.Tests.Integrity;

/// <summary>
/// Сборка файлов остальных форматов побайтно — исправных и испорченных.
/// </summary>
/// <remarks>
/// Разборщики читают заголовки, длины и суммы, а не звук, поэтому вместо
/// настоящих записей в кадры кладётся заполнитель. Так проверка не зависит ни
/// от кодировщиков, ни от файлов, лежащих в репозитории.
/// </remarks>
internal static class SyntheticFiles
{
    // ── Ogg ──────────────────────────────────────────────────────────────

    /// <summary>Собирает файл Ogg из страниц с правильными суммами.</summary>
    /// <param name="pages">Сколько страниц положить.</param>
    /// <param name="withEndFlag">Ставить ли последней странице признак конца потока.</param>
    /// <param name="skipSequence">Пропустить один номер страницы — как будто кусок потерян.</param>
    /// <returns>Содержимое файла.</returns>
    public static byte[] Ogg(int pages = 3, bool withEndFlag = true, bool skipSequence = false)
    {
        List<byte> file = [];
        uint sequence = 0;

        for (int i = 0; i < pages; i++)
        {
            byte flags = 0;
            if (i == 0)
            {
                flags |= 0x02;
            }

            if (i == pages - 1 && withEndFlag)
            {
                flags |= 0x04;
            }

            file.AddRange(OggPage(flags, sequence, payloadSize: 100 + i));
            sequence += skipSequence && i == 0 ? 2u : 1u;
        }

        return [.. file];
    }

    /// <summary>Смещение начала страницы с указанным номером.</summary>
    public static int OggPageOffset(int index)
    {
        int offset = 0;
        for (int i = 0; i < index; i++)
        {
            offset += 28 + 100 + i;
        }

        return offset;
    }

    private static byte[] OggPage(byte flags, uint sequence, int payloadSize)
    {
        // Одна страница, один отрезок — отрезок не длиннее 255 байт.
        byte[] page = new byte[27 + 1 + payloadSize];

        "OggS"u8.CopyTo(page);
        page[4] = 0;
        page[5] = flags;
        BinaryPrimitives.WriteUInt64LittleEndian(page.AsSpan(6), sequence);
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(14), 0x0BADC0DE);
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(18), sequence);
        page[26] = 1;
        page[27] = (byte)payloadSize;

        for (int i = 0; i < payloadSize; i++)
        {
            page[28 + i] = (byte)((i * 5) + sequence);
        }

        uint crc = 0;
        for (int i = 0; i < page.Length; i++)
        {
            byte value = i is >= 22 and < 26 ? (byte)0 : page[i];
            crc = Crc.Ogg32Update(crc, value);
        }

        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(22), crc);
        return page;
    }

    // ── MP3 ──────────────────────────────────────────────────────────────

    /// <summary>Длина кадра MPEG-1 Layer III, 128 кбит/с, 44,1 кГц.</summary>
    public const int Mp3FrameLength = 417;

    /// <summary>Собирает файл MP3 из кадров.</summary>
    /// <param name="frames">Сколько кадров положить.</param>
    /// <param name="withCrc">Сделать кадры защищёнными: с контрольной суммой.</param>
    /// <param name="declaredFrames">Записать в заголовок Info другое число кадров.</param>
    /// <param name="leadingId3">Добавить тег ID3v2 в начало.</param>
    /// <returns>Содержимое файла.</returns>
    public static byte[] Mp3(
        int frames = 5,
        bool withCrc = false,
        int? declaredFrames = null,
        bool leadingId3 = false)
    {
        List<byte> file = [];

        if (leadingId3)
        {
            byte[] tag = new byte[10 + 100];
            "ID3"u8.CopyTo(tag);
            tag[3] = 3;
            tag[9] = 100;
            file.AddRange(tag);
        }

        for (int i = 0; i < frames; i++)
        {
            bool first = i == 0;
            file.AddRange(Mp3Frame(i, withCrc, first && declaredFrames is not null ? declaredFrames : null));
        }

        return [.. file];
    }

    /// <summary>Смещение кадра с указанным номером.</summary>
    public static int Mp3FrameOffset(int index, bool leadingId3 = false) =>
        (leadingId3 ? 110 : 0) + (index * Mp3FrameLength);

    private static byte[] Mp3Frame(int number, bool withCrc, int? declaredFrames)
    {
        byte[] frame = new byte[Mp3FrameLength];

        frame[0] = 0xFF;

        // 1111 1011: MPEG-1, слой III; младший бит — «защиты нет».
        frame[1] = (byte)(withCrc ? 0xFA : 0xFB);

        // Битрейт 128 кбит/с (индекс 9), частота 44,1 кГц (индекс 0), без набивки.
        frame[2] = 0x90;
        frame[3] = 0x00;

        int sideInfo = 32;
        int payloadStart = 4 + (withCrc ? 2 : 0);

        for (int i = payloadStart; i < frame.Length; i++)
        {
            // Заполнитель без 0xFF: он начинает подпись кадра.
            frame[i] = (byte)(((i * 3) + number) & 0xFE);
        }

        if (declaredFrames is { } count)
        {
            int marker = payloadStart + sideInfo;
            "Info"u8.CopyTo(frame.AsSpan(marker));
            BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(marker + 4), 1);
            BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(marker + 8), (uint)count);
        }

        if (withCrc)
        {
            Span<byte> covered = stackalloc byte[2 + sideInfo];
            frame.AsSpan(2, 2).CopyTo(covered);
            frame.AsSpan(6, sideInfo).CopyTo(covered[2..]);

            ushort crc = Crc.Mpeg16(covered);
            frame[4] = (byte)(crc >> 8);
            frame[5] = (byte)(crc & 0xFF);
        }

        return frame;
    }

    // ── MP4 ──────────────────────────────────────────────────────────────

    /// <summary>Собирает файл MP4 из обязательных блоков.</summary>
    /// <param name="mediaBytes">Сколько байт звука положить в блок mdat.</param>
    /// <param name="declaredMediaBytes">Что записать в длину блока mdat; по умолчанию — настоящую.</param>
    /// <param name="withMovie">Класть ли блок moov с описанием дорожек.</param>
    /// <returns>Содержимое файла.</returns>
    public static byte[] Mp4(int mediaBytes = 256, int? declaredMediaBytes = null, bool withMovie = true)
    {
        List<byte> file = [];
        file.AddRange(Box("ftyp", "M4A "u8.ToArray()));

        if (withMovie)
        {
            file.AddRange(Box("moov", new byte[64]));
        }

        byte[] media = new byte[mediaBytes];
        for (int i = 0; i < media.Length; i++)
        {
            media[i] = (byte)i;
        }

        byte[] mdat = Box("mdat", media);

        if (declaredMediaBytes is { } declared)
        {
            BinaryPrimitives.WriteUInt32BigEndian(mdat.AsSpan(0), (uint)(declared + 8));
        }

        file.AddRange(mdat);
        return [.. file];
    }

    private static byte[] Box(string type, byte[] payload)
    {
        byte[] box = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        System.Text.Encoding.ASCII.GetBytes(type).CopyTo(box, 4);
        payload.CopyTo(box, 8);
        return box;
    }

    // ── WAV ──────────────────────────────────────────────────────────────

    /// <summary>Собирает файл WAV.</summary>
    /// <param name="dataBytes">Сколько байт отсчётов положить.</param>
    /// <param name="declaredDataBytes">Что записать в длину части data.</param>
    /// <param name="withFormat">Класть ли часть fmt с описанием формата.</param>
    /// <returns>Содержимое файла.</returns>
    public static byte[] Wav(int dataBytes = 512, int? declaredDataBytes = null, bool withFormat = true)
    {
        List<byte> body = [];

        if (withFormat)
        {
            byte[] format = new byte[16];
            BinaryPrimitives.WriteUInt16LittleEndian(format.AsSpan(0), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(format.AsSpan(2), 2);
            BinaryPrimitives.WriteUInt32LittleEndian(format.AsSpan(4), 44100);
            BinaryPrimitives.WriteUInt32LittleEndian(format.AsSpan(8), 44100 * 4);
            BinaryPrimitives.WriteUInt16LittleEndian(format.AsSpan(12), 4);
            BinaryPrimitives.WriteUInt16LittleEndian(format.AsSpan(14), 16);
            body.AddRange(Chunk("fmt ", format, null));
        }

        body.AddRange(Chunk("data", new byte[dataBytes], declaredDataBytes));

        List<byte> file = [];
        file.AddRange("RIFF"u8.ToArray());
        file.AddRange(BitConverter.GetBytes((uint)(4 + body.Count)));
        file.AddRange("WAVE"u8.ToArray());
        file.AddRange(body);

        return [.. file];
    }

    private static byte[] Chunk(string id, byte[] payload, int? declaredSize)
    {
        byte[] chunk = new byte[8 + payload.Length];
        System.Text.Encoding.ASCII.GetBytes(id).CopyTo(chunk, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), (uint)(declaredSize ?? payload.Length));
        payload.CopyTo(chunk, 8);
        return chunk;
    }

    // ── WavPack ──────────────────────────────────────────────────────────

    /// <summary>Собирает файл WavPack из блоков.</summary>
    /// <param name="blocks">Сколько блоков положить.</param>
    /// <param name="payloadBytes">Размер данных в блоке.</param>
    /// <param name="declaredExtra">Прибавить к объявленной длине последнего блока лишнее.</param>
    /// <returns>Содержимое файла.</returns>
    public static byte[] WavPack(int blocks = 3, int payloadBytes = 64, int declaredExtra = 0)
    {
        List<byte> file = [];

        for (int i = 0; i < blocks; i++)
        {
            byte[] block = new byte[32 + payloadBytes];
            "wvpk"u8.CopyTo(block);

            int declared = block.Length - 8 + (i == blocks - 1 ? declaredExtra : 0);
            BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(4), (uint)declared);
            BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(8), 0x0410);

            file.AddRange(block);
        }

        return [.. file];
    }

    // ── APE ──────────────────────────────────────────────────────────────

    /// <summary>Собирает файл Monkey's Audio.</summary>
    /// <param name="audioBytes">Сколько байт сжатого звука объявить.</param>
    /// <param name="actualBytes">Сколько байт положить на самом деле; по умолчанию — столько же.</param>
    /// <returns>Содержимое файла.</returns>
    public static byte[] Ape(int audioBytes = 512, int? actualBytes = null)
    {
        int descriptor = 52;
        int header = 24;
        byte[] file = new byte[descriptor + header + (actualBytes ?? audioBytes)];

        "MAC "u8.CopyTo(file);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(4), 3990);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(8), (uint)descriptor);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(12), (uint)header);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(16), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(20), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(24), (uint)audioBytes);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(28), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(32), 0);

        return file;
    }

    // ── Теги ─────────────────────────────────────────────────────────────

    /// <summary>Тег ID3v2 для начала файла.</summary>
    /// <param name="payloadBytes">Размер содержимого тега без десяти байт заголовка.</param>
    /// <param name="declaredPayloadBytes">
    /// Что записать в поле размера; по умолчанию — настоящий размер. Другое
    /// значение делает тег заведомо испорченным.
    /// </param>
    /// <returns>Байты тега.</returns>
    /// <remarks>
    /// Размер записан «синхробезопасно»: в каждом из четырёх байт значащие
    /// только младшие семь бит, поэтому больше 127 в один байт не положить.
    /// </remarks>
    public static byte[] LeadingId3(int payloadBytes = 100, int? declaredPayloadBytes = null)
    {
        int declared = declaredPayloadBytes ?? payloadBytes;
        ArgumentOutOfRangeException.ThrowIfGreaterThan(declared, 0x0FFFFFFF);

        byte[] tag = new byte[10 + payloadBytes];
        "ID3"u8.CopyTo(tag);
        tag[3] = 3;
        tag[6] = (byte)((declared >> 21) & 0x7F);
        tag[7] = (byte)((declared >> 14) & 0x7F);
        tag[8] = (byte)((declared >> 7) & 0x7F);
        tag[9] = (byte)(declared & 0x7F);

        return tag;
    }

    /// <summary>Тег ID3v1 для конца файла — ровно 128 байт.</summary>
    /// <returns>Байты тега.</returns>
    public static byte[] TrailingId3v1()
    {
        byte[] tag = new byte[128];
        "TAG"u8.CopyTo(tag);
        "Название"u8.CopyTo(tag.AsSpan(3));
        return tag;
    }

    /// <summary>Тег APEv2 для конца файла: только концевик, без заголовка.</summary>
    /// <returns>Байты тега.</returns>
    public static byte[] TrailingApev2()
    {
        byte[] tag = new byte[32];
        "APETAGEX"u8.CopyTo(tag);
        BinaryPrimitives.WriteUInt32LittleEndian(tag.AsSpan(8), 2000);

        // Длина тега считается вместе с концевиком, но без заголовка.
        BinaryPrimitives.WriteUInt32LittleEndian(tag.AsSpan(12), 32);
        BinaryPrimitives.WriteUInt32LittleEndian(tag.AsSpan(16), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(tag.AsSpan(20), 0);

        return tag;
    }
}
