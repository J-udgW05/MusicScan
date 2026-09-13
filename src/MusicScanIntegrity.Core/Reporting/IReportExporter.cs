using MusicScanIntegrity.Core.Models;
using MusicScanIntegrity.Core.Settings;

namespace MusicScanIntegrity.Core.Reporting;

/// <summary>Данные, из которых собирается отчёт.</summary>
/// <param name="Summary">Сводка по проверке.</param>
/// <param name="Results">Результаты по файлам (уже отфильтрованные по настройкам отчёта).</param>
/// <param name="Playlists">Результаты по плейлистам.</param>
/// <param name="GeneratedAt">Момент формирования отчёта.</param>
public sealed record ReportData(
    ScanSummary Summary,
    IReadOnlyList<FileCheckResult> Results,
    IReadOnlyList<PlaylistCheckResult> Playlists,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<CollectionFinding>? Findings = null)
{
    /// <summary>Название программы — попадает в заголовок отчёта.</summary>
    public const string ProductName = "Music Scan Integrity";
}

/// <summary>Формирование отчёта одного формата.</summary>
public interface IReportExporter
{
    /// <summary>Формат, который умеет этот экспортёр.</summary>
    ReportFormat Format { get; }

    /// <summary>Расширение файла отчёта, например «.html».</summary>
    string FileExtension { get; }

    /// <summary>
    /// Пишет отчёт в поток. Экспорт не должен блокировать интерфейс,
    /// поэтому метод асинхронный (02_ARCHITECTURE.md, раздел 7).
    /// </summary>
    Task WriteAsync(ReportData data, Stream output, CancellationToken cancellationToken = default);
}
