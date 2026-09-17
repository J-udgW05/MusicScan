using MusicScanIntegrity.Core.Models;
using MusicScanIntegrity.Core.Resources;
using MusicScanIntegrity.Core.Settings;

namespace MusicScanIntegrity.Core.Reporting;

/// <summary>Saves reports.</summary>
public interface IReportService
{
    /// <summary>Exporters for every supported format.</summary>
    IReadOnlyList<IReportExporter> Exporters { get; }

    /// <summary>Suggested report file name, for example "musicscan_2026-09-02_2104.html".</summary>
    string SuggestFileName(ReportFormat format, DateTimeOffset moment);

    /// <summary>Default report folder: from settings, or Documents.</summary>
    string ResolveDefaultFolder(AppSettings settings);

    /// <summary>
    /// The folder worth remembering after a report is saved.
    /// </summary>
    /// <param name="savedFilePath">Path the report was saved to.</param>
    /// <param name="currentDefaultFolder">Folder that was offered as the default.</param>
    /// <returns>
    /// The folder to store in settings, or <see langword="null" /> when there
    /// is nothing to remember: the path is empty, or it matches the default.
    /// </returns>
    string? FolderToRemember(string savedFilePath, string currentDefaultFolder);

    /// <summary>Writes the report to a file.</summary>
    Task SaveAsync(
        ReportFormat format,
        string filePath,
        ReportData data,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Selects the results for the report: problem files by default, healthy
    /// ones only when the corresponding setting is on.
    /// </summary>
    IReadOnlyList<FileCheckResult> Filter(IEnumerable<FileCheckResult> results, AppSettings settings);
}

/// <inheritdoc cref="IReportService" />
public sealed class ReportService : IReportService
{
    private readonly Dictionary<ReportFormat, IReportExporter> _byFormat;

    /// <summary>Creates the report service with the standard exporters.</summary>
    public ReportService(IEnumerable<IReportExporter>? exporters = null)
    {

        // An empty set counts as "not supplied"; see PlaylistService.
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
    /// Compared by full path, case-insensitively: on Windows "D:\Reports" and
    /// "d:\reports\" are the same folder. Leaving the setting untouched means
    /// "let the application decide", which is not the same as storing the
    /// Documents folder in it.
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

    /// <summary>Whether these are the same directory.</summary>
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
            // Not path-like at all; treat it as a different location.
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
            throw new NotSupportedException(Common.Format.Text(Strings.Report_FormatUnsupported, format));
        }

        string? folder = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        // Written to a temporary file alongside: a failed save must not leave
        // a half-written report in place of the previous one.
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

        // Corrupted first, then warnings, then skipped: a report is opened for
        // the problems, not for the list of healthy files.
        return [.. filtered
            .OrderByDescending(r => r.Status == CheckStatus.Corrupted)
            .ThenByDescending(r => r.Status == CheckStatus.Warning)
            .ThenByDescending(r => r.Status == CheckStatus.Skipped)
            .ThenBy(r => r.FullPath, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Removes a half-written report after a failed export.</summary>
    /// <remarks>
    /// The user has already been told about the failure itself. A second
    /// message saying the fragment could not be deleted adds nothing — it sits
    /// where they chose to save it.
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
