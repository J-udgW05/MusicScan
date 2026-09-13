namespace MusicScanIntegrity.Core.Models;

/// <summary>Сводка по завершённой (или остановленной) проверке — основа вкладки «Отчёт».</summary>
public sealed class ScanSummary
{
    /// <summary>Корневая папка проверки.</summary>
    public required string RootPath { get; init; }

    /// <summary>Итоговые счётчики.</summary>
    public required ScanCounters Counters { get; init; }

    /// <summary>Когда проверка началась.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>Сколько времени заняла проверка.</summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>Сколько потоков реально использовалось.</summary>
    public required int Parallelism { get; init; }

    /// <summary>
    /// Насколько глубоко читались файлы.
    /// </summary>
    /// <remarks>
    /// Попадает в отчёт: без этого отчёты быстрой и полной проверки выглядят
    /// одинаково, хотя стоят за ними очень разные утверждения.
    /// </remarks>
    public Settings.CheckDepth Depth { get; init; } = Settings.CheckDepth.Sampled;

    /// <summary>Сверялись ли контрольные суммы формата.</summary>
    public bool ContainerIntegrityChecked { get; init; } = true;

    /// <summary>Как проверяли — одной строкой для отчёта.</summary>
    public string DepthLabel
    {
        get
        {
            string depth = Depth switch
            {
                Settings.CheckDepth.Quick => "прочитано начало каждого файла",
                Settings.CheckDepth.Full => "каждый файл прочитан целиком",
                _ => "прочитаны начало, конец и середина каждого файла",
            };

            return ContainerIntegrityChecked
                ? $"{depth}; контрольные суммы формата сверены"
                : $"{depth}; контрольные суммы формата не сверялись";
        }
    }

    /// <summary>Проверка была прервана пользователем.</summary>
    public bool WasStopped { get; init; }

    /// <summary>Проверка остановлена из-за критического сбоя; текст для пользователя.</summary>
    public string? CriticalFailure { get; init; }

    /// <summary>Разбивка по папкам — «Проверенные папки» на вкладке «Отчёт».</summary>
    public IReadOnlyList<FolderStat> Folders { get; init; } = [];

    /// <summary>Сколько плейлистов проверено.</summary>
    public int PlaylistCount { get; init; }

    /// <summary>Сколько путей внутри плейлистов не нашлось.</summary>
    public int PlaylistMissingLinks { get; init; }

    /// <summary>Папки, куда не пустила система при обходе.</summary>
    public IReadOnlyList<InaccessibleFolder> InaccessibleFolders { get; init; } = [];
}

/// <summary>Статистика по одной папке.</summary>
/// <param name="Path">Путь к папке.</param>
/// <param name="FileCount">Сколько файлов проверено в ней.</param>
/// <param name="BadCount">Сколько из них повреждено.</param>
public sealed record FolderStat(string Path, int FileCount, int BadCount);
