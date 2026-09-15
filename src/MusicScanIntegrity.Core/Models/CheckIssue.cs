namespace MusicScanIntegrity.Core.Models;

/// <summary>
/// One finding about a file: a code, a human wording and a technical cause.
/// </summary>
/// <remarks>
/// The split is deliberate — the main text must never read "Error 0x…"; the
/// technical cause goes on a second line.
/// </remarks>
public sealed record CheckIssue(IssueCode Code, string Message, string? TechnicalDetail = null)
{
    /// <summary>Status this finding gives the file.</summary>
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

        // Everything else is soft: the file reads, but something is off.
        _ => CheckStatus.Warning,
    };

    /// <summary>Short caption for the status column of the results table.</summary>
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
