namespace MusicScanIntegrity.Core.Models;

/// <summary>
/// Результат проверки одного плейлиста.
/// У плейлистов проверяется только существование путей внутри — декодирование
/// уже покрыто обычным сканированием (02_ARCHITECTURE.md, раздел 6).
/// </summary>
public sealed class PlaylistCheckResult
{
    /// <summary>Путь к файлу плейлиста.</summary>
    public required string FullPath { get; init; }

    /// <summary>Имя файла плейлиста.</summary>
    public string FileName => Path.GetFileName(FullPath);

    /// <summary>Все записи плейлиста.</summary>
    public required IReadOnlyList<PlaylistEntry> Entries { get; init; }

    /// <summary>Не удалось разобрать сам файл плейлиста.</summary>
    public CheckIssue? ParseIssue { get; init; }

    /// <summary>
    /// Замечание по содержимому: например, метки cue выходят за длительность файла.
    /// </summary>
    public CheckIssue? ContentIssue { get; init; }

    /// <summary>Сколько путей не нашлось на диске.</summary>
    public int MissingCount => Entries.Count(e => !e.Exists);

    /// <summary>Итоговый статус плейлиста.</summary>
    public CheckStatus Status =>
        ParseIssue is not null ? ParseIssue.Severity
        : ContentIssue is not null ? ContentIssue.Severity
        : MissingCount > 0 ? CheckStatus.Warning
        : CheckStatus.Ok;
}

/// <summary>Одна запись внутри плейлиста.</summary>
/// <param name="RawPath">Путь ровно так, как он записан в плейлисте.</param>
/// <param name="ResolvedPath">Развёрнутый абсолютный путь (относительные — от папки плейлиста).</param>
/// <param name="Exists">Файл найден на диске.</param>
/// <param name="KnownStatus">
/// Статус из основного сканирования, если этот файл уже проверялся.
/// Нужен, чтобы один и тот же файл не получил два разных результата
/// (03_IMPLEMENTATION_GUIDE.md, раздел 2).
/// </param>
public sealed record PlaylistEntry(string RawPath, string? ResolvedPath, bool Exists, CheckStatus? KnownStatus = null);
