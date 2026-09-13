namespace MusicScanIntegrity.Core.Integrity;

/// <summary>
/// Где внутри файла лежат сами аудиоданные: без тегов в начале и в конце.
/// </summary>
/// <remarks>
/// <para>
/// Теги — законная часть файла, но не часть потока. Разборщик, который считает
/// границы от нулевого байта, принял бы ID3 в начале за разрушенный заголовок,
/// а APEv2 в конце — за мусор после последнего кадра. И то, и другое — ложная
/// тревога на исправном файле, а это худшее, что программа может сказать.
/// </para>
/// <para>
/// Считается один раз, до выбора разборщика: границы у всех форматов ищутся
/// одинаково, и повторять это в каждом разборщике значило бы держать шесть
/// копий одного кода, часть из которых рано или поздно разойдётся.
/// </para>
/// </remarks>
/// <param name="FileLength">Полный размер файла на диске.</param>
/// <param name="AudioStart">Первый байт после тегов в начале.</param>
/// <param name="AudioEnd">Первый байт тегов в конце; при их отсутствии — конец файла.</param>
internal readonly record struct ContainerBounds(long FileLength, long AudioStart, long AudioEnd)
{
    /// <summary>Сколько байт занимает сам поток.</summary>
    public long AudioLength => AudioEnd - AudioStart;

    /// <summary>Границы файла целиком — когда теги искать незачем.</summary>
    /// <param name="fileLength">Размер файла.</param>
    /// <returns>Границы от нуля до конца.</returns>
    public static ContainerBounds Whole(long fileLength) => new(fileLength, 0, fileLength);

    /// <summary>Находит границы аудиоданных в открытом файле.</summary>
    /// <param name="stream">Поток с возможностью перемотки; позиция восстанавливается.</param>
    /// <param name="fileLength">Размер файла.</param>
    /// <returns>Границы; при неожиданных значениях — файл целиком.</returns>
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

            // Тег, объявивший себя больше файла, — сам по себе повреждение, и
            // разбирать такой файл должен разборщик, а не эта мерка. Отдаём
            // файл целиком: пусть он и скажет, что именно не так.
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
