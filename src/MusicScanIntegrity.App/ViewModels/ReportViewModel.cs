using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MusicScanIntegrity.Core.Models;
using MusicScanIntegrity.Core.Settings;
using CoreFormat = MusicScanIntegrity.Core.Common.Format;

namespace MusicScanIntegrity.App.ViewModels;

/// <summary>Вкладка «Отчёт»: KPI, распределение по статусам, папки, выбор формата.</summary>
public sealed partial class ReportViewModel : ObservableObject
{
    [ObservableProperty]
    private string _totalFiles = "0";

    [ObservableProperty]
    private string _totalFilesNote = "в 0 папках";

    [ObservableProperty]
    private string _corruptedCount = "0";

    [ObservableProperty]
    private string _corruptedNote = "0 % коллекции";

    [ObservableProperty]
    private string _warningCount = "0";

    [ObservableProperty]
    private string _elapsed = "0:00";

    [ObservableProperty]
    private string _elapsedNote = "проверка не запускалась";

    [ObservableProperty]
    private ReportFormat _selectedFormat = ReportFormat.Html;

    /// <summary>Папка, в которую ляжет отчёт по умолчанию.</summary>
    [ObservableProperty]
    private string _targetFolder = string.Empty;

    [ObservableProperty]
    private bool _hasSummary;

    // Доли для полосы распределения; в сумме дают ширину полосы.
    [ObservableProperty]
    private int _okShare;

    [ObservableProperty]
    private int _corruptedShare;

    [ObservableProperty]
    private int _warningShare;

    [ObservableProperty]
    private int _skippedShare;

    [ObservableProperty]
    private string _okLabel = "0";

    [ObservableProperty]
    private string _corruptedLabel = "0";

    [ObservableProperty]
    private string _warningLabel = "0";

    [ObservableProperty]
    private string _skippedLabel = "0";

    /// <summary>Проверенные папки: путь, файлов, повреждено.</summary>
    public ObservableCollection<FolderRow> Folders { get; } = [];

    /// <summary>
    /// Замечания по коллекции: пропуски в нумерации, разнобой форматов, повторы.
    /// </summary>
    /// <remarks>
    /// Живут отдельно от списка результатов, потому что относятся к папке или к
    /// паре файлов, а не к одному файлу.
    /// </remarks>
    public ObservableCollection<CollectionFinding> Findings { get; } = [];

    /// <summary>Замечания по коллекции есть — показывать карточку.</summary>
    [ObservableProperty]
    private bool _hasFindings;

    /// <summary>Подпись под списком замечаний.</summary>
    [ObservableProperty]
    private string _findingsNote = string.Empty;

    /// <summary>Заполняет список замечаний по коллекции.</summary>
    /// <param name="findings">Найденное разбором альбомов и поиском повторов.</param>
    public void UpdateFindings(IReadOnlyList<CollectionFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);

        const int limit = 200;

        Findings.Clear();
        foreach (CollectionFinding finding in findings.Take(limit))
        {
            Findings.Add(finding);
        }

        HasFindings = findings.Count > 0;
        FindingsNote = findings.Count > limit
            ? $"Показаны {limit} замечаний из {findings.Count} — остальные попадут в отчёт"
            : string.Empty;
    }

    /// <summary>Подпись под списком папок, если показаны не все.</summary>
    [ObservableProperty]
    private string _foldersNote = string.Empty;

    /// <summary>Заполняет вкладку по итогам проверки.</summary>
    public void Update(ScanSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        ScanCounters counters = summary.Counters;

        TotalFiles = CoreFormat.Number(counters.Total);
        TotalFilesNote = $"в {CoreFormat.Number(summary.Folders.Count)} " +
                         CoreFormat.Plural(summary.Folders.Count, "папке", "папках", "папках");

        CorruptedCount = CoreFormat.Number(counters.Corrupted);
        CorruptedNote = counters.Total > 0
            ? $"{CoreFormat.Percent((double)counters.Corrupted / counters.Total)} коллекции"
            : "коллекция пуста";

        WarningCount = CoreFormat.Number(counters.Warnings);

        Elapsed = CoreFormat.Duration(summary.Duration);
        ElapsedNote = $"{summary.Parallelism} " +
                      CoreFormat.Plural(summary.Parallelism, "поток", "потока", "потоков");

        // Доли передаются в разметку как звёздочные веса колонок полосы.
        OkShare = counters.Ok;
        CorruptedShare = counters.Corrupted;
        WarningShare = counters.Warnings;
        SkippedShare = counters.Skipped;

        OkLabel = CoreFormat.Number(counters.Ok);
        CorruptedLabel = CoreFormat.Number(counters.Corrupted);
        WarningLabel = CoreFormat.Number(counters.Warnings);
        SkippedLabel = CoreFormat.Number(counters.Skipped);

        // Список папок намеренно ограничен: на коллекции в сотни тысяч файлов
        // папок бывают тысячи, и вся таблица в отчёте не нужна. Но обрезать
        // молча нельзя — иначе цифра «в 1 148 папках» вверху не сходится
        // с числом строк внизу.
        const int folderLimit = 200;

        FoldersNote = summary.Folders.Count > folderLimit
            ? $"Показаны {folderLimit} папок с наибольшим числом файлов из {CoreFormat.Number(summary.Folders.Count)}"
            : string.Empty;

        Folders.Clear();
        foreach (FolderStat folder in summary.Folders.Take(folderLimit))
        {
            Folders.Add(new FolderRow(
                Relative(folder.Path, summary.RootPath),
                folder.Path,
                CoreFormat.Files(folder.FileCount),
                folder.BadCount == 0 ? "—" : CoreFormat.Number(folder.BadCount),
                folder.BadCount > 0));
        }

        HasSummary = true;
    }

    /// <summary>Сбрасывает вкладку перед новой проверкой.</summary>
    public void Clear()
    {
        Folders.Clear();
        FoldersNote = string.Empty;
        HasSummary = false;
        OkShare = CorruptedShare = WarningShare = SkippedShare = 0;
        TotalFiles = CorruptedCount = WarningCount = "0";
        Elapsed = "0:00";
        ElapsedNote = "проверка не запускалась";
    }

    /// <summary>
    /// Показывает путь относительно проверенной папки: общий префикс у всех строк
    /// одинаков и только съедает место, а различает папки как раз хвост.
    /// Полный путь остаётся во всплывающей подсказке.
    /// </summary>
    private static string Relative(string path, string root)
    {
        if (string.IsNullOrEmpty(root) || !path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        string tail = path[root.Length..].TrimStart(System.IO.Path.DirectorySeparatorChar);
        return tail.Length == 0 ? "· корень выбранной папки" : @"\" + tail;
    }

    /// <summary>Строка списка проверенных папок.</summary>
    /// <param name="Path">Путь относительно проверенной папки — то, что видно в списке.</param>
    /// <param name="FullPath">Полный путь для подсказки.</param>
    /// <param name="Files">Сколько файлов, словами.</param>
    /// <param name="Bad">Сколько повреждено.</param>
    /// <param name="HasBad">Есть ли повреждённые — для подсветки.</param>
    public sealed record FolderRow(string Path, string FullPath, string Files, string Bad, bool HasBad);
}
