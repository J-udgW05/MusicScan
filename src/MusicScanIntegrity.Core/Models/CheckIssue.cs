namespace MusicScanIntegrity.Core.Models;

/// <summary>
/// Одно замечание по файлу: код, человеческая формулировка и техническая причина.
/// </summary>
/// <remarks>
/// Разделение на «человеческий» и «технический» текст — требование UI_SPEC.md, раздел 9:
/// в основном тексте не должно быть «Error 0x…», техническая причина идёт второй строкой.
/// </remarks>
/// <param name="Code">Код замечания.</param>
/// <param name="Message">Формулировка для человека, например «Файл не найден».</param>
/// <param name="TechnicalDetail">Техническая причина: код ошибки декодера, исключение и т.п.</param>
public sealed record CheckIssue(IssueCode Code, string Message, string? TechnicalDetail = null)
{
    /// <summary>Статус, который это замечание задаёт файлу.</summary>
    public CheckStatus Severity => Code switch
    {
        IssueCode.None => CheckStatus.Ok,

        IssueCode.FileNotFound
            or IssueCode.AccessDenied
            or IssueCode.DecodeStartFailed
            or IssueCode.AudioReadFailed
            or IssueCode.EmptyFile
            or IssueCode.ChecksumMismatch
            or IssueCode.ContainerDamaged
            or IssueCode.Truncated
            or IssueCode.SilentCorruption
            or IssueCode.UnexpectedError => CheckStatus.Corrupted,

        IssueCode.LockedSkippedByUser => CheckStatus.Skipped,

        // Всё остальное — мягкие замечания: файл читается, но что-то не так.
        _ => CheckStatus.Warning,
    };

    /// <summary>Короткая подпись для колонки «Статус» в таблице результатов.</summary>
    public string ShortLabel => Code switch
    {
        IssueCode.MetadataProblem => "Нет тегов",
        IssueCode.CheckTimeout => "Долгая проверка",
        IssueCode.ExtensionMismatch => "Не то расширение",
        IssueCode.PasswordProtected => "Защищён",
        IssueCode.LockedWaitTimeout => "Был занят",
        IssueCode.LockedCheckedViaCopy => "Проверен по копии",
        IssueCode.LargeFile => "Большой файл",
        IssueCode.ChecksumMismatch => "Сумма не сошлась",
        IssueCode.ContainerDamaged => "Структура разрушена",
        IssueCode.Truncated => "Файл обрывается",
        IssueCode.DigitalSilence => "Нет звука",
        IssueCode.AudioDropout => "Провал в тишину",
        IssueCode.Clipping => "Перегрузка",
        IssueCode.DcOffset => "Смещение нуля",
        IssueCode.TranscodeSuspected => "Срезан верх",
        IssueCode.BrokenTagText => "Кракозябры в тегах",
        IssueCode.SilentCorruption => "Изменился сам собой",
        IssueCode.CueMarksBeyondFile => "Метки за пределом файла",
        _ => Severity.DisplayName(),
    };
}
