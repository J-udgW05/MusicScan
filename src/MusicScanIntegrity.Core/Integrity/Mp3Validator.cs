namespace MusicScanIntegrity.Core.Integrity;

/// <summary>
/// Проверка MP3: обход кадров, сверка их длин и контрольных сумм там, где они есть.
/// </summary>
/// <remarks>
/// <para>
/// В MP3 нет общей контрольной суммы файла. Есть длина каждого кадра, посчитанная
/// из его заголовка: если она верна, следующий кадр начинается ровно там, где
/// обещано. Разрыв этой цепочки — надёжный признак повреждения, и находит он
/// именно то, что чаще всего случается: недокачанный файл и мусор в середине.
/// </para>
/// <para>
/// Часть кадров бывает «защищённой» — тогда в кадре лежит CRC-16 по заголовку и
/// служебным данным, и её можно сверить по-настоящему. Такие кадры считаются
/// отдельно: файл, где сумма сошлась, проверен строже, чем тот, где сверять
/// было нечего.
/// </para>
/// <para>
/// Заголовок Xing/Info в начале хранит число кадров. Если их меньше обещанного,
/// файл обрывается — даже когда все оставшиеся кадры целы.
/// </para>
/// </remarks>
internal sealed class Mp3Validator : IContainerValidator
{
    /// <summary>Сколько мусора между кадрами считается ещё допустимым.</summary>
    /// <remarks>
    /// Ноль тут не годится: в реальных файлах между тегом и первым кадром или
    /// после последнего кадра попадаются короткие огрызки, а объявлять из-за
    /// них файл повреждённым — ложная тревога.
    /// </remarks>
    private const int JunkTolerance = 2048;

    private static readonly int[][] Bitrates =
    [
        // MPEG 1: слой I, слой II, слой III
        [0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448, 0],
        [0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384, 0],
        [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0],

        // MPEG 2 и 2.5: слой I, слои II и III
        [0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256, 0],
        [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0],
    ];

    private static readonly int[][] SampleRates =
    [
        [11025, 12000, 8000, 0],   // MPEG 2.5
        [0, 0, 0, 0],              // зарезервировано
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
                "Файл обрывается внутри тега в начале.",
                $"Тег занимает {bounds.AudioStart} Б, а файл короче",
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
                // Хвост короче заголовка кадра — это огрызок, а не кадр.
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
                    $"Файл обрывается на кадре {frames + 1}: он начат, но не дописан.",
                    $"Смещение {framePosition} Б, нужно {header.Length} Б, осталось {remaining} Б",
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
                        $"Контрольная сумма кадра {frames + 1} не сошлась — файл повреждён.",
                        $"Смещение {framePosition} Б",
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
                "В файле нет ни одного кадра MP3.",
                $"Просмотрено {junkBytes} Б без единого заголовка кадра",
                truncated: true);
        }

        if (junkBytes > JunkTolerance)
        {
            return ContainerValidation.Damaged(
                Format,
                "Между кадрами найден мусор — файл повреждён или склеен из кусков.",
                $"Лишних байт: {junkBytes}, кадров: {frames}",
                frames,
                firstErrorOffset < 0 ? null : firstErrorOffset);
        }

        // Заголовок Xing/Info пишет число кадров вместе с собой, поэтому
        // сравниваем с ним же — с точностью до одного кадра.
        if (declaredFrames > 0 && frames < declaredFrames - 1)
        {
            return ContainerValidation.Damaged(
                Format,
                $"Файл обрывается: обещано кадров {declaredFrames}, найдено {frames}.",
                $"Заголовок Xing/Info объявляет {declaredFrames} кадров",
                frames,
                truncated: true);
        }

        return checkedSums > 0
            ? ContainerValidation.Verified(Format, frames) with
            {
                TechnicalDetail = $"Кадров {frames}, с проверенной суммой {checkedSums}",
            }
            : ContainerValidation.StructureOnly(
                Format,
                frames,
                $"Кадров {frames}; контрольных сумм в файле нет — проверена только цепочка кадров");
    }

    /// <summary>Читает число кадров из заголовка Xing/Info, если он есть.</summary>
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

        // Младший бит флагов означает, что дальше записано число кадров.
        return (flags & 0x01) == 0 ? 0 : (int)ReadBigEndian(frame[(offset + 8)..]);
    }

    private static bool CrcMatches(ReadOnlySpan<byte> frame, FrameHeader header)
    {
        ushort stored = (ushort)((frame[4] << 8) | frame[5]);

        // Сумма считается по двум последним байтам заголовка и служебным данным,
        // но не по самой сумме — её в подсчёт не берут.
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

        // Значение 1 у версии и 0 у слоя объявлены недопустимыми в самом формате.
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

    /// <summary>Разобранный заголовок кадра.</summary>
    /// <param name="Length">Полная длина кадра в байтах.</param>
    /// <param name="HasCrc">В кадре есть контрольная сумма.</param>
    /// <param name="SideInfoSize">Размер служебных данных перед звуком.</param>
    /// <param name="Layer">Слой MPEG: 1, 2 или 3.</param>
    private readonly record struct FrameHeader(int Length, bool HasCrc, int SideInfoSize, int Layer)
    {
        /// <summary>
        /// Сколько байт после суммы она покрывает. Для слоёв I и II состав
        /// покрытия другой, поэтому там сумма не сверяется.
        /// </summary>
        public int CrcCoverage => Layer == 3 ? SideInfoSize : 0;

        /// <summary>Сумму этого кадра можно сверить.</summary>
        public bool CanCheckCrc => HasCrc && Layer == 3 && SideInfoSize > 0;
    }
}
