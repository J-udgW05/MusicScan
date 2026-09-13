namespace MusicScanIntegrity.Core.Integrity;

/// <summary>
/// Разборщик одного семейства форматов: читает файл его собственными правилами
/// и говорит, сошлись ли контрольные суммы.
/// </summary>
/// <remarks>
/// Это не декодирование: звук не восстанавливается, читаются только заголовки,
/// границы кадров и суммы. Поэтому проверка упирается в скорость диска, а не
/// в процессор, и даёт точный ответ там, где декодер даёт лишь «открылось».
/// </remarks>
internal interface IContainerValidator
{
    /// <summary>Название формата для сообщений.</summary>
    string Format { get; }

    /// <summary>Начальные байты, по которым файл опознаётся как свой.</summary>
    /// <param name="header">Первые байты файла.</param>
    /// <returns><see langword="true" />, если разборщик берётся за файл.</returns>
    bool Matches(ReadOnlySpan<byte> header);

    /// <summary>Проверяет открытый файл.</summary>
    /// <param name="stream">Поток с начала файла.</param>
    /// <param name="bounds">Где внутри файла лежат аудиоданные: без тегов.</param>
    /// <param name="cancellationToken">Отмена: стоп или таймаут по файлу.</param>
    /// <returns>Вердикт.</returns>
    /// <remarks>
    /// Смещения в вердикте считаются от начала файла, а не от начала потока:
    /// человек ищет место повреждения в файле целиком.
    /// </remarks>
    ContainerValidation Validate(Stream stream, ContainerBounds bounds, CancellationToken cancellationToken);
}
