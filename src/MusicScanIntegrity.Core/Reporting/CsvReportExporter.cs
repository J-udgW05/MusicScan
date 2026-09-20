using System.Globalization;
using System.Text;
using MusicScanIntegrity.Core.Common;
using MusicScanIntegrity.Core.Models;
using MusicScanIntegrity.Core.Resources;
using MusicScanIntegrity.Core.Settings;

namespace MusicScanIntegrity.Core.Reporting;

/// <summary>Tabular report for Excel and further processing.</summary>
/// <remarks>
/// Semicolon separated and written as UTF-8 with a BOM: a Russian Excel
/// expects ";" by default, since the comma is its decimal separator, and the
/// BOM keeps Cyrillic readable. The leading "sep=;" directive is understood by
/// both Excel and LibreOffice.
/// </remarks>
public sealed class CsvReportExporter : IReportExporter
{
    private const char Separator = ';';

    /// <inheritdoc />
    public ReportFormat Format => ReportFormat.Csv;

    /// <inheritdoc />
    public string FileExtension => ".csv";

    /// <inheritdoc />
    public async Task WriteAsync(ReportData data, Stream output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(output);

        await using StreamWriter writer = new(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true);

        await writer.WriteLineAsync($"sep={Separator}").ConfigureAwait(false);

        // The summary is a block of its own before the table; every report
        // format carries one.
        ScanSummary summary = data.Summary;
        await WriteRowAsync(writer, [Strings.Report_Report, ReportData.ProductName]).ConfigureAwait(false);
        await WriteRowAsync(writer, [Strings.Report_Folder, summary.RootPath]).ConfigureAwait(false);
        await WriteRowAsync(writer, [Strings.Report_Generated, data.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)]).ConfigureAwait(false);
        await WriteRowAsync(writer, [Strings.Report_ScanDuration, Common.Format.Duration(summary.Duration)]).ConfigureAwait(false);
        await WriteRowAsync(writer, [Strings.Report_Threads, summary.Parallelism.ToString(CultureInfo.InvariantCulture)]).ConfigureAwait(false);
        await WriteRowAsync(writer, [Strings.Report_Method, summary.DepthLabel]).ConfigureAwait(false);

        if (data.Findings is { Count: > 0 } collectionFindings)
        {
            await WriteRowAsync(writer, [Strings.Report_CollectionFindings, collectionFindings.Count.ToString(CultureInfo.InvariantCulture)]).ConfigureAwait(false);

            foreach (CollectionFinding finding in collectionFindings)
            {
                await WriteRowAsync(writer, [finding.KindLabel, $"{finding.Path} — {finding.Message}"]).ConfigureAwait(false);
            }
        }
        await WriteRowAsync(writer, [Strings.Report_TotalFiles, summary.Counters.Total.ToString(CultureInfo.InvariantCulture)]).ConfigureAwait(false);
        await WriteRowAsync(writer, [Strings.Report_Checked, summary.Counters.Checked.ToString(CultureInfo.InvariantCulture)]).ConfigureAwait(false);
        await WriteRowAsync(writer, [Strings.Report_Ok, summary.Counters.Ok.ToString(CultureInfo.InvariantCulture)]).ConfigureAwait(false);
        await WriteRowAsync(writer, [Strings.Report_Corrupted, summary.Counters.Corrupted.ToString(CultureInfo.InvariantCulture)]).ConfigureAwait(false);
        await WriteRowAsync(writer, [Strings.Report_Warnings, summary.Counters.Warnings.ToString(CultureInfo.InvariantCulture)]).ConfigureAwait(false);
        await WriteRowAsync(writer, [Strings.Report_Skipped, summary.Counters.Skipped.ToString(CultureInfo.InvariantCulture)]).ConfigureAwait(false);
        await WriteRowAsync(writer, [Strings.Report_Playlists, summary.PlaylistCount.ToString(CultureInfo.InvariantCulture)]).ConfigureAwait(false);
        await WriteRowAsync(writer, [Strings.Report_BrokenPlaylistLinks, summary.PlaylistMissingLinks.ToString(CultureInfo.InvariantCulture)]).ConfigureAwait(false);

        if (summary.WasStopped)
        {
            await WriteRowAsync(writer, [Strings.Report_Attention, Strings.Report_StoppedIncomplete]).ConfigureAwait(false);
        }

        if (summary.CriticalFailure is { } failure)
        {
            await WriteRowAsync(writer, [Strings.Report_CriticalFailure, failure]).ConfigureAwait(false);
        }

        await writer.WriteLineAsync().ConfigureAwait(false);

        await WriteRowAsync(writer, [Strings.Report_Col_FileName, Strings.Report_Col_Path, Strings.Report_Col_Format, Strings.Report_Col_SizeBytes, Strings.Report_Col_Status, Strings.Report_Col_Description, Strings.Report_Col_TechnicalCause])
            .ConfigureAwait(false);

        foreach (FileCheckResult result in data.Results)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await WriteRowAsync(writer,
            [
                result.FileName,
                result.DirectoryPath,
                result.Format,
                result.SizeBytes.ToString(CultureInfo.InvariantCulture),
                result.StatusLabel,
                result.Description,
                result.TechnicalDetail ?? string.Empty,
            ]).ConfigureAwait(false);
        }

        if (data.Playlists.Count > 0)
        {
            await writer.WriteLineAsync().ConfigureAwait(false);
            await WriteRowAsync(writer, [Strings.Report_Col_Playlist, Strings.Report_Col_TotalEntries, Strings.Report_Col_Missing, Strings.Report_Col_MissingPath]).ConfigureAwait(false);

            foreach (PlaylistCheckResult playlist in data.Playlists)
            {
                cancellationToken.ThrowIfCancellationRequested();

                IReadOnlyList<PlaylistEntry> missing = [.. playlist.Entries.Where(e => !e.Exists)];

                if (missing.Count == 0)
                {
                    await WriteRowAsync(writer,
                    [
                        playlist.FullPath,
                        playlist.Entries.Count.ToString(CultureInfo.InvariantCulture),
                        "0",
                        string.Empty,
                    ]).ConfigureAwait(false);
                    continue;
                }

                foreach (PlaylistEntry entry in missing)
                {
                    await WriteRowAsync(writer,
                    [
                        playlist.FullPath,
                        playlist.Entries.Count.ToString(CultureInfo.InvariantCulture),
                        missing.Count.ToString(CultureInfo.InvariantCulture),
                        entry.RawPath,
                    ]).ConfigureAwait(false);
                }
            }
        }

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static Task WriteRowAsync(TextWriter writer, IReadOnlyList<string> cells) =>
        writer.WriteLineAsync(string.Join(Separator, cells.Select(Escape)));

    /// <summary>
    /// Characters that make spreadsheet programs treat a cell as a formula.
    /// </summary>
    private static readonly char[] FormulaStarters = ['=', '+', '-', '@', '\t', '\r'];

    /// <summary>
    /// RFC 4180 escaping: a cell is quoted when it contains the separator, a
    /// quote or a line break, and inner quotes are doubled.
    /// </summary>
    /// <remarks>
    /// Before that, a cell starting with a formula character gets a leading
    /// apostrophe. The report carries file names we did not choose: a file
    /// called <c>=HYPERLINK(...)</c> would execute as a formula when the report
    /// is opened. The apostrophe marks the cell as text and is not displayed.
    /// The cost is that names starting with a minus keep the apostrophe.
    /// </remarks>
    internal static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value.IndexOfAny(FormulaStarters) == 0)
        {
            value = "'" + value;
        }

        bool needsQuotes = value.Contains(Separator) ||
                           value.Contains('"') ||
                           value.Contains('\n') ||
                           value.Contains('\r');

        return needsQuotes ? '"' + value.Replace("\"", "\"\"") + '"' : value;
    }
}
