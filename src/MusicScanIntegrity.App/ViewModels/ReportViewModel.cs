using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CoreFormat = MusicScanIntegrity.Core.Common.Format;
using MusicScanIntegrity.Core.Models;
using MusicScanIntegrity.Core.Resources;
using MusicScanIntegrity.Core.Settings;

namespace MusicScanIntegrity.App.ViewModels;

/// <summary>Report tab: KPIs, status breakdown, folders and format choice.</summary>
public sealed partial class ReportViewModel : ObservableObject
{
    [ObservableProperty]
    private string _totalFiles = "0";

    [ObservableProperty]
    private string _totalFilesNote = CoreFormat.Count(0, "Plural_Html_InFolders");

    [ObservableProperty]
    private string _corruptedCount = "0";

    [ObservableProperty]
    private string _corruptedNote = CoreFormat.Text(Strings.Html_OfCollection, CoreFormat.Percent(0));

    [ObservableProperty]
    private string _warningCount = "0";

    [ObservableProperty]
    private string _elapsed = "0:00";

    [ObservableProperty]
    private string _elapsedNote = Strings.ReportTab_NotRunYet;

    [ObservableProperty]
    private ReportFormat _selectedFormat = ReportFormat.Html;

    /// <summary>Folder the report is saved to by default.</summary>
    [ObservableProperty]
    private string _targetFolder = string.Empty;

    [ObservableProperty]
    private bool _hasSummary;

    // Shares for the distribution bar; together they make up its width.
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

    /// <summary>Checked folders: path, file count, corrupted count.</summary>
    public ObservableCollection<FolderRow> Folders { get; } = [];

    /// <summary>Collection findings: numbering gaps, mixed formats, duplicates.</summary>
    /// <remarks>
    /// Kept apart from the results list because they concern a folder or a pair of
    /// files rather than a single file.
    /// </remarks>
    public ObservableCollection<CollectionFinding> Findings { get; } = [];

    /// <summary>There are collection findings, so the card is shown.</summary>
    [ObservableProperty]
    private bool _hasFindings;

    /// <summary>Caption under the findings list.</summary>
    [ObservableProperty]
    private string _findingsNote = string.Empty;

    /// <summary>Fills the collection findings list.</summary>
    public void UpdateFindings(IReadOnlyList<CollectionFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);

        Findings.Clear();
        foreach (CollectionFinding finding in findings.Take(FindingsLimit))
        {
            Findings.Add(finding);
        }

        HasFindings = findings.Count > 0;
        _findingsTotal = findings.Count;
        UpdateFindingsNote();
    }

    private const int FindingsLimit = 200;

    private int _findingsTotal;

    private ScanSummary? _summary;

    private void UpdateFindingsNote() =>
        FindingsNote = _findingsTotal > FindingsLimit
            ? CoreFormat.Text(Strings.ReportTab_FindingsCapped, FindingsLimit, _findingsTotal)
            : string.Empty;

    /// <summary>Rebuilds captions after the interface language changes.</summary>
    public void RefreshLanguage()
    {
        UpdateFindingsNote();

        // Kind labels are read from the catalogue on display; re-adding the rows
        // re-templates them. The finding text itself stays as the scan wrote it.
        CollectionFinding[] shown = [.. Findings];
        Findings.Clear();
        foreach (CollectionFinding finding in shown)
        {
            Findings.Add(finding);
        }

        if (_summary is { } summary)
        {
            Update(summary);
        }
        else
        {
            TotalFilesNote = CoreFormat.Count(0, "Plural_Html_InFolders");
            CorruptedNote = CoreFormat.Text(Strings.Html_OfCollection, CoreFormat.Percent(0));
            ElapsedNote = Strings.ReportTab_NotRunYet;
        }
    }

    /// <summary>Caption under the folder list when not every folder is shown.</summary>
    [ObservableProperty]
    private string _foldersNote = string.Empty;

    /// <summary>Fills the tab from a finished scan.</summary>
    public void Update(ScanSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        _summary = summary;
        ScanCounters counters = summary.Counters;

        TotalFiles = CoreFormat.Number(counters.Total);
        TotalFilesNote = CoreFormat.Count(summary.Folders.Count, "Plural_Html_InFolders");

        CorruptedCount = CoreFormat.Number(counters.Corrupted);
        CorruptedNote = counters.Total > 0
            ? CoreFormat.Text(Strings.Html_OfCollection, CoreFormat.Percent((double)counters.Corrupted / counters.Total))
            : Strings.ReportTab_CollectionEmpty;

        WarningCount = CoreFormat.Number(counters.Warnings);

        Elapsed = CoreFormat.Duration(summary.Duration);
        ElapsedNote = CoreFormat.Count(summary.Parallelism, "Plural_ReportTab_Threads");

        // Shares go to the markup as star weights of the bar columns.
        OkShare = counters.Ok;
        CorruptedShare = counters.Corrupted;
        WarningShare = counters.Warnings;
        SkippedShare = counters.Skipped;

        OkLabel = CoreFormat.Number(counters.Ok);
        CorruptedLabel = CoreFormat.Number(counters.Corrupted);
        WarningLabel = CoreFormat.Number(counters.Warnings);
        SkippedLabel = CoreFormat.Number(counters.Skipped);

        // The folder list is capped on purpose: a collection of hundreds of
        // thousands of files spans thousands of folders. The cut must be announced,
        // though, or "in 1,148 folders" at the top would contradict the row count.
        const int folderLimit = 200;

        FoldersNote = summary.Folders.Count > folderLimit
            ? CoreFormat.Text(Strings.ReportTab_FoldersCapped, folderLimit, CoreFormat.Number(summary.Folders.Count))
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

    /// <summary>Resets the tab before a new scan.</summary>
    public void Clear()
    {
        Folders.Clear();
        FoldersNote = string.Empty;
        HasSummary = false;
        _summary = null;
        OkShare = CorruptedShare = WarningShare = SkippedShare = 0;
        TotalFiles = CorruptedCount = WarningCount = "0";
        Elapsed = "0:00";
        ElapsedNote = Strings.ReportTab_NotRunYet;
    }

    /// <summary>
    /// Shows the path relative to the scanned folder: the shared prefix only takes
    /// space, and it is the tail that tells folders apart. The full path stays in
    /// the tooltip.
    /// </summary>
    private static string Relative(string path, string root)
    {
        if (string.IsNullOrEmpty(root) || !path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        string tail = path[root.Length..].TrimStart(System.IO.Path.DirectorySeparatorChar);
        return tail.Length == 0 ? Strings.ReportTab_RootFolder : @"\" + tail;
    }

    /// <summary>A row in the checked folders list.</summary>
    /// <param name="Path">Path relative to the scanned folder, as displayed.</param>
    /// <param name="FullPath">Full path for the tooltip.</param>
    /// <param name="Files">File count in words.</param>
    /// <param name="HasBad">Whether any file is corrupted, for highlighting.</param>
    public sealed record FolderRow(string Path, string FullPath, string Files, string Bad, bool HasBad);
}
