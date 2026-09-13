using System.Globalization;
using System.Text;
using MusicScanIntegrity.Core.Models;
using MusicScanIntegrity.Core.Settings;

namespace MusicScanIntegrity.Core.Reporting;

/// <summary>Простой текстовый список — «читать глазами».</summary>
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
        await writer.WriteLineAsync($"{ReportData.ProductName} — отчёт о проверке коллекции").ConfigureAwait(false);
        await writer.WriteLineAsync(new string('=', RuleWidth)).ConfigureAwait(false);
        await writer.WriteLineAsync().ConfigureAwait(false);

        await writer.WriteLineAsync($"Папка:            {summary.RootPath}").ConfigureAwait(false);
        await writer.WriteLineAsync($"Сформирован:      {data.GeneratedAt:dd.MM.yyyy HH:mm}").ConfigureAwait(false);
        await writer.WriteLineAsync($"Время проверки:   {Common.Format.Duration(summary.Duration)} ({Common.Format.DurationWords(summary.Duration)})").ConfigureAwait(false);
        await writer.WriteLineAsync($"Потоков:          {summary.Parallelism}").ConfigureAwait(false);
        await writer.WriteLineAsync($"Как проверяли:    {summary.DepthLabel}").ConfigureAwait(false);
        await writer.WriteLineAsync().ConfigureAwait(false);

        if (data.Findings is { Count: > 0 } findings)
        {
            await writer.WriteLineAsync("ЗАМЕЧАНИЯ ПО КОЛЛЕКЦИИ").ConfigureAwait(false);
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

        await writer.WriteLineAsync("СВОДКА").ConfigureAwait(false);
        await writer.WriteLineAsync(new string('-', RuleWidth)).ConfigureAwait(false);
        await writer.WriteLineAsync($"  Всего файлов:     {Common.Format.Number(summary.Counters.Total)}").ConfigureAwait(false);
        await writer.WriteLineAsync($"  Проверено:        {Common.Format.Number(summary.Counters.Checked)}").ConfigureAwait(false);
        await writer.WriteLineAsync($"  ✓ В порядке:      {Common.Format.Number(summary.Counters.Ok)}").ConfigureAwait(false);
        await writer.WriteLineAsync($"  ✕ Повреждено:     {Common.Format.Number(summary.Counters.Corrupted)}").ConfigureAwait(false);
        await writer.WriteLineAsync($"  ! Предупреждений: {Common.Format.Number(summary.Counters.Warnings)}").ConfigureAwait(false);
        await writer.WriteLineAsync($"  – Пропущено:      {Common.Format.Number(summary.Counters.Skipped)}").ConfigureAwait(false);

        if (summary.PlaylistCount > 0)
        {
            await writer.WriteLineAsync($"  Плейлистов:       {Common.Format.Number(summary.PlaylistCount)} (битых ссылок: {Common.Format.Number(summary.PlaylistMissingLinks)})").ConfigureAwait(false);
        }

        await writer.WriteLineAsync().ConfigureAwait(false);

        if (summary.WasStopped)
        {
            await writer.WriteLineAsync("ВНИМАНИЕ: проверка была остановлена — отчёт неполный.").ConfigureAwait(false);
            await writer.WriteLineAsync().ConfigureAwait(false);
        }

        if (summary.CriticalFailure is { } failure)
        {
            await writer.WriteLineAsync($"КРИТИЧЕСКИЙ СБОЙ: {failure}").ConfigureAwait(false);
            await writer.WriteLineAsync().ConfigureAwait(false);
        }

        // Файлы группируются по статусу: сначала то, ради чего отчёт и открывают.
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
            await writer.WriteLineAsync("ПЛЕЙЛИСТЫ С ПРОБЛЕМАМИ").ConfigureAwait(false);
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
                    await writer.WriteLineAsync($"    не найден: {entry.RawPath}").ConfigureAwait(false);
                }

                await writer.WriteLineAsync().ConfigureAwait(false);
            }
        }

        if (summary.InaccessibleFolders.Count > 0)
        {
            await writer.WriteLineAsync("ПАПКИ, КОТОРЫЕ НЕ УДАЛОСЬ ПРОЧИТАТЬ").ConfigureAwait(false);
            await writer.WriteLineAsync(new string('-', RuleWidth)).ConfigureAwait(false);

            foreach (InaccessibleFolder folder in summary.InaccessibleFolders)
            {
                await writer.WriteLineAsync($"  {folder.Path}").ConfigureAwait(false);
                await writer.WriteLineAsync($"    {folder.Reason}").ConfigureAwait(false);
            }

            await writer.WriteLineAsync().ConfigureAwait(false);
        }

        await writer.WriteLineAsync(new string('=', RuleWidth)).ConfigureAwait(false);
        await writer.WriteLineAsync($"Отчёт сформирован программой {ReportData.ProductName} " +
            $"{DateTime.Now.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("ru-RU"))}").ConfigureAwait(false);

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
