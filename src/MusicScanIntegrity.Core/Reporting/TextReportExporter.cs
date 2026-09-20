using System.Globalization;
using System.Text;
using MusicScanIntegrity.Core.Models;
using MusicScanIntegrity.Core.Resources;
using MusicScanIntegrity.Core.Settings;

namespace MusicScanIntegrity.Core.Reporting;

/// <summary>Plain text list, meant to be read directly.</summary>
public sealed class TextReportExporter : IReportExporter
{
    private const int RuleWidth = 78;

    /// <inheritdoc />
    public ReportFormat Format => ReportFormat.Text;

    /// <inheritdoc />
    public string FileExtension => ".txt";

    /// <inheritdoc />
    public async Task WriteAsync(ReportData data, Stream output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(output);

        await using StreamWriter writer = new(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true);
        ScanSummary summary = data.Summary;

        await writer.WriteLineAsync(new string('=', RuleWidth)).ConfigureAwait(false);
        await writer.WriteLineAsync(Common.Format.Text(Strings.Report_TextTitle, ReportData.ProductName)).ConfigureAwait(false);
        await writer.WriteLineAsync(new string('=', RuleWidth)).ConfigureAwait(false);
        await writer.WriteLineAsync().ConfigureAwait(false);

        // Label width follows the longest label, so both languages line up.
        (string Label, string Value)[] header =
        [
            (Strings.Report_Folder, summary.RootPath),
            (Strings.Report_Generated, Common.Format.Text(Strings.Report_DateTime, data.GeneratedAt)),
            (Strings.Report_ScanDuration, $"{Common.Format.Duration(summary.Duration)} ({Common.Format.DurationWords(summary.Duration)})"),
            (Strings.Report_Threads, summary.Parallelism.ToString(CultureInfo.InvariantCulture)),
            (Strings.Report_Method, summary.DepthLabel),
        ];
        await WriteAlignedAsync(writer, header).ConfigureAwait(false);
        await writer.WriteLineAsync().ConfigureAwait(false);

        if (data.Findings is { Count: > 0 } findings)
        {
            await writer.WriteLineAsync(Heading(Strings.Report_Title_CollectionFindings)).ConfigureAwait(false);
            await writer.WriteLineAsync(new string('-', RuleWidth)).ConfigureAwait(false);

            foreach (CollectionFinding finding in findings)
            {
                await writer.WriteLineAsync($"  [{finding.KindLabel}] {finding.Path}").ConfigureAwait(false);
                await writer.WriteLineAsync($"      {finding.Message}").ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(finding.Detail))
                {
                    await writer.WriteLineAsync($"      {finding.Detail}").ConfigureAwait(false);
                }
            }

            await writer.WriteLineAsync().ConfigureAwait(false);
        }

        await writer.WriteLineAsync(Heading(Strings.Report_Title_Summary)).ConfigureAwait(false);
        await writer.WriteLineAsync(new string('-', RuleWidth)).ConfigureAwait(false);

        List<(string Label, string Value)> counts =
        [
            ("  " + Strings.Report_TotalFiles, Common.Format.Number(summary.Counters.Total)),
            ("  " + Strings.Report_Checked, Common.Format.Number(summary.Counters.Checked)),
            ("  ✓ " + Strings.Report_Ok, Common.Format.Number(summary.Counters.Ok)),
            ("  ✕ " + Strings.Report_Corrupted, Common.Format.Number(summary.Counters.Corrupted)),
            ("  ! " + Strings.Report_Warnings, Common.Format.Number(summary.Counters.Warnings)),
            ("  – " + Strings.Report_Skipped, Common.Format.Number(summary.Counters.Skipped)),
        ];

        if (summary.PlaylistCount > 0)
        {
            counts.Add(("  " + Strings.Report_Playlists,
                Common.Format.Number(summary.PlaylistCount) + " " +
                Common.Format.Text(Strings.Report_BrokenLinksNote, Common.Format.Number(summary.PlaylistMissingLinks))));
        }

        await WriteAlignedAsync(writer, counts).ConfigureAwait(false);

        await writer.WriteLineAsync().ConfigureAwait(false);

        if (summary.WasStopped)
        {
            await writer.WriteLineAsync(Heading(Strings.Report_Attention) + ": " + Strings.Report_StoppedIncomplete).ConfigureAwait(false);
            await writer.WriteLineAsync().ConfigureAwait(false);
        }

        if (summary.CriticalFailure is { } failure)
        {
            await writer.WriteLineAsync(Heading(Strings.Report_CriticalFailure) + ": " + failure).ConfigureAwait(false);
            await writer.WriteLineAsync().ConfigureAwait(false);
        }

        // Grouped by status, starting with what the report is opened for.
        foreach (CheckStatus status in (CheckStatus[])[CheckStatus.Corrupted, CheckStatus.Warning, CheckStatus.Skipped, CheckStatus.Ok])
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<FileCheckResult> group = [.. data.Results.Where(r => r.Status == status)];
            if (group.Count == 0)
            {
                continue;
            }

            await writer.WriteLineAsync($"{status.Glyph()} {status.DisplayName().ToUpperInvariant()} — {Common.Format.Files(group.Count)}").ConfigureAwait(false);
            await writer.WriteLineAsync(new string('-', RuleWidth)).ConfigureAwait(false);

            foreach (FileCheckResult result in group)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await writer.WriteLineAsync($"  {result.FullPath}").ConfigureAwait(false);
                await writer.WriteLineAsync($"    {result.Format} · {Common.Format.Size(result.SizeBytes)} · {result.StatusLabel}").ConfigureAwait(false);

                if (status != CheckStatus.Ok)
                {
                    await writer.WriteLineAsync($"    {result.Description}").ConfigureAwait(false);

                    if (result.TechnicalDetail is { } detail)
                    {
                        await writer.WriteLineAsync($"    ({detail})").ConfigureAwait(false);
                    }
                }

                await writer.WriteLineAsync().ConfigureAwait(false);
            }
        }

        IReadOnlyList<PlaylistCheckResult> brokenPlaylists = [.. data.Playlists.Where(p => p.MissingCount > 0 || p.ParseIssue is not null)];
        if (brokenPlaylists.Count > 0)
        {
            await writer.WriteLineAsync(Heading(Strings.Report_Title_BrokenPlaylists)).ConfigureAwait(false);
            await writer.WriteLineAsync(new string('-', RuleWidth)).ConfigureAwait(false);

            foreach (PlaylistCheckResult playlist in brokenPlaylists)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await writer.WriteLineAsync($"  {playlist.FullPath}").ConfigureAwait(false);

                if (playlist.ParseIssue is { } issue)
                {
                    await writer.WriteLineAsync($"    {issue.Message}").ConfigureAwait(false);
                }

                foreach (PlaylistEntry entry in playlist.Entries.Where(e => !e.Exists))
                {
                    await writer.WriteLineAsync("    " + Common.Format.Text(Strings.Report_NotFound, entry.RawPath)).ConfigureAwait(false);
                }

                await writer.WriteLineAsync().ConfigureAwait(false);
            }
        }

        if (summary.InaccessibleFolders.Count > 0)
        {
            await writer.WriteLineAsync(Heading(Strings.Report_Title_UnreadableFolders)).ConfigureAwait(false);
            await writer.WriteLineAsync(new string('-', RuleWidth)).ConfigureAwait(false);

            foreach (InaccessibleFolder folder in summary.InaccessibleFolders)
            {
                await writer.WriteLineAsync($"  {folder.Path}").ConfigureAwait(false);
                await writer.WriteLineAsync($"    {folder.Reason}").ConfigureAwait(false);
            }

            await writer.WriteLineAsync().ConfigureAwait(false);
        }

        await writer.WriteLineAsync(new string('=', RuleWidth)).ConfigureAwait(false);
        await writer.WriteLineAsync(Common.Format.Text(Strings.Report_TextFooter, ReportData.ProductName, data.GeneratedAt)).ConfigureAwait(false);

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Upper-cased section heading.</summary>
    private static string Heading(string title) => title.ToUpper(CultureInfo.CurrentUICulture);

    /// <summary>Writes "label: value" lines with values aligned in one column.</summary>
    private static async Task WriteAlignedAsync(StreamWriter writer, IReadOnlyList<(string Label, string Value)> rows)
    {
        int width = rows.Max(r => r.Label.Length) + 2;

        foreach ((string label, string value) in rows)
        {
            await writer.WriteLineAsync((label + ":").PadRight(width) + value).ConfigureAwait(false);
        }
    }
}
