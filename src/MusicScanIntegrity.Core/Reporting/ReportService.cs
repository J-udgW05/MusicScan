using MusicScanIntegrity.Core.Models;
using MusicScanIntegrity.Core.Settings;

namespace MusicScanIntegrity.Core.Reporting;

/// <summary>Сохранение отчётов.</summary>
public interface IReportService
{
    /// <summary>Экспортёры по всем поддерживаемым форматам.</summary>
    IReadOnlyList<IReportExporter> Exporters { get; }

    /// <summary>Предлагаемое имя файла отчёта, например «musicscan_2026-09-02_2104.html».</summary>
    string SuggestFileName(ReportFormat format, DateTimeOffset moment);

    /// <summary>Папка для отчётов по умолчанию — из настроек либо «Документы».</summary>
    string ResolveDefaultFolder(AppSettings settings);

    /// <summary>
    /// Папка, которую стоит запомнить после сохранения отчёта.
    /// </summary>
    /// <param name="savedFilePath">Путь сохранённого отчёта.</param>
    /// <param name="currentDefaultFolder">Папка, которая предлагалась по умолчанию.</param>
    /// <returns>
    /// Папка для записи в настройки или <see langword="null" />, если запоминать
    /// нечего: путь пуст или человек сохранил туда же, куда и предлагалось.
    /// </returns>
    string? FolderToRemember(string savedFilePath, string currentDefaultFolder);

    /// <summary>Сохраняет отчёт в файл.</summary>
    Task SaveAsync(
        ReportFormat format,
        string filePath,
        ReportData data,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Отбирает результаты для отчёта: по умолчанию только проблемные,
    /// файлы «в порядке» включаются отдельной настройкой.
    /// </summary>
    IReadOnlyList<FileCheckResult> Filter(IEnumerable<FileCheckResult> results, AppSettings settings);
}

/// <inheritdoc cref="IReportService" />
public sealed class ReportService : IReportService
{
    private readonly Dictionary<ReportFormat, IReportExporter> _byFormat;

    /// <summary>Создаёт службу отчётов со стандартным набором экспортёров.</summary>
    public ReportService(IEnumerable<IReportExporter>? exporters = null)
    {

        // Пустой набор считается «не задан» — см. пояснение в PlaylistService.
        IReadOnlyList<IReportExporter> all = exporters?.ToArray() is { Length: > 0 } provided
            ? provided
            : [new HtmlReportExporter(), new CsvReportExporter(), new TextReportExporter()];

        Exporters = all;
        _byFormat = all.ToDictionary(e => e.Format);
    }

    /// <inheritdoc />
    public IReadOnlyList<IReportExporter> Exporters { get; }

    /// <inheritdoc />
    public string SuggestFileName(ReportFormat format, DateTimeOffset moment)
    {
        string extension = _byFormat.TryGetValue(format, out IReportExporter? exporter) ? exporter.FileExtension : ".txt";
        return $"musicscan_{moment:yyyy-MM-dd_HHmm}{extension}";
    }

    /// <inheritdoc />
    public string ResolveDefaultFolder(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!string.IsNullOrWhiteSpace(settings.ReportsFolder) && Directory.Exists(settings.ReportsFolder))
        {
            return settings.ReportsFolder;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Сравнение по полному пути и без учёта регистра: в Windows «D:\Отчёты»
    /// и «d:\отчёты\» — одна и та же папка, и записывать её второй раз незачем.
    /// Незаписанное значение оставляет настройку прежней, то есть «как решит
    /// программа», — это не то же самое, что записать туда «Документы».
    /// </remarks>
    public string? FolderToRemember(string savedFilePath, string currentDefaultFolder)
    {
        if (string.IsNullOrWhiteSpace(savedFilePath))
        {
            return null;
        }

        string? folder = Path.GetDirectoryName(savedFilePath);

        if (string.IsNullOrWhiteSpace(folder))
        {
            return null;
        }

        return SamePlace(folder, currentDefaultFolder) ? null : folder;
    }

    /// <summary>Один и тот же ли это каталог.</summary>
    private static bool SamePlace(string first, string? second)
    {
        if (string.IsNullOrWhiteSpace(second))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Строка вообще не похожа на путь — считаем, что это другое место.
            return false;
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        ReportFormat format,
        string filePath,
        ReportData data,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(data);

        if (!_byFormat.TryGetValue(format, out IReportExporter? exporter))
        {
            throw new NotSupportedException($"Формат отчёта {format} не поддерживается.");
        }

        string? folder = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        // Пишем во временный файл рядом: если сохранение сорвётся, у пользователя
        // не останется наполовину записанного отчёта вместо старого.
        string tempPath = filePath + ".part";

        try
        {
            await using (FileStream stream = new(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 64,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await exporter.WriteAsync(data, stream, cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, filePath, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<FileCheckResult> Filter(IEnumerable<FileCheckResult> results, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(settings);

        IEnumerable<FileCheckResult> filtered = settings.IncludeOkFilesInReport
            ? results
            : results.Where(r => r.Status != CheckStatus.Ok);

        // Сначала повреждённые, потом предупреждения, потом пропущенные:
        // отчёт открывают ради проблем, а не ради списка исправных файлов.
        return [.. filtered
            .OrderByDescending(r => r.Status == CheckStatus.Corrupted)
            .ThenByDescending(r => r.Status == CheckStatus.Warning)
            .ThenByDescending(r => r.Status == CheckStatus.Skipped)
            .ThenBy(r => r.FullPath, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Убирает недописанный отчёт после неудачной выгрузки.</summary>
    /// <remarks>
    /// Об ошибке пользователю уже сказали — той, из-за которой выгрузка и
    /// сорвалась. Второе сообщение, что вдобавок не стёрся обрывок файла,
    /// ничего не добавит: он лежит там, куда пользователь сам указал путь.
    /// </remarks>
    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
        }
    }
}
