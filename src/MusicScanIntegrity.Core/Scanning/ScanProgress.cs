using MusicScanIntegrity.Core.Models;

namespace MusicScanIntegrity.Core.Scanning;

/// <summary>Состояние проверки.</summary>
public enum ScanState
{
    /// <summary>Проверка не запускалась.</summary>
    Idle,

    /// <summary>Идёт быстрый обход папки.</summary>
    Discovering,

    /// <summary>Идёт проверка файлов.</summary>
    Running,

    /// <summary>Пауза: начатые файлы дорабатываются, новые не берутся.</summary>
    Paused,

    /// <summary>Проверка завершена.</summary>
    Completed,

    /// <summary>Проверка остановлена пользователем.</summary>
    Stopped,

    /// <summary>Проверка прервана критическим сбоем декодера.</summary>
    Failed,
}

/// <summary>Снимок хода проверки для интерфейса.</summary>
/// <param name="State">Состояние.</param>
/// <param name="Counters">Счётчики.</param>
/// <param name="CurrentFile">Файл, который проверяется прямо сейчас.</param>
/// <param name="Elapsed">Сколько времени идёт проверка.</param>
/// <param name="Parallelism">Сколько файлов проверяется одновременно.</param>
/// <param name="PendingLockedQuestions">Сколько вопросов о занятых файлах ждёт ответа.</param>
public readonly record struct ScanProgress(
    ScanState State,
    ScanCounters Counters,
    string? CurrentFile,
    TimeSpan Elapsed,
    int Parallelism,
    int PendingLockedQuestions);

/// <summary>
/// Предупреждение, которое показывается прямо по ходу проверки, а не только в конце
/// (01_SPECIFICATION.md, раздел 2, пункт 5).
/// </summary>
/// <param name="Title">Человеческая формулировка.</param>
/// <param name="Path">Файл, к которому относится предупреждение.</param>
/// <param name="Code">Код замечания.</param>
public sealed record LiveWarning(string Title, string Path, IssueCode Code);
