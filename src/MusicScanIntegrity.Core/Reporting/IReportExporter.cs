using MusicScanIntegrity.Core.Models;
using MusicScanIntegrity.Core.Settings;

namespace MusicScanIntegrity.Core.Reporting;

/// <summary>The data a report is built from.</summary>
/// <param name="Results">Per-file results, already filtered by the report settings.</param>
/// <param name="Theme">Colour scheme of the HTML report, matching the application's theme.</param>
public sealed record ReportData(
    ScanSummary Summary,
    IReadOnlyList<FileCheckResult> Results,
    IReadOnlyList<PlaylistCheckResult> Playlists,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<CollectionFinding>? Findings = null,
    ReportTheme Theme = ReportTheme.Light)
{
    /// <summary>Product name, used in the report heading.</summary>
    public const string ProductName = "Music Scan Integrity";
}

/// <summary>Colour scheme of a report that has one.</summary>
public enum ReportTheme
{
    /// <summary>Light background.</summary>
    Light,

    /// <summary>Dark background.</summary>
    Dark,
}

/// <summary>Renders a report in one format.</summary>
public interface IReportExporter
{
    /// <summary>The format this exporter handles.</summary>
    ReportFormat Format { get; }

    /// <summary>Report file extension, for example ".html".</summary>
    string FileExtension { get; }

    /// <summary>
    /// Writes the report to a stream. Asynchronous so exporting never blocks
    /// the UI.
    /// </summary>
    Task WriteAsync(ReportData data, Stream output, CancellationToken cancellationToken = default);
}
