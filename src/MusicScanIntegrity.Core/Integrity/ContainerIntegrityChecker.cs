namespace MusicScanIntegrity.Core.Integrity;

/// <summary>
/// Проверка файла его собственными средствами: контрольными суммами формата и
/// целостностью контейнера.
/// </summary>
public interface IContainerIntegrityChecker
{
    /// <summary>Проверяет файл, выбрав разборщик по его содержимому.</summary>
    /// <param name="filePath">Путь к файлу.</param>
    /// <param name="cancellationToken">Отмена: стоп или таймаут по файлу.</param>
    /// <returns>Вердикт; для незнакомых форматов — «разборщика нет».</returns>
    ContainerValidation Check(string filePath, CancellationToken cancellationToken);
}

/// <inheritdoc cref="IContainerIntegrityChecker" />
/// <remarks>
/// Формат определяется по содержимому, а не по расширению: файл с именем
/// «.flac» и данными MP3 внутри должен проверяться правилами MP3, иначе вердикт
/// будет о несуществующем повреждении.
/// </remarks>
public sealed class ContainerIntegrityChecker : IContainerIntegrityChecker
{
    /// <summary>
    /// Разборщики в порядке проверки. MP3 идёт последним: его подпись — всего
    /// одиннадцать единичных бит, и она случайно встречается в начале других
    /// форматов чаще, чем хотелось бы.
    /// </summary>
    private static readonly IContainerValidator[] Validators =
    [
        new FlacValidator(),
        new OggValidator(),
        new Mp4Validator(),
        new RiffValidator(),
        new WavPackValidator(),
        new ApeValidator(),
        new Mp3Validator(),
    ];

    /// <summary>Сколько байт читается для опознания формата.</summary>
    private const int HeaderSize = 16;

    /// <inheritdoc />
    public ContainerValidation Check(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            using FileStream stream = new(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                64 * 1024,
                FileOptions.SequentialScan);

            long length = stream.Length;

            if (length < HeaderSize)
            {
                return ContainerValidation.NotSupported("неизвестный");
            }

            ContainerBounds bounds = ContainerBounds.Measure(stream, length);
            IContainerValidator? validator = SelectValidator(stream, bounds);

            if (validator is null)
            {
                return ContainerValidation.NotSupported("неизвестный");
            }

            return validator.Validate(stream, bounds, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return ContainerValidation.Unreadable("неизвестный", $"{ex.GetType().Name} · {ex.Message}");
        }
    }

    /// <summary>Подбирает разборщик по подписи в начале аудиоданных.</summary>
    /// <remarks>
    /// Подпись ищется там же, откуда потом читает разборщик, — за тегом, а не с
    /// нулевого байта. Иначе формат опознавался бы по одному месту, а разбирался
    /// с другого, и исправный файл с тегом объявлялся бы разрушенным.
    /// </remarks>
    private static IContainerValidator? SelectValidator(FileStream stream, ContainerBounds bounds)
    {
        byte[] header = new byte[HeaderSize];
        stream.Position = bounds.AudioStart;

        IContainerValidator? found = stream.ReadAtLeast(header, HeaderSize, throwOnEndOfStream: false) == HeaderSize
            ? Find(header)
            : null;

        stream.Position = 0;
        return found;
    }

    private static IContainerValidator? Find(ReadOnlySpan<byte> header)
    {
        foreach (IContainerValidator validator in Validators)
        {
            if (validator.Matches(header))
            {
                return validator;
            }
        }

        return null;
    }
}
