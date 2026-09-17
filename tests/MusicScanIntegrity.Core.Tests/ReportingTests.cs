using System.Text;
using MusicScanIntegrity.Core.Audio;
using MusicScanIntegrity.Core.Common;
using MusicScanIntegrity.Core.Models;
using MusicScanIntegrity.Core.Reporting;
using MusicScanIntegrity.Core.Settings;
using Xunit;

namespace MusicScanIntegrity.Core.Tests;

public sealed class ReportExporterTests
{
    private static FileCheckResult Result(
        string name,
        CheckStatus status = CheckStatus.Ok,
        CheckIssue? issue = null,
        long size = 1024,
        string format = "FLAC") => new()
        {
            FullPath = @"D:\Music\" + name,
            FileName = name,
            DirectoryPath = @"D:\Music",
            SizeBytes = size,
            Status = status,
            Issues = issue is null ? [] : [issue],
            Format = format,
        };

    private static ReportData Data(params FileCheckResult[] results) => new(
        new ScanSummary
        {
            RootPath = @"D:\Music\Коллекция",
            Counters = new ScanCounters(results.Length, results.Length, results.Count(r => r.Status == CheckStatus.Ok), results.Count(r => r.Status == CheckStatus.Corrupted), results.Count(r => r.Status == CheckStatus.Warning), results.Count(r => r.Status == CheckStatus.Skipped)),
            StartedAt = new DateTimeOffset(2026, 9, 2, 21, 4, 0, TimeSpan.Zero),
            Duration = TimeSpan.FromMinutes(18) + TimeSpan.FromSeconds(42),
            Parallelism = 8,
        },
        results,
        [],
        new DateTimeOffset(2026, 9, 2, 21, 23, 0, TimeSpan.Zero));

    private static async Task<string> RenderAsync(IReportExporter exporter, ReportData data)
    {
        using MemoryStream stream = new();
        await exporter.WriteAsync(data, stream);
        return Encoding.UTF8.GetString(stream.ToArray()).TrimStart('\uFEFF');
    }

    [Fact]
    public async Task Collection_findings_appear_in_every_report()
    {
        ReportData data = Data(Result("01.flac")) with
        {
            Findings =
            [
                new CollectionFinding(
                    CollectionFindingKind.MissingTracks,
                    @"D:\Music\Альбом",
                    "В альбоме не хватает дорожек: 3.",
                    "Найдено 4 из 5 по нумерации в тегах"),
                new CollectionFinding(
                    CollectionFindingKind.Duplicate,
                    @"D:\Music\Альбом\2.flac",
                    "Тот же трек лежит ещё в 1 месте: Группа — Вторая."),
            ],
        };

        string html = await RenderAsync(new HtmlReportExporter(), data);
        string csv = await RenderAsync(new CsvReportExporter(), data);
        string text = await RenderAsync(new TextReportExporter(), data);

        foreach (string report in new[] { html, csv, text })
        {
            Assert.Contains("не хватает дорожек", report, StringComparison.Ordinal);
            Assert.Contains("Дубликат", report, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Html_escapes_brackets_and_quotes_in_file_names()
    {
        FileCheckResult dangerous = Result("""<script>alert("x")</script>.mp3""", CheckStatus.Corrupted,
            new CheckIssue(IssueCode.DecodeStartFailed, """Не открылся файл "a" & <b>"""));

        string html = await RenderAsync(new HtmlReportExporter(), Data(dangerous));

        // The report carries exactly one script of its own, the row filter. A file
        // named "<script>" must stay text and never become a second script.
        Assert.Equal(1, Occurrences(html, "<script"));
        Assert.DoesNotContain("<script>alert(", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("&quot;", html, StringComparison.Ordinal);
        Assert.Contains("&amp;", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Html_contains_summary_for_all_statuses()
    {
        string html = await RenderAsync(new HtmlReportExporter(), Data(
            Result("ok.flac"),
            Result("bad.mp3", CheckStatus.Corrupted, new CheckIssue(IssueCode.AudioReadFailed, "битый")),
            Result("warn.wav", CheckStatus.Warning, new CheckIssue(IssueCode.MetadataProblem, "нет тегов")),
            Result("skip.wv", CheckStatus.Skipped, new CheckIssue(IssueCode.LockedSkippedByUser, "занят"))));

        Assert.Contains("В порядке", html, StringComparison.Ordinal);
        Assert.Contains("Повреждено", html, StringComparison.Ordinal);
        Assert.Contains("Предупреждения", html, StringComparison.Ordinal);
        Assert.Contains("Пропущено", html, StringComparison.Ordinal);
        Assert.Contains("Время проверки", html, StringComparison.Ordinal);
        Assert.Contains("18:42", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Html_never_conveys_status_by_colour_alone()
    {
        string html = await RenderAsync(new HtmlReportExporter(), Data(
            Result("bad.mp3", CheckStatus.Corrupted, new CheckIssue(IssueCode.AudioReadFailed, "битый"))));

        // A coloured pill must be accompanied by a glyph and a word.
        Assert.Contains("✕", html, StringComparison.Ordinal);
        Assert.Contains("Повреждён", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Html_is_valid_xml_even_with_hostile_names()
    {
        string html = await RenderAsync(new HtmlReportExporter(), Data(
            Result("""a"b<c>d&e.mp3""", CheckStatus.Corrupted, new CheckIssue(IssueCode.AudioReadFailed, "<>&\""))));

        // Crude but effective: no unescaped "<" outside tags. Scripts are excluded:
        // inside <script> the content is raw text and a "<" in a loop condition breaks
        // nothing. Without the exclusion the test would pass only by coincidence of
        // bracket counts in the script itself.
        string markup = WithoutScripts(html);
        int openTags = markup.Count(c => c == '<');
        int closeTags = markup.Count(c => c == '>');
        Assert.Equal(openTags, closeTags);
    }

    [Fact]
    public async Task Html_offers_status_chips_from_table()
    {
        string html = await RenderAsync(new HtmlReportExporter(), Data(
            Result("ok.flac"),
            Result("bad.mp3", CheckStatus.Corrupted, new CheckIssue(IssueCode.AudioReadFailed, "битый")),
            Result("warn.wav", CheckStatus.Warning, new CheckIssue(IssueCode.MetadataProblem, "нет тегов"))));

        // Chip captions match the results tab in the application.
        Assert.Contains("Повреждено", html, StringComparison.Ordinal);
        Assert.Contains("Предупреждения", html, StringComparison.Ordinal);

        Assert.Contains(Attr("data-filter", "all"), html, StringComparison.Ordinal);
        Assert.Contains(Attr("data-filter", "ok"), html, StringComparison.Ordinal);
        Assert.Contains(Attr("data-filter", "err"), html, StringComparison.Ordinal);
        Assert.Contains(Attr("data-filter", "warn"), html, StringComparison.Ordinal);

        // No skipped rows in the table, so no chip for them: a filter known to be
        // empty is not worth offering.
        Assert.DoesNotContain(Attr("data-filter", "skip"), html, StringComparison.Ordinal);
    }

    /// <remarks>
    /// Healthy files are excluded by default. An "ok" chip counting from the summary
    /// would promise rows that are not in the file and show an empty table.
    /// </remarks>
    [Fact]
    public async Task Chip_counts_come_from_table_not_summary()
    {
        // 500 files checked, 499 healthy, but only the corrupted one is in the report:
        // that is the default setting at work.
        FileCheckResult[] inReport =
            [Result("bad.mp3", CheckStatus.Corrupted, new CheckIssue(IssueCode.AudioReadFailed, "битый"))];

        ReportData data = new(
            new ScanSummary
            {
                RootPath = @"D:\Music\Коллекция",
                Counters = new ScanCounters(500, 500, 499, 1, 0, 0),
                StartedAt = new DateTimeOffset(2026, 9, 2, 21, 4, 0, TimeSpan.Zero),
                Duration = TimeSpan.FromMinutes(3),
                Parallelism = 8,
            },
            inReport,
            [],
            new DateTimeOffset(2026, 9, 2, 21, 23, 0, TimeSpan.Zero));

        string html = await RenderAsync(new HtmlReportExporter(), data);

        Assert.DoesNotContain(Attr("data-filter", "ok"), html, StringComparison.Ordinal);
        Assert.Contains("В таблице только файлы с замечаниями", html, StringComparison.Ordinal);
        Assert.Contains("499", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Html_offers_format_choice_with_counts()
    {
        string html = await RenderAsync(new HtmlReportExporter(), Data(
            Result("a.mp3", CheckStatus.Corrupted, new CheckIssue(IssueCode.AudioReadFailed, "битый"), format: "MP3"),
            Result("b.mp3", CheckStatus.Corrupted, new CheckIssue(IssueCode.AudioReadFailed, "битый"), format: "MP3"),
            Result("c.flac", CheckStatus.Warning, new CheckIssue(IssueCode.MetadataProblem, "нет тегов"))));

        Assert.Contains("Формат: все", html, StringComparison.Ordinal);
        Assert.Contains("MP3 · 2", html, StringComparison.Ordinal);
        Assert.Contains("FLAC · 1", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_table_row_is_tagged_for_filtering()
    {
        string html = await RenderAsync(new HtmlReportExporter(), Data(
            Result("Тихая песня.mp3", CheckStatus.Warning,
                new CheckIssue(IssueCode.MetadataProblem, "нет тегов"), format: "MP3")));

        Assert.Contains(Attr("data-status", "warn"), html, StringComparison.Ordinal);
        Assert.Contains(Attr("data-format", "MP3"), html, StringComparison.Ordinal);

        // The search key is lower-cased: the search box lower-cases input too, so the
        // search is case-insensitive.
        Assert.Contains("тихая песня.mp3", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reports are opened offline, from a stick or an email attachment; a single
    /// external reference would break the page.
    /// </summary>
    [Fact]
    public async Task Report_has_no_external_references()
    {
        string html = await RenderAsync(new HtmlReportExporter(), Data(
            Result("bad.mp3", CheckStatus.Corrupted, new CheckIssue(IssueCode.AudioReadFailed, "битый"))));

        Assert.DoesNotContain("http://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<link", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("src=", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Empty_report_has_no_filter_bar()
    {
        string html = await RenderAsync(new HtmlReportExporter(), Data());

        Assert.DoesNotContain("data-filter", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.Ordinal);
    }

    /// <summary>A markup attribute as it appears in the report.</summary>
    private static string Attr(string name, string value) => name + "=\"" + value + "\"";

    /// <remarks>
    /// Otherwise the export dialog would offer Documents every time and the path
    /// would have to be picked again on every save.
    /// </remarks>
    [Fact]
    public void Different_folder_is_remembered()
    {
        ReportService service = new();

        string? folder = service.FolderToRemember(
            @"D:\Отчёты\отчёт.html",
            @"C:\Users\Hser\Documents");

        Assert.Equal(@"D:\Отчёты", folder);
    }

    /// <remarks>
    /// Storing Documents explicitly is not the same as leaving the setting empty:
    /// empty means "let the app decide" and follows a relocated Documents folder,
    /// while a stored path does not.
    /// </remarks>
    [Fact]
    public void Saving_to_default_folder_changes_nothing()
    {
        ReportService service = new();

        Assert.Null(service.FolderToRemember(
            @"C:\Users\Hser\Documents\отчёт.html",
            @"C:\Users\Hser\Documents"));
    }

    /// <summary>The same folder spelled differently is still the same folder.</summary>
    [Theory]
    [InlineData(@"D:\Отчёты\")]
    [InlineData(@"d:\отчёты")]
    [InlineData(@"D:\Отчёты\.")]
    public void Same_folder_spelled_differently_is_not_different(string sameFolder)
    {
        ReportService service = new();

        Assert.Null(service.FolderToRemember(@"D:\Отчёты\отчёт.html", sameFolder));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("отчёт.html")]
    public void Path_without_folder_is_not_remembered(string path)
    {
        ReportService service = new();

        Assert.Null(service.FolderToRemember(path, @"C:\Users\Hser\Documents"));
    }

    /// <remarks>
    /// Anything can be typed into the settings file by hand; the app must not crash
    /// on it.
    /// </remarks>
    [Fact]
    public void Invalid_default_folder_does_not_interfere()
    {
        ReportService service = new();

        string? folder = service.FolderToRemember(@"D:\Отчёты\отчёт.html", "|<>*?");

        Assert.Equal(@"D:\Отчёты", folder);
    }

    /// <summary>How many times a string occurs in the text.</summary>
    private static int Occurrences(string text, string value)
    {
        int count = 0;
        for (int i = text.IndexOf(value, StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    /// <summary>The report with script element contents removed.</summary>
    private static string WithoutScripts(string html)
    {
        while (true)
        {
            int start = html.IndexOf("<script", StringComparison.Ordinal);
            if (start < 0)
            {
                return html;
            }

            int end = html.IndexOf("</script>", start, StringComparison.Ordinal);
            Assert.True(end > start, "элемент script не закрыт");
            html = html.Remove(start, end - start + "</script>".Length);
        }
    }

    [Fact]
    public async Task Csv_escapes_separator_and_quotes()
    {
        string csv = await RenderAsync(new CsvReportExporter(), Data(
            Result("""трек; с "кавычками".mp3""", CheckStatus.Corrupted, new CheckIssue(IssueCode.AudioReadFailed, "текст; с разделителем"))));

        Assert.Contains(""""трек; с ""кавычками"".mp3"""", csv, StringComparison.Ordinal);
        Assert.StartsWith("sep=;", csv, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("простой", "простой")]
    [InlineData("с;разделителем", "\"с;разделителем\"")]
    [InlineData("с\"кавычкой", "\"с\"\"кавычкой\"")]
    [InlineData("с\nпереносом", "\"с\nпереносом\"")]
    public void Csv_escaping_follows_rules(string input, string expected)
    {
        Assert.Equal(expected, CsvReportExporter.Escape(input));
    }

    [Fact]
    public async Task Csv_starts_with_bom_so_excel_reads_cyrillic()
    {
        using MemoryStream stream = new();
        await new CsvReportExporter().WriteAsync(Data(Result("трек.mp3")), stream);

        byte[] bytes = stream.ToArray();
        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);
    }

    [Fact]
    public async Task Text_report_groups_files_by_status()
    {
        string text = await RenderAsync(new TextReportExporter(), Data(
            Result("ok.flac"),
            Result("bad.mp3", CheckStatus.Corrupted, new CheckIssue(IssueCode.AudioReadFailed, "битый поток"))));

        Assert.Contains("СВОДКА", text, StringComparison.Ordinal);
        Assert.Contains("ПОВРЕЖДЁН", text, StringComparison.Ordinal);
        Assert.Contains("bad.mp3", text, StringComparison.Ordinal);
        Assert.Contains("битый поток", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stopped_scan_is_marked_in_every_report()
    {
        ReportData data = Data(Result("ok.flac")) with
        {
            Summary = new ScanSummary
            {
                RootPath = @"D:\Music",
                Counters = new ScanCounters(100, 42, 42, 0, 0, 0),
                StartedAt = DateTimeOffset.Now,
                Duration = TimeSpan.FromMinutes(2),
                Parallelism = 8,
                WasStopped = true,
            },
        };

        Assert.Contains("останов", await RenderAsync(new HtmlReportExporter(), data), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("останов", await RenderAsync(new TextReportExporter(), data), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("останов", await RenderAsync(new CsvReportExporter(), data), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://x\")", "\"'=HYPERLINK(\"\"http://x\"\")\"")]
    [InlineData("+1+1", "'+1+1")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("-Трек 01.flac", "'-Трек 01.flac")]
    public void Formula_cell_is_neutralised(string value, string expected)
    {
        // File names are not ours to choose: a file named "=HYPERLINK(...)" would run
        // as a formula when opened in Excel. The apostrophe marks the cell as text and
        // is not displayed.
        Assert.Equal(expected, CsvReportExporter.Escape(value));
    }

    [Theory]
    [InlineData("Трек 01.flac")]
    [InlineData("2+2.mp3")]
    [InlineData("")]
    public void Ordinary_cell_is_untouched(string value)
    {
        Assert.Equal(value, CsvReportExporter.Escape(value));
    }

    [Fact]
    public async Task Reports_follow_english_interface_language()
    {
        using CultureScope _ = new(AppLanguage.English);
        ReportData data = Data(
            Result("a.flac", CheckStatus.Corrupted, new CheckIssue(IssueCode.FileNotFound, "x"), size: 25_270_000),
            Result("b.flac"));

        string html = await RenderAsync(new HtmlReportExporter(), data);
        string csv = await RenderAsync(new CsvReportExporter(), data);
        string text = await RenderAsync(new TextReportExporter(), data);

        Assert.Contains("<html lang=\"en\">", html, StringComparison.Ordinal);
        Assert.Contains("Showing all 2 files", html, StringComparison.Ordinal);
        Assert.Contains("data-some=\"Showing {0} of {1}\"", html, StringComparison.Ordinal);
        Assert.Contains("24.1&#160;MB", html, StringComparison.Ordinal);
        Assert.Contains("Total files", csv, StringComparison.Ordinal);
        Assert.Contains("COLLECTION SCAN REPORT", text.ToUpperInvariant(), StringComparison.Ordinal);

        foreach (string report in new[] { WithoutScripts(html), csv, text })
        {
            // The folder name is data and stays as it is.
            Assert.DoesNotContain(report.Replace("Коллекция", string.Empty, StringComparison.Ordinal), IsCyrillic);
        }
    }

    private static bool IsCyrillic(char c) => c is >= (char)0x0400 and <= (char)0x04FF;

    [Fact]
    public async Task Russian_html_report_keeps_three_plural_forms()
    {
        string html = await RenderAsync(new HtmlReportExporter(), Data(Result("a.flac"), Result("b.flac")));

        Assert.Contains("<html lang=\"ru\">", html, StringComparison.Ordinal);
        Assert.Contains("Показаны все 2 файла", html, StringComparison.Ordinal);
        Assert.Contains("data-many=\"Показаны все {0} файлов\"", html, StringComparison.Ordinal);
    }
}

public sealed class ReportServiceTests
{
    private static FileCheckResult Result(string name, CheckStatus status) => new()
    {
        FullPath = @"D:\Music\" + name,
        FileName = name,
        DirectoryPath = @"D:\Music",
        SizeBytes = 1024,
        Status = status,
        Issues = [],
        Format = "FLAC",
    };

    [Fact]
    public void Report_includes_only_problem_files_by_default()
    {
        ReportService service = new();

        IReadOnlyList<FileCheckResult> filtered = service.Filter(
            [Result("a.flac", CheckStatus.Ok), Result("b.mp3", CheckStatus.Corrupted)],
            new AppSettings());

        Assert.Single(filtered);
        Assert.Equal("b.mp3", filtered[0].FileName);
    }

    [Fact]
    public void Setting_includes_healthy_files()
    {
        ReportService service = new();

        IReadOnlyList<FileCheckResult> filtered = service.Filter(
            [Result("a.flac", CheckStatus.Ok), Result("b.mp3", CheckStatus.Corrupted)],
            new AppSettings { IncludeOkFilesInReport = true });

        Assert.Equal(2, filtered.Count);
        // Corrupted files first; they are what the report is opened for.
        Assert.Equal("b.mp3", filtered[0].FileName);
    }

    [Fact]
    public async Task Saving_creates_file_without_temporary_leftovers()
    {
        using TempDirectory temp = new();
        ReportService service = new();
        string path = Path.Combine(temp.Path, "reports", "report.html");

        ReportData data = new(
            new ScanSummary
            {
                RootPath = @"D:\Music",
                Counters = new ScanCounters(1, 1, 1, 0, 0, 0),
                StartedAt = DateTimeOffset.Now,
                Duration = TimeSpan.FromSeconds(5),
                Parallelism = 4,
            },
            [Result("a.flac", CheckStatus.Ok)],
            [],
            DateTimeOffset.Now);

        await service.SaveAsync(ReportFormat.Html, path, data);

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".part"));
    }

    [Theory]
    [InlineData(ReportFormat.Html, ".html")]
    [InlineData(ReportFormat.Csv, ".csv")]
    [InlineData(ReportFormat.Text, ".txt")]
    public void Report_file_name_matches_format(ReportFormat format, string extension)
    {
        ReportService service = new();
        string name = service.SuggestFileName(format, new DateTimeOffset(2026, 9, 2, 21, 4, 0, TimeSpan.Zero));

        Assert.Equal($"musicscan_2026-09-02_2104{extension}", name);
    }
}

public sealed class FormatTests
{
    [Theory]
    [InlineData(0, "0 Б")]
    [InlineData(512, "512 Б")]
    [InlineData(1024, "1 КБ")]
    [InlineData(626_688, "612 КБ")]
    [InlineData(3_984_589, "3,8 МБ")]
    [InlineData(1_932_735_283, "1,8 ГБ")]
    [InlineData(25_260_032, "24,1 МБ")]
    [InlineData(297_795_584, "284 МБ")]
    public void Sizes_are_formatted_as_designed(long bytes, string expected)
    {
        Assert.Equal(expected.Replace(' ', Format.NarrowSpace), Format.Size(bytes));
    }

    [Fact]
    public void Digit_groups_use_non_breaking_space()
    {
        Assert.Equal($"12{Format.NarrowSpace}480", Format.Number(12480));
    }

    [Theory]
    [InlineData(1, "1 файл")]
    [InlineData(2, "2 файла")]
    [InlineData(5, "5 файлов")]
    [InlineData(11, "11 файлов")]
    [InlineData(21, "21 файл")]
    [InlineData(102, "102 файла")]
    public void Plural_forms_are_correct(int count, string expected)
    {
        Assert.Equal(expected, Format.Files(count));
    }

    [Fact]
    public void Scan_duration_is_formatted_as_designed()
    {
        Assert.Equal("18:42", Format.Duration(TimeSpan.FromMinutes(18) + TimeSpan.FromSeconds(42)));
        Assert.Equal("1:05:03", Format.Duration(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(3)));
    }
}

public sealed class CheckStatusIconTests
{
    [Theory]
    [InlineData(CheckStatus.Ok, "status-ok", "mark-ok")]
    [InlineData(CheckStatus.Warning, "status-warning", "mark-warning")]
    [InlineData(CheckStatus.Corrupted, "status-broken", "mark-broken")]
    [InlineData(CheckStatus.Skipped, "status-skipped", "mark-skipped")]
    public void Every_status_has_both_icons(CheckStatus status, string icon, string mark)
    {
        Assert.Equal(icon, status.IconKey());
        Assert.Equal(mark, status.MarkKey());
    }
}

public sealed class ContentTypeSnifferTests
{
    [Theory]
    [InlineData("fLaC", "FLAC")]
    [InlineData("MAC ", "APE")]
    [InlineData("wvpk", "WV")]
    [InlineData("MThd", "MIDI")]
    [InlineData("IMPM", "IT")]
    public void Format_is_detected_by_signature(string signature, string expected)
    {
        byte[] header = new byte[64];
        Encoding.ASCII.GetBytes(signature).CopyTo(header, 0);

        Assert.Equal(expected, ContentTypeSniffer.Detect(header));
    }

    [Fact]
    public void Wav_is_detected_by_riff_and_wave()
    {
        byte[] header = new byte[64];
        "RIFF"u8.ToArray().CopyTo(header, 0);
        "WAVE"u8.ToArray().CopyTo(header, 8);

        Assert.Equal("WAV", ContentTypeSniffer.Detect(header));
    }

    [Fact]
    public void Id3_counts_as_mp3()
    {
        byte[] header = new byte[64];
        "ID3"u8.ToArray().CopyTo(header, 0);

        Assert.Equal("MP3", ContentTypeSniffer.Detect(header));
    }

    [Theory]
    [InlineData(".flac", "FLAC", true)]
    [InlineData(".mp3", "FLAC", false)]
    [InlineData(".mid", "MIDI", true)]
    [InlineData(".aif", "AIFF", true)]
    [InlineData(".m4a", "ALAC", true)]
    [InlineData(".ogg", "OPUS", true)]
    [InlineData(".wav", "MP3", false)]
    public void Extension_matches_contents(string extension, string detected, bool expected)
    {
        Assert.Equal(expected, ContentTypeSniffer.Matches(extension, detected));
    }

    [Fact]
    public void Unknown_signature_is_not_reported()
    {
        Assert.Null(ContentTypeSniffer.Detect(new byte[64]));
    }

}
