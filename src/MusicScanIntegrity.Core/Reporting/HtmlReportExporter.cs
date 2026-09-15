using System.Net;
using System.Text;
using MusicScanIntegrity.Core.Models;
using MusicScanIntegrity.Core.Settings;

namespace MusicScanIntegrity.Core.Reporting;

/// <summary>
/// Styled HTML page carrying the same status colours as the application.
/// </summary>
/// <remarks>
/// Colour tokens and typography match the application, dark theme included.
/// Everything passes through <see cref="WebUtility.HtmlEncode"/>: quotes and
/// angle brackets in file names must not break the markup.
/// </remarks>
public sealed class HtmlReportExporter : IReportExporter
{
    /// <inheritdoc />
    public ReportFormat Format => ReportFormat.Html;

    /// <inheritdoc />
    public string FileExtension => ".html";

    /// <inheritdoc />
    public async Task WriteAsync(ReportData data, Stream output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(output);

        await using StreamWriter writer = new(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);
        ScanSummary summary = data.Summary;

        await writer.WriteAsync(Head(summary)).ConfigureAwait(false);
        await writer.WriteAsync(Header(data)).ConfigureAwait(false);
        await writer.WriteAsync(Kpis(summary)).ConfigureAwait(false);
        await writer.WriteAsync(Distribution(summary.Counters)).ConfigureAwait(false);

        if (summary.WasStopped || summary.CriticalFailure is not null)
        {
            await writer.WriteAsync(Notice(summary)).ConfigureAwait(false);
        }

        if (data.Results.Count > 0)
        {
            await writer.WriteAsync(Toolbar(data)).ConfigureAwait(false);
        }

        await writer.WriteAsync(TableHead()).ConfigureAwait(false);

        foreach (FileCheckResult result in data.Results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteAsync(Row(result)).ConfigureAwait(false);
        }

        await writer.WriteAsync(TableTail(data)).ConfigureAwait(false);

        if (data.Findings is { Count: > 0 } findings)
        {
            await writer.WriteAsync(Findings(findings, cancellationToken)).ConfigureAwait(false);
        }

        if (data.Playlists.Count > 0)
        {
            await writer.WriteAsync(Playlists(data.Playlists, cancellationToken)).ConfigureAwait(false);
        }

        if (summary.InaccessibleFolders.Count > 0)
        {
            await writer.WriteAsync(Inaccessible(summary.InaccessibleFolders)).ConfigureAwait(false);
        }

        await writer.WriteAsync(Footer(data)).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Escapes markup characters into plain text.</summary>
    internal static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private static string StatusClass(CheckStatus status) => status switch
    {
        CheckStatus.Ok => "ok",
        CheckStatus.Warning => "warn",
        CheckStatus.Corrupted => "err",
        _ => "skip",
    };

    private static string Head(ScanSummary summary) => $$"""
        <!DOCTYPE html>
        <html lang="ru">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>{{E(ReportData.ProductName)}} — отчёт по папке {{E(Path.GetFileName(summary.RootPath.TrimEnd(Path.DirectorySeparatorChar)))}}</title>
        <style>
        :root{
          --bg:#f3f3f3;--mica:#f9f9fb;--card:#fff;--card2:#fbfbfd;--bd:#e3e3e6;--bd2:#ececef;
          --fg:#1a1a1c;--fg2:#5c5c62;--fg3:#8b8b92;--acc:#2f6fd0;
          --ok:#0f7b3f;--okbg:#e8f6ed;--err:#c42b2f;--errbg:#fdecec;
          --warn:#8a5a06;--warnbg:#fdf3e2;--skip:#6f6f76;--skipbg:#efeff1;
        }
        @media (prefers-color-scheme: dark){
          :root{
            --bg:#1c1c1e;--mica:#202022;--card:#26262a;--card2:#2c2c31;--bd:#38383e;--bd2:#303036;
            --fg:#f2f2f4;--fg2:#b0b0b8;--fg3:#84848d;--acc:#5b9bf3;
            --ok:#5ec27f;--okbg:#1c3227;--err:#ff7075;--errbg:#3a2024;
            --warn:#f0b429;--warnbg:#372c15;--skip:#9a9aa2;--skipbg:#2b2b30;
          }
        }
        *{box-sizing:border-box}
        body{margin:0;padding:32px 24px;background:var(--bg);color:var(--fg);
          font:13px/1.45 "Segoe UI Variable Text","Segoe UI",system-ui,sans-serif;
          -webkit-font-smoothing:antialiased;font-variant-numeric:tabular-nums}
        .wrap{max-width:1240px;margin:0 auto}
        .mono{font-family:"Cascadia Mono",Consolas,ui-monospace,monospace;font-size:11px}
        h1{font-size:26px;font-weight:650;letter-spacing:-.02em;margin:0}
        .sub{font-size:12.5px;color:var(--fg2);margin-top:6px}
        .eyebrow{font-size:11.5px;letter-spacing:.08em;text-transform:uppercase;color:var(--fg3);font-weight:650}
        section{background:var(--card);border:1px solid var(--bd);border-radius:10px;margin-top:18px;overflow:hidden}
        .pad{padding:18px}
        .kpis{display:grid;grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:14px;margin-top:22px}
        .kpi{background:var(--card);border:1px solid var(--bd);border-radius:10px;padding:16px}
        .kpi .v{font-size:32px;font-weight:650;letter-spacing:-.025em;line-height:1.05;margin-top:8px}
        .kpi .n{font-size:12px;color:var(--fg3);margin-top:2px}
        .bar{display:flex;height:14px;border-radius:999px;overflow:hidden;gap:2px;margin-top:14px}
        .legend{display:grid;grid-template-columns:repeat(auto-fit,minmax(160px,1fr));gap:12px;margin-top:16px}
        .legend div{display:flex;align-items:center;gap:9px;font-size:12.5px;color:var(--fg2)}
        .dot{width:10px;height:10px;border-radius:3px;flex:none}
        .legend b{margin-left:auto;color:var(--fg);font-weight:650}
        /* Своя прокрутка: секция обрезает по краю, и без неё крайняя
           колонка на узком окне пропадала бы совсем. */
        .scroll{overflow-x:auto}
        table{width:100%;min-width:620px;border-collapse:collapse}
        th{background:var(--card2);border-bottom:1px solid var(--bd);padding:11px 16px;text-align:left;
          font-size:11.5px;font-weight:650;letter-spacing:.08em;text-transform:uppercase;color:var(--fg3)}
        td{border-bottom:1px solid var(--bd2);padding:10px 16px;vertical-align:top}
        tr:last-child td{border-bottom:none}
        td.num{text-align:right;white-space:nowrap;color:var(--fg2)}
        .name{font-weight:550}
        .path{color:var(--fg3)}
        .why{color:var(--fg2);font-size:12.5px;margin-top:3px}
        .tech{color:var(--fg3);margin-top:3px}
        .pill{display:inline-flex;align-items:center;gap:6px;border-radius:999px;padding:4px 10px;
          font-size:11.5px;font-weight:650;white-space:nowrap}
        .pill .g{font-weight:700}
        .ok{background:var(--okbg);color:var(--ok)} .b-ok{background:var(--ok)}
        .err{background:var(--errbg);color:var(--err)} .b-err{background:var(--err)}
        .warn{background:var(--warnbg);color:var(--warn)} .b-warn{background:var(--warn)}
        .skip{background:var(--skipbg);color:var(--skip)} .b-skip{background:var(--skip)}
        .notice{border-left:3px solid var(--warn);background:var(--warnbg);color:var(--fg);
          border-radius:8px;padding:13px 16px;margin-top:18px;font-size:12.5px}
        footer{margin-top:22px;font-size:11.5px;color:var(--fg3);text-align:center}
        /* ── Панель отбора: то же, что на вкладке «Результаты» ───────────── */
        .tools{position:sticky;top:0;z-index:5;overflow:visible;
          display:flex;flex-wrap:wrap;gap:10px;align-items:center;
          background:var(--mica);border:1px solid var(--bd);border-radius:10px;
          padding:12px 14px;margin-top:18px}
        .search{flex:1 1 210px;min-width:150px;background:var(--card);color:var(--fg);
          border:1px solid var(--bd);border-radius:8px;padding:7px 11px;font:inherit;font-size:12.5px}
        .search::placeholder{color:var(--fg3)}
        .search:focus{outline:2px solid var(--acc);outline-offset:-1px}
        .chips{display:flex;flex-wrap:wrap;gap:8px}
        .chip{display:inline-flex;align-items:center;gap:6px;cursor:pointer;
          background:var(--card);border:1px solid var(--bd);border-radius:999px;
          padding:6px 13px;font:inherit;font-size:12px;font-weight:600;color:var(--fg2)}
        .chip:hover{border-color:var(--fg3)}
        .chip b{font-weight:700;color:var(--fg3)}
        .chip.on{border-color:transparent;box-shadow:inset 0 0 0 1.5px currentColor}
        .chip.c-all.on{background:var(--card2);color:var(--acc)}
        .chip.c-ok.on{background:var(--okbg);color:var(--ok)}
        .chip.c-err.on{background:var(--errbg);color:var(--err)}
        .chip.c-warn.on{background:var(--warnbg);color:var(--warn)}
        .chip.c-skip.on{background:var(--skipbg);color:var(--skip)}
        .chip.on b{color:inherit}
        .chip .g{font-weight:700}
        .fmt{background:var(--card);color:var(--fg);border:1px solid var(--bd);border-radius:8px;
          padding:7px 11px;font:inherit;font-size:12.5px;cursor:pointer}
        .fmt:focus{outline:2px solid var(--acc);outline-offset:-1px}
        .shown{margin:10px 2px 0;font-size:12px;color:var(--fg3)}
        .hint{margin:6px 2px 0;font-size:12px;color:var(--fg3)}
        tr[hidden]{display:none}
        .none td{padding:26px 16px;text-align:center;color:var(--fg3)}
        @media print{ .tools{position:static} }
        </style>
        </head>
        <body><div class="wrap">

        """;

    private static string Header(ReportData data) => $"""
        <div class="eyebrow">{E(ReportData.ProductName)} · отчёт о проверке коллекции</div>
        <h1>{E(data.Summary.RootPath)}</h1>
        <div class="sub">Сформирован {data.GeneratedAt:dd.MM.yyyy} в {data.GeneratedAt:HH:mm} ·
        проверка заняла {E(Common.Format.Duration(data.Summary.Duration))} ·
        потоков: {data.Summary.Parallelism}</div>
        <div class="sub">{E(data.Summary.DepthLabel)}</div>

        """;

    private static string Kpis(ScanSummary summary)
    {
        ScanCounters c = summary.Counters;
        double corruptedShare = c.Total > 0 ? (double)c.Corrupted / c.Total : 0;

        return $"""
            <div class="kpis">
              <div class="kpi"><div class="eyebrow">Всего файлов</div>
                <div class="v">{E(Common.Format.Number(c.Total))}</div>
                <div class="n">в {E(Common.Format.Number(summary.Folders.Count))} {E(Common.Format.Plural(summary.Folders.Count, "папке", "папках", "папках"))}</div></div>
              <div class="kpi"><div class="eyebrow">Повреждено</div>
                <div class="v" style="color:var(--err)">{E(Common.Format.Number(c.Corrupted))}</div>
                <div class="n">{E(Common.Format.Percent(corruptedShare))} коллекции</div></div>
              <div class="kpi"><div class="eyebrow">Требуют внимания</div>
                <div class="v" style="color:var(--warn)">{E(Common.Format.Number(c.Warnings))}</div>
                <div class="n">теги, занятость, расширение</div></div>
              <div class="kpi"><div class="eyebrow">Время проверки</div>
                <div class="v">{E(Common.Format.Duration(summary.Duration))}</div>
                <div class="n">{c.Checked} {E(Common.Format.Plural(c.Checked, "файл проверен", "файла проверено", "файлов проверено"))}</div></div>
            </div>

            """;
    }

    private static string Distribution(ScanCounters c)
    {
        int total = Math.Max(1, c.Ok + c.Corrupted + c.Warnings + c.Skipped);
        string Width(int part) => (100.0 * part / total).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        return $"""
            <section class="pad">
              <div style="font-size:13px;font-weight:650">Распределение по статусам</div>
              <div class="bar">
                <div class="b-ok" style="width:{Width(c.Ok)}%"></div>
                <div class="b-err" style="width:{Width(c.Corrupted)}%"></div>
                <div class="b-warn" style="width:{Width(c.Warnings)}%"></div>
                <div class="b-skip" style="width:{Width(c.Skipped)}%"></div>
              </div>
              <div class="legend">
                <div><span class="dot b-ok"></span>✓ В порядке<b>{E(Common.Format.Number(c.Ok))}</b></div>
                <div><span class="dot b-err"></span>✕ Повреждено<b>{E(Common.Format.Number(c.Corrupted))}</b></div>
                <div><span class="dot b-warn"></span>! Предупреждения<b>{E(Common.Format.Number(c.Warnings))}</b></div>
                <div><span class="dot b-skip"></span>– Пропущено<b>{E(Common.Format.Number(c.Skipped))}</b></div>
              </div>
            </section>

            """;
    }

    private static string Notice(ScanSummary summary)
    {
        StringBuilder builder = new();
        builder.Append("<div class=\"notice\">");

        if (summary.CriticalFailure is { } failure)
        {
            builder.Append("<b>Проверка прервана критическим сбоем.</b> ").Append(E(failure)).Append(' ');
        }

        if (summary.WasStopped)
        {
            builder.Append("<b>Проверка была остановлена вручную</b> — в отчёт попало только то, что успели проверить.");
        }

        builder.Append("</div>\n");
        return builder.ToString();
    }

    private static string TableHead() => """
        <section><div class="scroll">
        <table><thead><tr>
          <th style="width:26px"></th><th>Имя</th><th>Путь</th>
          <th style="width:72px">Формат</th><th style="width:92px;text-align:right">Размер</th><th style="width:220px">Статус</th>
        </tr></thead><tbody>

        """;

    private static string Row(FileCheckResult result)
    {
        string css = StatusClass(result.Status);
        StringBuilder builder = new();

        // Filtering uses these three attributes; the search string is built
        // ahead of time rather than on every keystroke.
        builder.Append("<tr data-status=\"").Append(css)
               .Append("\" data-format=\"").Append(E(result.Format))
               .Append("\" data-find=\"").Append(E(SearchKey(result))).Append("\">")
               .Append("<td><span class=\"pill ").Append(css).Append("\"><span class=\"g\">")
               .Append(E(result.Status.Glyph())).Append("</span></span></td>")
               .Append("<td><div class=\"name\">").Append(E(result.FileName)).Append("</div>");

        if (result.Status != CheckStatus.Ok)
        {
            builder.Append("<div class=\"why\">").Append(E(result.Description)).Append("</div>");

            if (result.TechnicalDetail is { } detail)
            {
                builder.Append("<div class=\"tech mono\">").Append(E(detail)).Append("</div>");
            }
        }

        builder.Append("</td>")
               .Append("<td class=\"path mono\">").Append(E(result.DirectoryPath)).Append("</td>")
               .Append("<td>").Append(E(result.Format)).Append("</td>")
               .Append("<td class=\"num\">").Append(E(Common.Format.Size(result.SizeBytes))).Append("</td>")
               .Append("<td><span class=\"pill ").Append(css).Append("\"><span class=\"g\">")
               .Append(E(result.Status.Glyph())).Append("</span>").Append(E(result.StatusLabel))
               .Append("</span></td></tr>\n");

        return builder.ToString();
    }

    /// <summary>What the search box matches against: name and path, lower-cased.</summary>
    private static string SearchKey(FileCheckResult result) =>
        (result.FileName + " " + result.DirectoryPath).ToLowerInvariant();

    /// <summary>
    /// Filter bar above the table: the same status chips and format selector
    /// as the results tab.
    /// </summary>
    /// <remarks>
    /// Chip counts come from the table rows, not the scan summary. The
    /// difference is real: healthy files are excluded by default, so a chip
    /// counting from the summary would promise rows the file does not contain.
    /// Only statuses actually present in the table get a chip.
    /// </remarks>
    private static string Toolbar(ReportData data)
    {
        int total = data.Results.Count;
        StringBuilder builder = new();

        builder.Append("<div class=\"tools\">")
               .Append("<input id=\"q\" class=\"search\" type=\"search\" autocomplete=\"off\" ")
               .Append("placeholder=\"Поиск по имени или пути\" aria-label=\"Поиск по имени или пути\">")
               .Append("<div class=\"chips\" role=\"group\" aria-label=\"Отбор по статусу\">")
               .Append(Chip("all", "all", string.Empty, "Все", total));

        // Labels are category names, as on the results tab, rather than the
        // status of a single file — the reader should see familiar wording.
        foreach ((CheckStatus status, string css, string label) in new[]
                 {
                     (CheckStatus.Ok, "ok", "В порядке"),
                     (CheckStatus.Corrupted, "err", "Повреждено"),
                     (CheckStatus.Warning, "warn", "Предупреждения"),
                     (CheckStatus.Skipped, "skip", "Пропущено"),
                 })
        {
            int count = data.Results.Count(r => r.Status == status);
            if (count > 0)
            {
                builder.Append(Chip(css, css, status.Glyph(), label, count));
            }
        }

        builder.Append("</div>");

        string[] formats = [.. data.Results
            .Select(r => r.Format)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)];

        builder.Append("<select id=\"fmt\" class=\"fmt\" aria-label=\"Отбор по формату\">")
               .Append("<option value=\"all\">Формат: все</option>");

        foreach (string format in formats)
        {
            int count = data.Results.Count(r => string.Equals(r.Format, format, StringComparison.OrdinalIgnoreCase));
            builder.Append("<option value=\"").Append(E(format)).Append("\">")
                   .Append(E(format)).Append(" · ").Append(count).Append("</option>");
        }

        builder.Append("</select></div>")
               .Append("<div id=\"shown\" class=\"shown\">Показаны все ")
               .Append(E(Common.Format.Files(total))).Append("</div>");

        // When healthy files were excluded, say so: otherwise the table count
        // disagrees with the summary above it for no visible reason.
        int okInReport = data.Results.Count(r => r.Status == CheckStatus.Ok);
        int okInScan = data.Summary.Counters.Ok;

        if (okInReport == 0 && okInScan > 0)
        {
            builder.Append("<div class=\"hint\">В таблице только файлы с замечаниями. Исправных ")
                   .Append(E(Common.Format.Number(okInScan)))
                   .Append(" — они в отчёт не включены (настройка «Добавлять в отчёт исправные файлы»).</div>");
        }

        return builder.Append('\n').ToString();
    }

    /// <summary>One status filter chip.</summary>
    private static string Chip(string value, string css, string glyph, string label, int count)
    {
        StringBuilder builder = new();
        builder.Append("<button type=\"button\" class=\"chip c-").Append(css)
               .Append(value == "all" ? " on" : string.Empty)
               .Append("\" data-filter=\"").Append(value).Append("\">");

        if (!string.IsNullOrEmpty(glyph))
        {
            builder.Append("<span class=\"g\">").Append(E(glyph)).Append("</span>");
        }

        return builder.Append(E(label)).Append("<b>").Append(count).Append("</b></button>").ToString();
    }

    /// <summary>Table tail: the empty row shown when the filter matches nothing.</summary>
    private static string TableTail(ReportData data) => data.Results.Count > 0
        ? "<tr id=\"none\" class=\"none\" hidden><td colspan=\"6\">Ничего не найдено — измените условия отбора.</td></tr>\n</tbody></table></div></section>\n"
        : "</tbody></table></div></section>\n";

    /// <summary>Findings about folders and duplicates rather than single files.</summary>
    private static string Findings(IReadOnlyList<CollectionFinding> findings, CancellationToken cancellationToken)
    {
        StringBuilder builder = new();
        builder.Append("<section><div class=\"scroll\"><table><thead><tr><th style=\"width:200px\">Замечание</th><th>Где и что</th></tr></thead><tbody>\n");

        foreach (CollectionFinding finding in findings)
        {
            cancellationToken.ThrowIfCancellationRequested();

            builder.Append("<tr><td>").Append(E(finding.KindLabel)).Append("</td><td>")
                   .Append("<div class=\"why\">").Append(E(finding.Message)).Append("</div>")
                   .Append("<div class=\"path mono\">").Append(E(finding.Path)).Append("</div>");

            if (!string.IsNullOrWhiteSpace(finding.Detail))
            {
                builder.Append("<div class=\"tech mono\">").Append(E(finding.Detail)).Append("</div>");
            }

            builder.Append("</td></tr>\n");
        }

        builder.Append("</tbody></table></div></section>\n");
        return builder.ToString();
    }

    private static string Playlists(IReadOnlyList<PlaylistCheckResult> playlists, CancellationToken cancellationToken)
    {
        StringBuilder builder = new();
        builder.Append("<section><div class=\"scroll\"><table><thead><tr><th>Плейлист</th><th style=\"width:120px;text-align:right\">Записей</th><th style=\"width:140px;text-align:right\">Не найдено</th></tr></thead><tbody>\n");

        foreach (PlaylistCheckResult playlist in playlists)
        {
            cancellationToken.ThrowIfCancellationRequested();

            builder.Append("<tr><td><div class=\"name\">").Append(E(playlist.FileName)).Append("</div>")
                   .Append("<div class=\"path mono\">").Append(E(playlist.FullPath)).Append("</div>");

            if (playlist.ParseIssue is { } issue)
            {
                builder.Append("<div class=\"why\">").Append(E(issue.Message)).Append("</div>");
            }

            foreach (PlaylistEntry entry in playlist.Entries.Where(e => !e.Exists))
            {
                builder.Append("<div class=\"tech mono\">не найден: ").Append(E(entry.RawPath)).Append("</div>");
            }

            builder.Append("</td><td class=\"num\">").Append(playlist.Entries.Count)
                   .Append("</td><td class=\"num\"")
                   .Append(playlist.MissingCount > 0 ? " style=\"color:var(--err);font-weight:650\"" : string.Empty)
                   .Append('>').Append(playlist.MissingCount).Append("</td></tr>\n");
        }

        builder.Append("</tbody></table></div></section>\n");
        return builder.ToString();
    }

    private static string Inaccessible(IReadOnlyList<InaccessibleFolder> folders)
    {
        StringBuilder builder = new();
        builder.Append("<section class=\"pad\"><div class=\"eyebrow\">Папки, которые не удалось прочитать</div>");

        foreach (InaccessibleFolder folder in folders)
        {
            builder.Append("<div style=\"margin-top:10px\"><div class=\"mono\">").Append(E(folder.Path))
                   .Append("</div><div class=\"why\">").Append(E(folder.Reason)).Append("</div></div>");
        }

        builder.Append("</section>\n");
        return builder.ToString();
    }

    private static string Footer(ReportData data) => $"""
        <footer>{E(ReportData.ProductName)} · отчёт сформирован {data.GeneratedAt:dd.MM.yyyy HH:mm}</footer>
        </div>
        {(data.Results.Count > 0 ? FilterScript : string.Empty)}</body></html>

        """;

    /// <summary>
    /// Row filtering inside the report itself.
    /// </summary>
    /// <remarks>
    /// Plain JavaScript with no external references: the report gets copied to
    /// a stick, emailed and opened offline, so anything loaded from outside
    /// would simply fail. Rows are hidden through the hidden property rather
    /// than by rebuilding the table — instant on several hundred rows and it
    /// does not disturb the markup.
    /// </remarks>
    private const string FilterScript = """
        <script>
        (function () {
          var rows = Array.prototype.slice.call(
            document.querySelectorAll('tr[data-status]'));
          if (!rows.length) { return; }

          var chips = Array.prototype.slice.call(document.querySelectorAll('.chip'));
          var format = document.getElementById('fmt');
          var query = document.getElementById('q');
          var shown = document.getElementById('shown');
          var none = document.getElementById('none');
          var status = 'all';

          function plural(n, one, few, many) {
            var d = Math.abs(n) % 100;
            if (d >= 11 && d <= 14) { return many; }
            d = d % 10;
            if (d === 1) { return one; }
            if (d >= 2 && d <= 4) { return few; }
            return many;
          }

          function apply() {
            var wantFormat = format ? format.value : 'all';
            var text = query ? query.value.trim().toLowerCase() : '';
            var visible = 0;

            for (var i = 0; i < rows.length; i++) {
              var row = rows[i];
              var fits =
                (status === 'all' || row.getAttribute('data-status') === status) &&
                (wantFormat === 'all' || row.getAttribute('data-format') === wantFormat) &&
                (text === '' || row.getAttribute('data-find').indexOf(text) !== -1);

              row.hidden = !fits;
              if (fits) { visible++; }
            }

            if (none) { none.hidden = visible > 0; }

            if (shown) {
              shown.textContent = visible === rows.length
                ? 'Показаны все ' + rows.length + ' ' +
                  plural(rows.length, 'файл', 'файла', 'файлов')
                : 'Показано ' + visible + ' из ' + rows.length;
            }
          }

          chips.forEach(function (chip) {
            chip.addEventListener('click', function () {
              status = chip.getAttribute('data-filter');
              chips.forEach(function (other) { other.classList.remove('on'); });
              chip.classList.add('on');
              apply();
            });
          });

          if (format) { format.addEventListener('change', apply); }
          if (query) { query.addEventListener('input', apply); }
          apply();
        })();
        </script>
        """;
}
