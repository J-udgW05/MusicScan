namespace MusicScanIntegrity.Core.Integrity;

/// <summary>
/// Проверка FLAC его собственными контрольными суммами.
/// </summary>
/// <remarks>
/// <para>
/// У каждого кадра FLAC есть CRC-8 заголовка и CRC-16 всего кадра. Их сверка
/// отвечает на вопрос точно: файл совпадает с тем, что закодировали, или нет.
/// Декодер такого ответа не даёт — он говорит лишь «открылось».
/// </para>
/// <para>
/// Границу кадра в FLAC не записывают: она видна только по началу следующего.
/// Поэтому обход идёт так: разбирается заголовок, дальше ищется следующая
/// подпись кадра, и на каждой догадке о границе сверяется CRC-16. Совпадение
/// суммы и есть доказательство, что граница найдена верно — ложная подпись
/// внутри данных сумму не даст.
/// </para>
/// <para>
/// MD5 распакованного звука из <c>STREAMINFO</c> здесь не проверяется: для этого
/// нужно распаковать файл целиком и собрать отсчёты ровно в том виде, в каком их
/// считал кодировщик. Это работа декодера, а не разборщика.
/// </para>
/// </remarks>
internal sealed class FlacValidator : IContainerValidator
{
    /// <summary>Предел поиска границы кадра, когда размер не известен из заголовка.</summary>
    private const int MaxFrameScan = 1024 * 1024;

    /// <inheritdoc />
    public string Format => "FLAC";

    /// <inheritdoc />
    public bool Matches(ReadOnlySpan<byte> header) => header.StartsWith("fLaC"u8);

    /// <inheritdoc />
    public ContainerValidation Validate(Stream stream, ContainerBounds bounds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        long audioEnd = bounds.AudioEnd;
        stream.Position = 0;
        StreamWindow window = new(stream);

        if (!window.Skip(bounds.AudioStart))
        {
            return Truncated("Файл обрывается внутри тега в начале.", window.Position);
        }

        if (!window.Ensure(4) || !window.Peek(4).SequenceEqual("fLaC"u8))
        {
            return ContainerValidation.Damaged(
                Format,
                "Файл не начинается подписью FLAC — заголовок разрушен.",
                "Первые байты не равны «fLaC»",
                offset: window.Position);
        }

        window.Advance(4);

        StreamInfo? info = ReadMetadata(window, out ContainerValidation? metadataFailure);
        if (metadataFailure is not null)
        {
            return metadataFailure;
        }

        if (info is null)
        {
            return ContainerValidation.Damaged(
                Format,
                "В файле нет обязательного блока с описанием потока.",
                "Блок STREAMINFO не найден",
                offset: window.Position);
        }

        return WalkFrames(window, info, audioEnd, cancellationToken);
    }

    /// <summary>Читает блоки метаданных до первого помеченного как последний.</summary>
    private StreamInfo? ReadMetadata(StreamWindow window, out ContainerValidation? failure)
    {
        failure = null;
        StreamInfo? info = null;
        int blocks = 0;

        while (true)
        {
            if (!window.Ensure(4))
            {
                failure = Truncated("Файл обрывается на описании потока.", window.Position);
                return null;
            }

            ReadOnlySpan<byte> header = window.Peek(4);
            bool isLast = (header[0] & 0x80) != 0;
            int type = header[0] & 0x7F;
            int length = (header[1] << 16) | (header[2] << 8) | header[3];
            window.Advance(4);

            // Тип 127 объявлен недопустимым в самом формате.
            if (type == 127)
            {
                failure = ContainerValidation.Damaged(
                    Format,
                    "Описание потока повреждено: встретился недопустимый блок.",
                    "Тип блока метаданных 127",
                    offset: window.Position - 4);
                return null;
            }

            if (type == 0)
            {
                if (length < 34 || !window.Ensure(34))
                {
                    failure = Truncated("Описание потока обрывается.", window.Position);
                    return null;
                }

                info = ParseStreamInfo(window.Peek(34));
                window.Advance(34);

                if (!window.Skip(length - 34))
                {
                    failure = Truncated("Описание потока обрывается.", window.Position);
                    return null;
                }
            }
            else if (!window.Skip(length))
            {
                failure = Truncated("Файл обрывается внутри метаданных.", window.Position);
                return null;
            }

            blocks++;

            if (isLast)
            {
                return info;
            }

            // Разумный предел: в исправном файле блоков единицы, а бесконечный
            // цикл на разрушенном заголовке недопустим.
            if (blocks > 1024)
            {
                failure = ContainerValidation.Damaged(
                    Format,
                    "Описание потока повреждено: блоки не заканчиваются.",
                    "Более 1024 блоков метаданных подряд",
                    offset: window.Position);
                return null;
            }
        }
    }

    /// <summary>Обходит кадры, сверяя CRC-16 каждого.</summary>
    private ContainerValidation WalkFrames(
        StreamWindow window,
        StreamInfo info,
        long audioEnd,
        CancellationToken cancellationToken)
    {
        if (window.Position >= audioEnd)
        {
            return ContainerValidation.Damaged(
                Format,
                "В файле есть заголовок, но нет ни одного кадра со звуком.",
                $"Аудиоданные заканчиваются на {window.Position} Б",
                offset: window.Position,
                truncated: true);
        }

        int scanLimit = info.MaxFrameSize > 0
            ? Math.Min(MaxFrameScan, (info.MaxFrameSize * 2) + 64)
            : MaxFrameScan;

        int frames = 0;
        long samples = 0;
        long bytesInFrames = 0;

        while (window.Position < audioEnd)
        {
            cancellationToken.ThrowIfCancellationRequested();

            long framePosition = window.Position;
            long remaining = audioEnd - framePosition;
            int want = (int)Math.Min(scanLimit, remaining);

            window.Ensure(want);
            ReadOnlySpan<byte> span = window.Peek(want);

            if (!TryParseFrameHeader(span, out int headerLength, out int blockSize))
            {
                return ContainerValidation.Damaged(
                    Format,
                    frames == 0
                        ? "Первый кадр со звуком разрушен — файл не проиграется."
                        : $"Кадр {frames + 1} разрушен: на его месте не заголовок кадра.",
                    $"Смещение {framePosition} Б, ожидался заголовок кадра FLAC",
                    frames,
                    framePosition);
            }

            bool reachesEnd = span.Length >= remaining;
            int frameLength = FindFrameEnd(span, headerLength, reachesEnd, FrameCap(info, frames, bytesInFrames));

            if (frameLength <= 0)
            {
                // Отдельно назвать обрыв и мусор в хвосте нельзя: и то, и другое
                // выглядит как кадр, который не заканчивается там, где должен.
                return ContainerValidation.Damaged(
                    Format,
                    reachesEnd
                        ? $"Файл повреждён: кадр {frames + 1} не дописан до конца или в хвосте лишние данные."
                        : $"Контрольная сумма кадра {frames + 1} не сошлась — файл повреждён.",
                    $"Смещение {framePosition} Б, проверено кадров: {frames}, осталось {remaining} Б",
                    frames,
                    framePosition,
                    truncated: reachesEnd,
                    damage: ContainerDamage.Checksum);
            }

            window.Advance(frameLength);
            frames++;
            samples += blockSize;
            bytesInFrames += frameLength;
        }

        // Заявленное число отсчётов — второй, независимый признак обрыва:
        // суммы могут сойтись у всех кадров, которые остались в обрезанном файле.
        if (info.TotalSamples > 0 && samples < info.TotalSamples)
        {
            int lost = (int)Math.Round((info.TotalSamples - samples) / (double)Math.Max(1, info.SampleRate));

            return ContainerValidation.Damaged(
                Format,
                $"Файл обрывается: не хватает {Common.Format.Seconds(Math.Max(1, lost))} звука от заявленного.",
                $"Заявлено {info.TotalSamples} отсчётов, найдено {samples}",
                frames,
                truncated: true);
        }

        return ContainerValidation.Verified(Format, frames);
    }

    /// <summary>
    /// Предел длины кадра: заявленный в описании потока, а если там ноль —
    /// вчетверо больше среднего из уже прочитанных кадров.
    /// </summary>
    /// <remarks>
    /// Без предела нули, дописанные в конец файла, сходят за продолжение
    /// последнего кадра: сумма CRC-16, посчитанная по кадру вместе с его же
    /// суммой, равна нулю, и дописанные нули её не меняют. Заявленный размер
    /// кадра эту лазейку закрывает.
    /// </remarks>
    private static int FrameCap(StreamInfo info, int frames, long bytesInFrames)
    {
        if (info.MaxFrameSize > 0)
        {
            return info.MaxFrameSize;
        }

        return frames > 0 ? (int)Math.Min(MaxFrameScan, bytesInFrames / frames * 4) : 0;
    }

    /// <summary>
    /// Ищет конец кадра: перебирает подписи следующего кадра и проверяет по CRC-16,
    /// что граница именно здесь.
    /// </summary>
    /// <returns>Длина кадра или -1, если сумма нигде не сошлась.</returns>
    private static int FindFrameEnd(ReadOnlySpan<byte> span, int headerLength, bool reachesEnd, int cap)
    {
        // Минимум: заголовок, хоть какие-то данные и два байта суммы.
        int minimum = headerLength + 3;

        ushort crc = 0;
        ushort previous = 0;
        ushort beforePrevious = 0;

        for (int i = 0; i <= span.Length; i++)
        {
            if (cap > 0 && i > cap)
            {
                return -1;
            }

            if (i >= minimum)
            {
                bool boundary = i == span.Length
                    ? reachesEnd
                    : i + 1 < span.Length && span[i] == 0xFF && (span[i + 1] & 0xFE) == 0xF8;

                if (boundary)
                {
                    ushort stored = (ushort)((span[i - 2] << 8) | span[i - 1]);
                    if (stored == beforePrevious)
                    {
                        return i;
                    }
                }
            }

            if (i == span.Length)
            {
                break;
            }

            beforePrevious = previous;
            previous = crc;
            crc = Crc.Flac16Update(crc, span[i]);
        }

        return -1;
    }

    /// <summary>Разбирает заголовок кадра и сверяет его CRC-8.</summary>
    private static bool TryParseFrameHeader(ReadOnlySpan<byte> span, out int length, out int blockSize)
    {
        length = 0;
        blockSize = 0;

        if (span.Length < 6 || span[0] != 0xFF || (span[1] & 0xFE) != 0xF8)
        {
            return false;
        }

        int blockSizeCode = span[2] >> 4;
        int sampleRateCode = span[2] & 0x0F;
        int channelCode = span[3] >> 4;
        int sampleSizeCode = (span[3] >> 1) & 0x07;
        int reserved = span[3] & 0x01;

        if (blockSizeCode == 0 || sampleRateCode == 15 || channelCode > 10 || sampleSizeCode is 3 or 7 || reserved != 0)
        {
            return false;
        }

        int position = 4;
        if (!TrySkipCodedNumber(span, ref position))
        {
            return false;
        }

        switch (blockSizeCode)
        {
            case 6:
                if (span.Length <= position)
                {
                    return false;
                }

                blockSize = span[position] + 1;
                position += 1;
                break;

            case 7:
                if (span.Length <= position + 1)
                {
                    return false;
                }

                blockSize = ((span[position] << 8) | span[position + 1]) + 1;
                position += 2;
                break;

            default:
                blockSize = BlockSizeFromCode(blockSizeCode);
                break;
        }

        position += sampleRateCode switch
        {
            12 => 1,
            13 or 14 => 2,
            _ => 0,
        };

        if (span.Length <= position)
        {
            return false;
        }

        if (Crc.Flac8(span[..position]) != span[position])
        {
            return false;
        }

        length = position + 1;
        return true;
    }

    /// <summary>
    /// Пропускает номер кадра или отсчёта — он записан по правилам UTF-8,
    /// только длиной до семи байт.
    /// </summary>
    private static bool TrySkipCodedNumber(ReadOnlySpan<byte> span, ref int position)
    {
        if (span.Length <= position)
        {
            return false;
        }

        byte first = span[position];
        int extra;

        if (first < 0x80)
        {
            extra = 0;
        }
        else if (first < 0xC0)
        {
            // Байт-продолжение на месте первого — заголовок разрушен.
            return false;
        }
        else if (first < 0xE0)
        {
            extra = 1;
        }
        else if (first < 0xF0)
        {
            extra = 2;
        }
        else if (first < 0xF8)
        {
            extra = 3;
        }
        else if (first < 0xFC)
        {
            extra = 4;
        }
        else if (first < 0xFE)
        {
            extra = 5;
        }
        else if (first == 0xFE)
        {
            extra = 6;
        }
        else
        {
            return false;
        }

        if (span.Length <= position + extra)
        {
            return false;
        }

        for (int i = 1; i <= extra; i++)
        {
            if ((span[position + i] & 0xC0) != 0x80)
            {
                return false;
            }
        }

        position += extra + 1;
        return true;
    }

    private static int BlockSizeFromCode(int code) => code switch
    {
        1 => 192,
        >= 2 and <= 5 => 576 << (code - 2),
        >= 8 and <= 15 => 256 << (code - 8),
        _ => 0,
    };

    private static StreamInfo ParseStreamInfo(ReadOnlySpan<byte> block)
    {
        int minFrame = (block[4] << 16) | (block[5] << 8) | block[6];
        int maxFrame = (block[7] << 16) | (block[8] << 8) | block[9];

        // Двадцать бит частоты, три канала, пять разрядности и тридцать шесть
        // отсчётов упакованы подряд — читаем их одним 64-битным словом.
        ulong packed = 0;
        for (int i = 10; i < 18; i++)
        {
            packed = (packed << 8) | block[i];
        }

        return new StreamInfo(
            minFrame,
            maxFrame,
            (int)(packed >> 44),
            (int)((packed >> 41) & 0x7) + 1,
            (int)((packed >> 36) & 0x1F) + 1,
            (long)(packed & 0xF_FFFF_FFFF));
    }

    private ContainerValidation Truncated(string message, long offset) =>
        ContainerValidation.Damaged(Format, message, $"Смещение {offset} Б", offset: offset, truncated: true);

    /// <summary>Разбор блока STREAMINFO.</summary>
    private sealed record StreamInfo(
        int MinFrameSize,
        int MaxFrameSize,
        int SampleRate,
        int Channels,
        int BitsPerSample,
        long TotalSamples);
}
