using MusicScanIntegrity.Core.Settings;

namespace MusicScanIntegrity.Core.Locking;

/// <summary>Вопрос пользователю о занятом файле.</summary>
/// <param name="FilePath">Путь к занятому файлу.</param>
/// <param name="SizeBytes">Размер файла — нужен, чтобы честно сказать, сколько будет скопировано.</param>
/// <param name="Owner">Кто держит файл (или честное «определить не удалось»).</param>
/// <param name="QueuedAfterThis">Сколько ещё вопросов ждёт в очереди.</param>
public sealed record LockedFileQuestion(
    string FilePath,
    long SizeBytes,
    LockOwnerResult Owner,
    int QueuedAfterThis);

/// <summary>Ответ пользователя.</summary>
/// <param name="Action">Что делать с этим файлом.</param>
/// <param name="ApplyToAll">Поступать так же со всеми остальными занятыми файлами.</param>
/// <param name="StopScan">Пользователь решил остановить всю проверку.</param>
public sealed record LockedFileDecision(LockedFileAction Action, bool ApplyToAll = false, bool StopScan = false)
{
    /// <summary>Ответ по умолчанию, когда спрашивать некого (например, в тестах).</summary>
    public static LockedFileDecision Skip { get; } = new(LockedFileAction.Skip);
}

/// <summary>
/// Кто спрашивает пользователя про занятый файл.
/// Реализация в интерфейсе обязана показывать вопросы по одному
/// (03_IMPLEMENTATION_GUIDE.md, раздел 2), а не пачкой окон.
/// </summary>
public interface ILockedFileDecisionProvider
{
    /// <summary>Задаёт вопрос и ждёт ответа пользователя.</summary>
    Task<LockedFileDecision> AskAsync(LockedFileQuestion question, CancellationToken cancellationToken);
}

/// <summary>
/// Заглушка на случай, когда спрашивать некому (тесты, пакетный режим):
/// файл просто пропускается.
/// </summary>
public sealed class AlwaysSkipDecisionProvider : ILockedFileDecisionProvider
{
    /// <inheritdoc />
    public Task<LockedFileDecision> AskAsync(LockedFileQuestion question, CancellationToken cancellationToken) =>
        Task.FromResult(LockedFileDecision.Skip);
}
