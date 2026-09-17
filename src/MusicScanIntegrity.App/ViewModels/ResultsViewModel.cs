using System.Collections.ObjectModel;
using System.Collections;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoreFormat = MusicScanIntegrity.Core.Common.Format;
using MusicScanIntegrity.Core.Models;

namespace MusicScanIntegrity.App.ViewModels;

/// <summary>Results tab: table, search, filters and details.</summary>
public sealed partial class ResultsViewModel : ObservableObject
{
    private readonly ObservableCollection<FileResultViewModel> _all = [];
    private readonly Dictionary<string, int> _formatCounts = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private CheckStatus? _statusFilter;

    [ObservableProperty]
    private string _formatFilter = AllFormats;

    [ObservableProperty]
    private FileResultViewModel? _selected;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _okCount;

    [ObservableProperty]
    private int _corruptedCount;

    [ObservableProperty]
    private int _warningCount;

    [ObservableProperty]
    private int _skippedCount;

    [ObservableProperty]
    private int _playlistCount;

    [ObservableProperty]
    private int _playlistMissingCount;

    /// <summary>Filter value meaning "all formats"; displayed through the string catalogue.</summary>
    public const string AllFormats = "*";

    public ResultsViewModel()
    {
        View = CollectionViewSource.GetDefaultView(_all);
        View.Filter = PassesFilter;
        Formats = [AllFormats];
        ApplySort();
    }

    /// <summary>Column the table is sorted by.</summary>
    [ObservableProperty]
    private ResultsSortColumn _sortColumn = ResultsSortColumn.Name;

    /// <summary>Ascending order.</summary>
    [ObservableProperty]
    private bool _sortAscending = true;

    /// <summary>
    /// Toggles sorting: clicking the same column reverses the order, another column
    /// sorts by it ascending.
    /// </summary>
    [RelayCommand]
    private void SortBy(string? column)
    {
        ResultsSortColumn requested = column switch
        {
            "Name" => ResultsSortColumn.Name,
            "Path" => ResultsSortColumn.Path,
            "Format" => ResultsSortColumn.Format,
            "Size" => ResultsSortColumn.Size,
            "Status" => ResultsSortColumn.Status,
            _ => ResultsSortColumn.Name,
        };

        if (SortColumn == requested)
        {
            SortAscending = !SortAscending;
        }
        else
        {
            SortColumn = requested;
            SortAscending = true;
        }
    }

    partial void OnSortColumnChanged(ResultsSortColumn value) => ApplySort();

    partial void OnSortAscendingChanged(bool value) => ApplySort();

    private void ApplySort()
    {
        // CustomSort compares size and status by value rather than by caption;
        // otherwise "1,8 ГБ" would sort below "24,1 МБ" and statuses alphabetically.
        if (View is ListCollectionView list)
        {
            list.CustomSort = new ResultsComparer(SortColumn, SortAscending);
        }

        OnPropertyChanged(nameof(IsSortedByName));
        OnPropertyChanged(nameof(IsSortedByPath));
        OnPropertyChanged(nameof(IsSortedByFormat));
        OnPropertyChanged(nameof(IsSortedBySize));
        OnPropertyChanged(nameof(IsSortedByStatus));
    }

    /// <summary>Sorted by name.</summary>
    public bool IsSortedByName => SortColumn == ResultsSortColumn.Name;

    /// <summary>Sorted by path.</summary>
    public bool IsSortedByPath => SortColumn == ResultsSortColumn.Path;

    /// <summary>Sorted by format.</summary>
    public bool IsSortedByFormat => SortColumn == ResultsSortColumn.Format;

    /// <summary>Sorted by size.</summary>
    public bool IsSortedBySize => SortColumn == ResultsSortColumn.Size;

    /// <summary>Sorted by status.</summary>
    public bool IsSortedByStatus => SortColumn == ResultsSortColumn.Status;

    /// <summary>Filtered view of the table.</summary>
    public ICollectionView View { get; }

    /// <summary>All results, unfiltered.</summary>
    public IReadOnlyList<FileResultViewModel> All => _all;

    /// <summary>Formats offered by the filter drop-down.</summary>
    public ObservableCollection<string> Formats { get; }

    /// <summary>Playlist check results.</summary>
    public ObservableCollection<PlaylistCheckResult> Playlists { get; } = [];

    /// <summary>Counter caption on the "All" chip.</summary>
    public string TotalLabel => CoreFormat.Number(TotalCount);

    /// <summary>Adds the next batch of results.</summary>
    /// <remarks>
    /// Counters are incremented as rows arrive; recounting the whole list for
    /// hundreds of thousands of files would be too slow.
    /// <para>
    /// <c>DeferRefresh</c> cannot be used here: the view forbids modifying the
    /// collection while a refresh is deferred and throws. Batching comes from the
    /// engine, which delivers results in chunks.
    /// </para>
    /// </remarks>
    public void AddRange(IReadOnlyList<FileCheckResult> batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (batch.Count == 0)
        {
            return;
        }

        foreach (FileCheckResult result in batch)
        {
            FileResultViewModel row = new(result);
            _all.Add(row);

            _formatCounts.TryGetValue(row.Extension, out int count);
            _formatCounts[row.Extension] = count + 1;

            switch (result.Status)
            {
                case CheckStatus.Ok: OkCount++; break;
                case CheckStatus.Warning: WarningCount++; break;
                case CheckStatus.Corrupted: CorruptedCount++; break;
                case CheckStatus.Skipped: SkippedCount++; break;
            }
        }

        TotalCount = _all.Count;
        OnPropertyChanged(nameof(TotalLabel));
        RefreshFormats();
    }

    /// <summary>Replaces the playlist list.</summary>
    public void SetPlaylists(IReadOnlyList<PlaylistCheckResult> playlists)
    {
        Playlists.Clear();
        foreach (PlaylistCheckResult playlist in playlists)
        {
            Playlists.Add(playlist);
        }

        PlaylistCount = playlists.Count;
        PlaylistMissingCount = playlists.Sum(p => p.MissingCount);
    }

    /// <summary>Clears results before a new scan.</summary>
    public void Clear()
    {
        _all.Clear();
        _formatCounts.Clear();
        Playlists.Clear();
        Selected = null;
        Formats.Clear();
        Formats.Add(AllFormats);
        FormatFilter = AllFormats;
        StatusFilter = null;
        SearchText = string.Empty;
        TotalCount = 0;
        OkCount = 0;
        CorruptedCount = 0;
        WarningCount = 0;
        SkippedCount = 0;
        OnPropertyChanged(nameof(TotalLabel));
        PlaylistCount = 0;
        PlaylistMissingCount = 0;
    }

    /// <summary>
    /// Shows a single file: clears every filter and searches by its name. Used by
    /// the "Show" link on the scan tab.
    /// </summary>
    public void ShowSingle(string fileName)
    {
        StatusFilter = null;
        FormatFilter = AllFormats;
        SearchText = fileName;
        Selected = View.Cast<FileResultViewModel>().FirstOrDefault();
    }

    /// <summary>Toggles a status filter; pressing it again clears the filter.</summary>
    [RelayCommand]
    private void FilterByStatus(string? status)
    {
        CheckStatus? requested = status switch
        {
            "Ok" => CheckStatus.Ok,
            "Warning" => CheckStatus.Warning,
            "Corrupted" => CheckStatus.Corrupted,
            "Skipped" => CheckStatus.Skipped,
            _ => null,
        };

        StatusFilter = StatusFilter == requested ? null : requested;
    }

    /// <summary>The search box has text, so the clear button is shown.</summary>
    public bool HasSearchText => SearchText.Length > 0;

    /// <summary>Clears the search box.</summary>
    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    partial void OnSearchTextChanged(string value)
    {
        View.Refresh();
        OnPropertyChanged(nameof(HasSearchText));
    }

    partial void OnStatusFilterChanged(CheckStatus? value)
    {
        View.Refresh();
        OnPropertyChanged(nameof(IsAllSelected));
        OnPropertyChanged(nameof(IsOkSelected));
        OnPropertyChanged(nameof(IsCorruptedSelected));
        OnPropertyChanged(nameof(IsWarningSelected));
        OnPropertyChanged(nameof(IsSkippedSelected));
    }

    partial void OnFormatFilterChanged(string value) => View.Refresh();

    /// <summary>The "All" chip is active.</summary>
    public bool IsAllSelected => StatusFilter is null;

    /// <summary>The "Ok" chip is active.</summary>
    public bool IsOkSelected => StatusFilter == CheckStatus.Ok;

    /// <summary>The "Corrupted" chip is active.</summary>
    public bool IsCorruptedSelected => StatusFilter == CheckStatus.Corrupted;

    /// <summary>The "Warnings" chip is active.</summary>
    public bool IsWarningSelected => StatusFilter == CheckStatus.Warning;

    /// <summary>The "Skipped" chip is active.</summary>
    public bool IsSkippedSelected => StatusFilter == CheckStatus.Skipped;

    private bool PassesFilter(object item)
    {
        if (item is not FileResultViewModel row)
        {
            return false;
        }

        if (StatusFilter is { } status && row.Status != status)
        {
            return false;
        }

        if (!FormatFilter.Equals(AllFormats, StringComparison.Ordinal) &&
            !row.Extension.Equals(FormatFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (SearchText.Length > 0 &&
            row.FileName.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        return true;
    }

    private void RefreshFormats()
    {
        foreach (string format in _formatCounts.Keys.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            if (!Formats.Contains(format))
            {
                Formats.Add(format);
            }
        }
    }
}

/// <summary>Sort column of the results table.</summary>
public enum ResultsSortColumn
{
    /// <summary>File name.</summary>
    Name,

    /// <summary>Folder.</summary>
    Path,

    /// <summary>Format.</summary>
    Format,

    /// <summary>Size.</summary>
    Size,

    /// <summary>Status.</summary>
    Status,
}

/// <summary>Compares table rows by the selected column.</summary>
internal sealed class ResultsComparer(ResultsSortColumn column, bool ascending) : IComparer
{
    /// <inheritdoc />
    public int Compare(object? x, object? y)
    {
        if (x is not FileResultViewModel left || y is not FileResultViewModel right)
        {
            return 0;
        }

        int result = column switch
        {
            ResultsSortColumn.Path => string.Compare(left.DirectoryPath, right.DirectoryPath, StringComparison.CurrentCultureIgnoreCase),
            ResultsSortColumn.Format => string.Compare(left.Format, right.Format, StringComparison.CurrentCultureIgnoreCase),
            ResultsSortColumn.Size => left.SizeBytes.CompareTo(right.SizeBytes),
            ResultsSortColumn.Status => ((int)left.Status).CompareTo((int)right.Status),
            _ => string.Compare(left.FileName, right.FileName, StringComparison.CurrentCultureIgnoreCase),
        };

        // Break ties by name, or rows with equal format or status jump around on
        // every re-sort.
        if (result == 0 && column != ResultsSortColumn.Name)
        {
            result = string.Compare(left.FileName, right.FileName, StringComparison.CurrentCultureIgnoreCase);
        }

        return ascending ? result : -result;
    }
}
