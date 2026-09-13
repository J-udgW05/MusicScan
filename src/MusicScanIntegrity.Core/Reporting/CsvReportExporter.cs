using System.Globalization;
using System.Text;
using MusicScanIntegrity.Core.Common;
using MusicScanIntegrity.Core.Models;
using MusicScanIntegrity.Core.Settings;

namespace MusicScanIntegrity.Core.Reporting;

/// <summary>Отчёт-таблица для Excel и дальнейшей обработки.</summary>
/// <remarks>
/// Решение по неоднозначности: разделителем взята точка с запятой, а файл пишется
/// в UTF-8 с BOM. Русский Excel по умолчанию ждёт именно «;» (запятая у него —
/// десятичный разделитель), а BOM нужен, чтобы кириллица открылась без «кракозябр».
/// Первой строкой идёт директива «sep=;» — её понимает и Excel, и LibreOffice.
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

        // Сводка идёт отдельным блоком перед таблицей: спецификация требует,
        // чтобы она была в любом отчёте (01_SPECIFICATION.md, раздел 8).
        ScanSummary summary = data.Summary;
        await WriteRowAsync(writer, ["Отчёт", ReportData.ProductName]).ConfigureAwait(false);
        await WriteRowAsync(writer, ["Папка", summary.RootPath]).ConfigureAwait(false);
        await WriteRowAsync(writer, ["Сформирован", data.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)]).ConfigureAwait(false);
        await WriteRowAsync(writer, ["Время проверки", Common.Format.Duration(summary.Duration)]).ConfigureAwait(false);
        await WriteRowAsync(writer, ["Потоков", summary.Parallelism.ToString(CultureInfo.InvariantCulture)]).ConfigureAwait(false);
        await WriteRowAsync(writer, ["Как проверяли", summary.DepthLabel]).ConfigureAwait(false);

        if (data.Findings is { Count: > 0 } collectionFindings)
        {
            await WriteRowAsync(writer, ["Замечаний по коллекции", collectionFindings.Count.ToString(CultureInfo.InvariantCulture)]).ConfigureAwait(false);

            foreach (CollectionFinding finding in collectionFindings)
            {
                await WriteRowAsync(writer, [finding.KindLabel, $"{finding.Path} — {finding.Message}"]).ConfigureAwait(false);
            }
        }
        await WriteRowAsync(writer, ["Всего файлов", summary.Counters.Total.ToString(CultureInfo.InvariantCulture)]).ConfigureAwait(false);
        await WriteRowAsync(writer, ["Проверено", summary.Counters.Checked.ToString(CultureInfo.InvariantCulture)]).ConfigureAwait(false);
        await WriteRowAsync(writer, ["В порядке", summary.Counters.Ok.ToString(CultureInfo.InvariantCulture)]).ConfigureAwait(false);
        await WriteRowAsync(writer, ["Повреждено", summary.Counters.Corrupted.ToString(CultureInfo.InvariantCulture)]).ConfigureAwait(false);
        await WriteRowAsync(writer, ["Предупреждений", summary.Counters.Warnings.ToString(CultureInfo.InvariantCulture)]).ConfigureAwait(false);
        await WriteRowAsync(writer, ["Пропущено", summary.Counters.Skipped.ToString(CultureInfo.InvariantCulture)]).ConfigureAwait(false);
        await WriteRowAsync(writer, ["Плейлистов", summary.PlaylistCount.ToString(CultureInfo.InvariantCulture)]).ConfigureAwait(false);
        await WriteRowAsync(writer, ["Битых ссылок в плейлистах", summary.PlaylistMissingLinks.ToString(CultureInfo.InvariantCulture)]).ConfigureAwait(false);

        if (summary.WasStopped)
        {
            await WriteRowAsync(writer, ["Внимание", "Проверка была остановлена — отчёт неполный."]).ConfigureAwait(false);
        }

        if (summary.CriticalFailure is { } failure)
        {
            await WriteRowAsync(writer, ["Критический сбой", failure]).ConfigureAwait(false);
        }

        await writer.WriteLineAsync().ConfigureAwait(false);

        await WriteRowAsync(writer, ["Имя файла", "Путь", "Формат", "Размер, байт", "Статус", "Описание", "Техническая причина"])
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
            await WriteRowAsync(writer, ["Плейлист", "Всего записей", "Не найдено", "Отсутствующий путь"]).ConfigureAwait(false);

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
    /// Знаки, с которых табличные программы начинают считать ячейку формулой.
    /// </summary>
    private static readonly char[] FormulaStarters = ['=', '+', '-', '@', '\t', '\r'];

    /// <summary>
    /// Экранирование по RFC 4180: ячейка берётся в кавычки, если содержит
    /// разделитель, кавычку или перенос строки; внутренние кавычки удваиваются.
    /// </summary>
    /// <remarks>
    /// Перед этим ячейка, начинающаяся со знака формулы, получает апостроф.
    /// В отчёт попадают имена файлов, а имя придумывали не мы: файл, названный
    /// <c>=HYPERLINK(...)</c>, при открытии отчёта в Excel или LibreOffice
    /// выполнился бы как формула. Апостроф — принятая защита от этого: он
    /// говорит «это текст» и в самой ячейке не показывается. Цена — имена,
    /// начинающиеся с минуса, в отчёте будут с апострофом.
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
