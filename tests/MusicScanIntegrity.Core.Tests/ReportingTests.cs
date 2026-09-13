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
    public async Task Замечания_по_коллекции_попадают_во_все_отчёты()
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
    public async Task Html_экранирует_угловые_скобки_и_кавычки_в_именах_файлов()
    {
        FileCheckResult dangerous = Result("""<script>alert("x")</script>.mp3""", CheckStatus.Corrupted,
            new CheckIssue(IssueCode.DecodeStartFailed, """Не открылся файл "a" & <b>"""));

        string html = await RenderAsync(new HtmlReportExporter(), Data(dangerous));

        // Свой скрипт в отчёте ровно один — отбор строк. Имя файла, в котором
        // написано «<script>», обязано остаться текстом и вторым скриптом не стать.
        Assert.Equal(1, Occurrences(html, "<script"));
        Assert.DoesNotContain("<script>alert(", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("&quot;", html, StringComparison.Ordinal);
        Assert.Contains("&amp;", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Html_содержит_сводку_по_всем_статусам()
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
    public async Task Html_не_передаёт_статус_одним_только_цветом()
    {
        string html = await RenderAsync(new HtmlReportExporter(), Data(
            Result("bad.mp3", CheckStatus.Corrupted, new CheckIssue(IssueCode.AudioReadFailed, "битый"))));

        // Рядом с цветной пилюлей обязателен значок и слово.
        Assert.Contains("✕", html, StringComparison.Ordinal);
        Assert.Contains("Повреждён", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Html_валиден_как_xml_даже_с_опасными_именами()
    {
        string html = await RenderAsync(new HtmlReportExporter(), Data(
            Result("""a"b<c>d&e.mp3""", CheckStatus.Corrupted, new CheckIssue(IssueCode.AudioReadFailed, "<>&\""))));

        // Грубая, но действенная проверка: неэкранированных «<» вне тегов быть не должно.
        // Скрипт из подсчёта исключается: внутри <script> это сырой текст, и «<»
        // в условии цикла разметку не ломает. Без исключения тест сходился бы
        // случайно — по числу скобок в самом скрипте.
        string markup = WithoutScripts(html);
        int openTags = markup.Count(c => c == '<');
        int closeTags = markup.Count(c => c == '>');
        Assert.Equal(openTags, closeTags);
    }

    [Fact]
    public async Task Html_даёт_кнопки_отбора_по_статусам_из_таблицы()
    {
        string html = await RenderAsync(new HtmlReportExporter(), Data(
            Result("ok.flac"),
            Result("bad.mp3", CheckStatus.Corrupted, new CheckIssue(IssueCode.AudioReadFailed, "битый")),
            Result("warn.wav", CheckStatus.Warning, new CheckIssue(IssueCode.MetadataProblem, "нет тегов"))));

        // Слова на кнопках — те же, что на вкладке «Результаты» в программе.
        Assert.Contains("Повреждено", html, StringComparison.Ordinal);
        Assert.Contains("Предупреждения", html, StringComparison.Ordinal);

        Assert.Contains(Attr("data-filter", "all"), html, StringComparison.Ordinal);
        Assert.Contains(Attr("data-filter", "ok"), html, StringComparison.Ordinal);
        Assert.Contains(Attr("data-filter", "err"), html, StringComparison.Ordinal);
        Assert.Contains(Attr("data-filter", "warn"), html, StringComparison.Ordinal);

        // Пропущенных в таблице нет — и кнопки для них быть не должно:
        // нажимать на заведомо пустой отбор незачем.
        Assert.DoesNotContain(Attr("data-filter", "skip"), html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Счётчики на кнопках считаются по строкам таблицы, а не по сводке.
    /// </summary>
    /// <remarks>
    /// По умолчанию исправные файлы в отчёт не попадают. Кнопка «В порядке»
    /// со счётчиком из сводки обещала бы строки, которых в файле нет, — и при
    /// нажатии показала бы пустую таблицу.
    /// </remarks>
    [Fact]
    public async Task Счётчики_на_кнопках_считаются_по_таблице_а_не_по_сводке()
    {
        // Проверено 500 файлов, из них 499 исправны — но в отчёт попал только
        // повреждённый: так и работает настройка по умолчанию.
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
    public async Task Html_даёт_выбор_по_форматам_с_числом_файлов()
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
    public async Task Каждая_строка_таблицы_помечена_для_отбора()
    {
        string html = await RenderAsync(new HtmlReportExporter(), Data(
            Result("Тихая песня.mp3", CheckStatus.Warning,
                new CheckIssue(IssueCode.MetadataProblem, "нет тегов"), format: "MP3")));

        Assert.Contains(Attr("data-status", "warn"), html, StringComparison.Ordinal);
        Assert.Contains(Attr("data-format", "MP3"), html, StringComparison.Ordinal);

        // Строка для поиска — в нижнем регистре: поле поиска приводит
        // введённое к нему же, иначе поиск зависел бы от регистра.
        Assert.Contains("тихая песня.mp3", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Отчёт открывают без сети — с флешки, из почты. Ни одной внешней ссылки
    /// в нём быть не должно, иначе страница развалится.
    /// </summary>
    [Fact]
    public async Task Отчёт_не_ссылается_ни_на_что_снаружи()
    {
        string html = await RenderAsync(new HtmlReportExporter(), Data(
            Result("bad.mp3", CheckStatus.Corrupted, new CheckIssue(IssueCode.AudioReadFailed, "битый"))));

        Assert.DoesNotContain("http://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<link", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("src=", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Пустой_отчёт_обходится_без_панели_отбора()
    {
        string html = await RenderAsync(new HtmlReportExporter(), Data());

        Assert.DoesNotContain("data-filter", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.Ordinal);
    }

    /// <summary>Атрибут разметки в том виде, в каком он попадает в отчёт.</summary>
    private static string Attr(string name, string value) => name + "=\"" + value + "\"";

    /// <summary>
    /// Папка, выбранная человеком, запоминается.
    /// </summary>
    /// <remarks>
    /// Иначе диалог экспорта каждый раз предлагал бы «Документы», и путь
    /// приходилось бы указывать заново при каждом сохранении.
    /// </remarks>
    [Fact]
    public void Другая_папка_запоминается()
    {
        ReportService service = new();

        string? folder = service.FolderToRemember(
            @"D:\Отчёты\отчёт.html",
            @"C:\Users\Hser\Documents");

        Assert.Equal(@"D:\Отчёты", folder);
    }

    /// <summary>
    /// Сохранение туда же, куда предлагалось, настройку не трогает.
    /// </summary>
    /// <remarks>
    /// Записать «Документы» явно — не то же самое, что оставить настройку
    /// пустой: пустая означает «как решит программа» и переезжает вместе с
    /// системной папкой документов, записанная — нет.
    /// </remarks>
    [Fact]
    public void Сохранение_в_ту_же_папку_ничего_не_меняет()
    {
        ReportService service = new();

        Assert.Null(service.FolderToRemember(
            @"C:\Users\Hser\Documents\отчёт.html",
            @"C:\Users\Hser\Documents"));
    }

    /// <summary>
    /// Та же папка, записанная иначе, — всё ещё та же папка.
    /// </summary>
    [Theory]
    [InlineData(@"D:\Отчёты\")]
    [InlineData(@"d:\отчёты")]
    [InlineData(@"D:\Отчёты\.")]
    public void Разная_запись_одной_папки_не_считается_другой(string sameFolder)
    {
        ReportService service = new();

        Assert.Null(service.FolderToRemember(@"D:\Отчёты\отчёт.html", sameFolder));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("отчёт.html")]
    public void Путь_без_папки_запоминать_нечего(string path)
    {
        ReportService service = new();

        Assert.Null(service.FolderToRemember(path, @"C:\Users\Hser\Documents"));
    }

    /// <summary>
    /// Мусор вместо папки по умолчанию не мешает запомнить настоящую.
    /// </summary>
    /// <remarks>
    /// В файл настроек можно вписать что угодно руками — на этом программа
    /// падать не должна.
    /// </remarks>
    [Fact]
    public void Негодная_папка_по_умолчанию_не_мешает()
    {
        ReportService service = new();

        string? folder = service.FolderToRemember(@"D:\Отчёты\отчёт.html", "|<>*?");

        Assert.Equal(@"D:\Отчёты", folder);
    }

    /// <summary>Сколько раз строка встречается в тексте.</summary>
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

    /// <summary>Отчёт без содержимого элементов script.</summary>
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
    public async Task Csv_экранирует_разделитель_и_кавычки()
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
    public void Csv_экранирование_по_правилам(string input, string expected)
    {
        Assert.Equal(expected, CsvReportExporter.Escape(input));
    }

    [Fact]
    public async Task Csv_начинается_с_bom_чтобы_excel_не_ломал_кириллицу()
    {
        using MemoryStream stream = new();
        await new CsvReportExporter().WriteAsync(Data(Result("трек.mp3")), stream);

        byte[] bytes = stream.ToArray();
        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);
    }

    [Fact]
    public async Task Текстовый_отчёт_группирует_файлы_по_статусам()
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
    public async Task Остановленная_проверка_помечается_во_всех_отчётах()
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
    public void Ячейка_с_формулой_обезвреживается(string value, string expected)
    {
        // Имена файлов придумывали не мы: файл, названный «=HYPERLINK(...)»,
        // при открытии отчёта в Excel выполнился бы как формула. Апостроф
        // говорит табличной программе «это текст» и в ячейке не показывается.
        Assert.Equal(expected, CsvReportExporter.Escape(value));
    }

    [Theory]
    [InlineData("Трек 01.flac")]
    [InlineData("2+2.mp3")]
    [InlineData("")]
    public void Обычная_ячейка_не_трогается(string value)
    {
        Assert.Equal(value, CsvReportExporter.Escape(value));
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
    public void По_умолчанию_в_отчёт_попадают_только_проблемные_файлы()
    {
        ReportService service = new();

        IReadOnlyList<FileCheckResult> filtered = service.Filter(
            [Result("a.flac", CheckStatus.Ok), Result("b.mp3", CheckStatus.Corrupted)],
            new AppSettings());

        Assert.Single(filtered);
        Assert.Equal("b.mp3", filtered[0].FileName);
    }

    [Fact]
    public void Настройка_включает_в_отчёт_и_исправные_файлы()
    {
        ReportService service = new();

        IReadOnlyList<FileCheckResult> filtered = service.Filter(
            [Result("a.flac", CheckStatus.Ok), Result("b.mp3", CheckStatus.Corrupted)],
            new AppSettings { IncludeOkFilesInReport = true });

        Assert.Equal(2, filtered.Count);
        // Повреждённые идут первыми — ради них отчёт и открывают.
        Assert.Equal("b.mp3", filtered[0].FileName);
    }

    [Fact]
    public async Task Сохранение_создаёт_файл_и_не_оставляет_временных_хвостов()
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
    public void Имя_файла_отчёта_соответствует_формату(ReportFormat format, string extension)
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
    public void Размеры_форматируются_как_в_макете(long bytes, string expected)
    {
        Assert.Equal(expected.Replace(' ', Format.NarrowSpace), Format.Size(bytes));
    }

    [Fact]
    public void Разряды_разделяются_неразрывным_пробелом()
    {
        Assert.Equal($"12{Format.NarrowSpace}480", Format.Number(12480));
    }

    [Theory]
    [InlineData(1, "файл")]
    [InlineData(2, "файла")]
    [InlineData(5, "файлов")]
    [InlineData(11, "файлов")]
    [InlineData(21, "файл")]
    [InlineData(102, "файла")]
    public void Склонение_считается_правильно(int count, string expected)
    {
        Assert.Equal(expected, Format.Plural(count, "файл", "файла", "файлов"));
    }

    [Fact]
    public void Время_проверки_выводится_как_в_макете()
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
    public void У_каждого_статуса_есть_обе_иконки(CheckStatus status, string icon, string mark)
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
    public void Формат_определяется_по_сигнатуре(string signature, string expected)
    {
        byte[] header = new byte[64];
        Encoding.ASCII.GetBytes(signature).CopyTo(header, 0);

        Assert.Equal(expected, ContentTypeSniffer.Detect(header));
    }

    [Fact]
    public void Wav_узнаётся_по_riff_и_wave()
    {
        byte[] header = new byte[64];
        "RIFF"u8.ToArray().CopyTo(header, 0);
        "WAVE"u8.ToArray().CopyTo(header, 8);

        Assert.Equal("WAV", ContentTypeSniffer.Detect(header));
    }

    [Fact]
    public void Id3_считается_mp3()
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
    public void Совпадение_расширения_и_содержимого(string extension, string detected, bool expected)
    {
        Assert.Equal(expected, ContentTypeSniffer.Matches(extension, detected));
    }

    [Fact]
    public void Неизвестная_сигнатура_не_повод_жаловаться()
    {
        Assert.Null(ContentTypeSniffer.Detect(new byte[64]));
    }

}
