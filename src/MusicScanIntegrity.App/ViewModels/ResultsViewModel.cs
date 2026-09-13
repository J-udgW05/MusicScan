using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicScanIntegrity.Core.Models;
using CoreFormat = MusicScanIntegrity.Core.Common.Format;

namespace MusicScanIntegrity.App.ViewModels;

/// <summary>Вкладка «Результаты»: таблица, поиск, фильтры, подробности.</summary>
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

    /// <summary>Значение фильтра «все форматы».</summary>
    public const string AllFormats = "Формат: все";

    /// <summary>Создаёт модель вкладки результатов.</summary>
    public ResultsViewModel()
    {
        View = CollectionViewSource.GetDefaultView(_all);
        View.Filter = PassesFilter;
        Formats = [AllFormats];
        ApplySort();
    }

    /// <summary>Колонка, по которой отсортирована таблица.</summary>
    [ObservableProperty]
    private ResultsSortColumn _sortColumn = ResultsSortColumn.Name;

    /// <summary>Сортировка по возрастанию.</summary>
    [ObservableProperty]
    private bool _sortAscending = true;

    /// <summary>
    /// Переключает сортировку: щелчок по той же колонке разворачивает порядок,
    /// по другой — сортирует по ней по возрастанию.
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
        // Сортировка идёт через CustomSort: размер и статус сравниваются по
        // числу и по перечислению, а не по подписи, иначе «1,8 ГБ» окажется
        // меньше «24,1 МБ», а «Повреждён» — раньше «В порядке» по алфавиту.
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

    /// <summary>Таблица отсортирована по имени.</summary>
    public bool IsSortedByName => SortColumn == ResultsSortColumn.Name;

    /// <summary>Таблица отсортирована по пути.</summary>
    public bool IsSortedByPath => SortColumn == ResultsSortColumn.Path;

    /// <summary>Таблица отсортирована по формату.</summary>
    public bool IsSortedByFormat => SortColumn == ResultsSortColumn.Format;

    /// <summary>Таблица отсортирована по размеру.</summary>
    public bool IsSortedBySize => SortColumn == ResultsSortColumn.Size;

    /// <summary>Таблица отсортирована по статусу.</summary>
    public bool IsSortedByStatus => SortColumn == ResultsSortColumn.Status;

    /// <summary>Отфильтрованное представление таблицы.</summary>
    public ICollectionView View { get; }

    /// <summary>Все результаты без фильтрации.</summary>
    public IReadOnlyList<FileResultViewModel> All => _all;

    /// <summary>Список форматов для выпадающего фильтра.</summary>
    public ObservableCollection<string> Formats { get; }

    /// <summary>Результаты проверки плейлистов.</summary>
    public ObservableCollection<PlaylistCheckResult> Playlists { get; } = [];

    /// <summary>Подпись счётчика «Все» на пилюле-фильтре.</summary>
    public string TotalLabel => CoreFormat.Number(TotalCount);

    /// <summary>Добавляет очередную порцию результатов.</summary>
    /// <remarks>
    /// Счётчики наращиваются по мере добавления: пересчитывать их обходом всего
    /// списка на коллекции в сотни тысяч файлов слишком дорого.
    /// <para>
    /// Оборачивать добавление в <c>DeferRefresh</c> нельзя: представление
    /// запрещает менять коллекцию, пока обновление отложено, и бросает
    /// исключение. Пакетность здесь обеспечивает сам движок — он отдаёт
    /// результаты порциями, а не по одному.
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

    /// <summary>Заменяет список плейлистов.</summary>
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

    /// <summary>Очищает результаты перед новой проверкой.</summary>
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
    /// Показывает единственный файл: снимает все фильтры и оставляет поиск
    /// по его имени. Используется переходом «Показать» с вкладки «Проверка».
    /// </summary>
    public void ShowSingle(string fileName)
    {
        StatusFilter = null;
        FormatFilter = AllFormats;
        SearchText = fileName;
        Selected = View.Cast<FileResultViewModel>().FirstOrDefault();
    }

    /// <summary>Переключает фильтр по статусу (повторное нажатие снимает фильтр).</summary>
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

    /// <summary>В поиске что-то введено — можно показать кнопку очистки.</summary>
    public bool HasSearchText => SearchText.Length > 0;

    /// <summary>Очищает строку поиска.</summary>
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

    /// <summary>Пилюля «Все» активна.</summary>
    public bool IsAllSelected => StatusFilter is null;

    /// <summary>Пилюля «В порядке» активна.</summary>
    public bool IsOkSelected => StatusFilter == CheckStatus.Ok;

    /// <summary>Пилюля «Повреждено» активна.</summary>
    public bool IsCorruptedSelected => StatusFilter == CheckStatus.Corrupted;

    /// <summary>Пилюля «Предупреждения» активна.</summary>
    public bool IsWarningSelected => StatusFilter == CheckStatus.Warning;

    /// <summary>Пилюля «Пропущено» активна.</summary>
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

/// <summary>Колонка сортировки таблицы результатов.</summary>
public enum ResultsSortColumn
{
    /// <summary>Имя файла.</summary>
    Name,

    /// <summary>Папка.</summary>
    Path,

    /// <summary>Формат.</summary>
    Format,

    /// <summary>Размер.</summary>
    Size,

    /// <summary>Статус.</summary>
    Status,
}

/// <summary>Сравнение строк таблицы по выбранной колонке.</summary>
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

        // При равенстве добавляем имя вторым ключом: иначе строки с одинаковым
        // форматом или статусом перескакивают с места на место при пересортировке.
        if (result == 0 && column != ResultsSortColumn.Name)
        {
            result = string.Compare(left.FileName, right.FileName, StringComparison.CurrentCultureIgnoreCase);
        }

        return ascending ? result : -result;
    }
}
