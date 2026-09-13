namespace MusicScanIntegrity.Core.Integrity;

/// <summary>Чем закончилась проверка файла его собственными средствами.</summary>
public enum ContainerVerdict
{
    /// <summary>Для этого формата разборщика нет — вердикта не будет.</summary>
    NotSupported,

    /// <summary>
    /// Структура цела, но контрольных сумм в формате нет или их нельзя
    /// проверить без распаковки. Это слабее, чем «суммы сошлись».
    /// </summary>
    StructureOnly,

    /// <summary>Контрольные суммы сошлись — файл совпадает с тем, что закодировали.</summary>
    Verified,

    /// <summary>Найдено повреждение: сумма не сошлась или структура порвана.</summary>
    Damaged,

    /// <summary>Файл не удалось прочитать — о самом содержимом ничего не известно.</summary>
    Unreadable,
}

/// <summary>Чем именно испорчен файл — от этого зависит формулировка для человека.</summary>
public enum ContainerDamage
{
    /// <summary>Повреждений нет.</summary>
    None,

    /// <summary>Разрушена структура: кадры, страницы или блоки не сходятся.</summary>
    Structure,

    /// <summary>Не сошлась контрольная сумма.</summary>
    Checksum,

    /// <summary>Файл обрывается: данных меньше, чем обещано заголовком.</summary>
    Truncation,
}

/// <summary>
/// Результат проверки файла его собственными средствами: контрольными суммами
/// и структурой контейнера.
/// </summary>
/// <param name="Verdict">Итог.</param>
/// <param name="Format">Формат, как его определил разборщик.</param>
/// <param name="Message">Человеческая формулировка для строки результата.</param>
/// <param name="TechnicalDetail">Техническая причина: смещение, ожидаемая и полученная сумма.</param>
/// <param name="UnitsChecked">Сколько кадров, страниц или блоков проверено.</param>
/// <param name="ErrorOffset">Смещение первой найденной ошибки от начала файла.</param>
/// <param name="Truncated">Файл обрывается на середине: не хватает данных, обещанных заголовком.</param>
/// <param name="TrailingBytes">Сколько лишних байт осталось в хвосте после последнего кадра.</param>
/// <param name="Damage">Вид повреждения.</param>
public sealed record ContainerValidation(
    ContainerVerdict Verdict,
    string Format,
    string? Message = null,
    string? TechnicalDetail = null,
    int UnitsChecked = 0,
    long? ErrorOffset = null,
    bool Truncated = false,
    long TrailingBytes = 0,
    ContainerDamage Damage = ContainerDamage.None)
{
    /// <summary>Разборщика для формата нет.</summary>
    public static ContainerValidation NotSupported(string format) =>
        new(ContainerVerdict.NotSupported, format);

    /// <summary>Суммы сошлись.</summary>
    public static ContainerValidation Verified(string format, int units, long trailing = 0) =>
        new(ContainerVerdict.Verified, format, UnitsChecked: units, TrailingBytes: trailing);

    /// <summary>Структура цела, но сумм в формате нет.</summary>
    public static ContainerValidation StructureOnly(string format, int units, string? detail = null) =>
        new(ContainerVerdict.StructureOnly, format, TechnicalDetail: detail, UnitsChecked: units);

    /// <summary>Найдено повреждение.</summary>
    public static ContainerValidation Damaged(
        string format,
        string message,
        string? detail = null,
        int units = 0,
        long? offset = null,
        bool truncated = false,
        ContainerDamage damage = ContainerDamage.Structure) =>
        new(
            ContainerVerdict.Damaged,
            format,
            message,
            detail,
            units,
            offset,
            truncated,
            Damage: truncated ? ContainerDamage.Truncation : damage);

    /// <summary>Файл не читается.</summary>
    public static ContainerValidation Unreadable(string format, string detail) =>
        new(ContainerVerdict.Unreadable, format, TechnicalDetail: detail);
}
