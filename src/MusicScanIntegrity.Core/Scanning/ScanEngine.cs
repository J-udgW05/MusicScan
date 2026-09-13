using System.Collections.Concurrent;
using System.Diagnostics;
using MusicScanIntegrity.Core.Discovery;
using MusicScanIntegrity.Core.Locking;
using MusicScanIntegrity.Core.Models;
using MusicScanIntegrity.Core.Playlists;
using MusicScanIntegrity.Core.Settings;

namespace MusicScanIntegrity.Core.Scanning;

/// <summary>Движок проверки: параллельность, пауза, остановка, порционная отдача результатов.</summary>
public interface IScanEngine
{
    /// <summary>Текущее состояние.</summary>
    ScanState State { get; }

    /// <summary>Готовые результаты приходят порциями, а не по одному.</summary>
    event EventHandler<IReadOnlyList<FileCheckResult>>? ResultsReady;

    /// <summary>Плейлисты проверены.</summary>
    event EventHandler<IReadOnlyList<PlaylistCheckResult>>? PlaylistsReady;

    /// <summary>Обновление счётчиков и прогресса.</summary>
    event EventHandler<ScanProgress>? ProgressChanged;

    /// <summary>Предупреждение по ходу проверки.</summary>
    event EventHandler<LiveWarning>? WarningRaised;

    /// <summary>Проверка завершилась (успешно, остановлена или прервана сбоем).</summary>
    event EventHandler<ScanSummary>? Finished;

    /// <summary>Запускает проверку по результатам быстрого обхода.</summary>
    Task RunAsync(DiscoveryResult discovery, AppSettings settings, CancellationToken cancellationToken = default);

    /// <summary>Ставит проверку на паузу.</summary>
    void Pause();

    /// <summary>Продолжает проверку после паузы.</summary>
    void Resume();

    /// <summary>Полностью прерывает проверку; уже полученные результаты сохраняются.</summary>
    void Stop();
}

/// <inheritdoc cref="IScanEngine" />
public sealed class ScanEngine : IScanEngine, IDisposable
{
    /// <summary>
    /// Как часто отдавать накопленные результаты в интерфейс.
    /// Отдавать каждый результат по отдельности на коллекции в сотни тысяч файлов —
    /// верный способ подвесить интерфейс (03_IMPLEMENTATION_GUIDE.md, раздел 2).
    /// </summary>
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>Максимальный размер порции результатов.</summary>
    private const int MaxBatchSize = 250;

    private readonly IFileChecker _fileChecker;
    private readonly IPlaylistService _playlistService;
    private readonly ILockedFileDecisionProvider _decisionProvider;

    private readonly ConcurrentQueue<FileCheckResult> _pending = new();

    /// <summary>
    /// Статус каждого проверенного файла. Заполняется сразу при получении
    /// результата: сводка и проверка плейлистов должны видеть все файлы,
    /// а не только последнюю порцию.
    /// </summary>
    private readonly ConcurrentDictionary<string, CheckStatus> _statuses = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Длительности проверенных файлов: по ним сверяются метки дорожек в cue-листах.
    /// </summary>
    private readonly ConcurrentDictionary<string, double> _durations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _countersLock = new();

    private PauseTokenSource? _pause;
    private CancellationTokenSource? _stop;
    private LockedFileQuestionQueue? _questions;
    private ScanCounters _counters;
    private volatile string? _currentFile;
    private volatile bool _stopRequested;

    /// <summary>Создаёт движок проверки.</summary>
    public ScanEngine(
        IFileChecker fileChecker,
        IPlaylistService playlistService,
        ILockedFileDecisionProvider decisionProvider)
    {
        _fileChecker = fileChecker;
        _playlistService = playlistService;
        _decisionProvider = decisionProvider;
    }

    /// <inheritdoc />
    public ScanState State { get; private set; } = ScanState.Idle;

    /// <inheritdoc />
    public event EventHandler<IReadOnlyList<FileCheckResult>>? ResultsReady;

    /// <inheritdoc />
    public event EventHandler<IReadOnlyList<PlaylistCheckResult>>? PlaylistsReady;

    /// <inheritdoc />
    public event EventHandler<ScanProgress>? ProgressChanged;

    /// <inheritdoc />
    public event EventHandler<LiveWarning>? WarningRaised;

    /// <inheritdoc />
    public event EventHandler<ScanSummary>? Finished;

    /// <inheritdoc />
    public async Task RunAsync(
        DiscoveryResult discovery,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(settings);

        if (State is ScanState.Running or ScanState.Paused or ScanState.Discovering)
        {
            throw new InvalidOperationException("Проверка уже идёт.");
        }

        _pause?.Dispose();
        _stop?.Dispose();
        _questions?.Dispose();

        _pause = new PauseTokenSource();
        _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _questions = new LockedFileQuestionQueue(_decisionProvider);
        _pending.Clear();
        _statuses.Clear();
        _durations.Clear();
        _stopRequested = false;
        _currentFile = null;

        lock (_countersLock)
        {
            _counters = new ScanCounters(discovery.AudioItems.Count, 0, 0, 0, 0, 0);
        }

        State = ScanState.Running;
        DateTimeOffset startedAt = DateTimeOffset.Now;
        Stopwatch stopwatch = Stopwatch.StartNew();
        // Число потоков зависит не только от настроек: на диске с подвижной
        // головкой параллельные чтения мешают друг другу.
        int parallelism = ParallelismPlanner.Resolve(
            settings,
            settings.RespectDriveType ? StorageTypeDetector.Detect(discovery.RootPath) : StorageType.Unknown);

        string? criticalFailure = null;
        List<PlaylistCheckResult> playlistResults = [];

        using CancellationTokenSource flushLoopCts = new();
        Task flushLoop = RunFlushLoopAsync(stopwatch, parallelism, flushLoopCts.Token);

        try
        {
            FileCheckContext context = new(settings, _questions, OnLargeFile);

            await Parallel.ForEachAsync(
                discovery.AudioItems,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = parallelism,
                    CancellationToken = _stop.Token,
                },
                async (item, token) =>
                {
                    // Пауза действует только на новые файлы; уже начатые доработают.
                    await _pause.WaitWhilePausedAsync(token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();

                    _currentFile = item.FullPath;
                    FileCheckResult result = await _fileChecker.CheckAsync(item, context, token).ConfigureAwait(false);

                    _pending.Enqueue(result);
                    _statuses[result.FullPath] = result.Status;

                    if (result.DurationSeconds > 0)
                    {
                        _durations[result.FullPath] = result.DurationSeconds;
                    }

                    lock (_countersLock)
                    {
                        _counters = _counters.Add(result.Status);
                    }

                    RaiseWarningsFor(result);
                }).ConfigureAwait(false);

            // Плейлисты проверяются после файлов: так известны статусы из основного
            // сканирования и один файл не получит два разных результата.
            if (settings.CheckPlaylists && discovery.Playlists.Count > 0)
            {
                Flush();
                playlistResults = await CheckPlaylistsAsync(discovery, _statuses, _stop.Token).ConfigureAwait(false);
                PlaylistsReady?.Invoke(this, playlistResults);
            }

            State = _stopRequested ? ScanState.Stopped : ScanState.Completed;
        }
        catch (OperationCanceledException)
        {
            State = ScanState.Stopped;
        }
        catch (AudioEngineFailureException ex)
        {
            // Критический сбой: проверка останавливается, программа продолжает работать,
            // всё накопленное сохраняется (02_ARCHITECTURE.md, раздел 4).
            State = ScanState.Failed;
            criticalFailure = ex.Message;
        }
        catch (Exception ex)
        {
            State = ScanState.Failed;
            criticalFailure = $"Проверка прервана непредвиденной ошибкой: {ex.Message}";
        }
        finally
        {
            await flushLoopCts.CancelAsync().ConfigureAwait(false);

            try
            {
                await flushLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Ожидаемо: цикл отдачи результатов остановлен вместе с проверкой.
            }

            // Отдаём всё, что осталось в очереди, и последний раз обновляем прогресс.
            Flush();

            stopwatch.Stop();
            ProgressChanged?.Invoke(this, BuildProgress(stopwatch.Elapsed, parallelism));
        }

        ScanSummary summary = BuildSummary(
            discovery, startedAt, stopwatch.Elapsed, parallelism, _statuses, playlistResults, criticalFailure, settings);

        Finished?.Invoke(this, summary);
    }

    /// <inheritdoc />
    public void Pause()
    {
        if (State != ScanState.Running)
        {
            return;
        }

        _pause?.Pause();
        State = ScanState.Paused;
    }

    /// <inheritdoc />
    public void Resume()
    {
        if (State != ScanState.Paused)
        {
            return;
        }

        _pause?.Resume();
        State = ScanState.Running;
    }

    /// <inheritdoc />
    public void Stop()
    {
        if (State is not (ScanState.Running or ScanState.Paused or ScanState.Discovering))
        {
            return;
        }

        _stopRequested = true;

        // Снимаем паузу перед остановкой: иначе потоки, ждущие снятия паузы,
        // не увидят запрос на отмену. Кнопка «Стоп» должна работать всегда,
        // в том числе когда проверка стоит на паузе.
        _pause?.Resume();
        _stop?.Cancel();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _pause?.Dispose();
        _stop?.Dispose();
        _questions?.Dispose();
    }

    private async Task<List<PlaylistCheckResult>> CheckPlaylistsAsync(
        DiscoveryResult discovery,
        IReadOnlyDictionary<string, CheckStatus> statuses,
        CancellationToken cancellationToken)
    {
        List<PlaylistCheckResult> results = [];

        foreach (ScanItem playlist in discovery.Playlists)
        {
            cancellationToken.ThrowIfCancellationRequested();

            PlaylistCheckResult result = await _playlistService
                .CheckAsync(playlist.FullPath, statuses, _durations, cancellationToken)
                .ConfigureAwait(false);

            results.Add(result);

            if (result.MissingCount > 0)
            {
                WarningRaised?.Invoke(this, new LiveWarning(
                    $"В плейлисте не найдено файлов: {result.MissingCount}",
                    result.FullPath,
                    IssueCode.PlaylistTargetMissing));
            }

            if (result.ContentIssue is { } contentIssue)
            {
                WarningRaised?.Invoke(this, new LiveWarning(
                    contentIssue.Message,
                    result.FullPath,
                    contentIssue.Code));
            }
        }

        return results;
    }

    /// <summary>Периодически отдаёт накопленные результаты и обновляет прогресс.</summary>
    private async Task RunFlushLoopAsync(Stopwatch stopwatch, int parallelism, CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(FlushInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                Flush();
                ProgressChanged?.Invoke(this, BuildProgress(stopwatch.Elapsed, parallelism));
            }
        }
        catch (OperationCanceledException)
        {
            // Штатное завершение цикла вместе с проверкой.
        }
    }

    /// <summary>
    /// Отдаёт всё накопленное порциями не больше <see cref="MaxBatchSize"/>.
    /// Опустошать очередь нужно целиком: одной порции не хватит, если за интервал
    /// успело накопиться больше результатов, чем помещается в порцию.
    /// </summary>
    private void Flush()
    {
        while (true)
        {
            List<FileCheckResult> batch = [];
            while (batch.Count < MaxBatchSize && _pending.TryDequeue(out FileCheckResult? result))
            {
                batch.Add(result);
            }

            if (batch.Count == 0)
            {
                return;
            }

            ResultsReady?.Invoke(this, batch);
        }
    }

    private ScanProgress BuildProgress(TimeSpan elapsed, int parallelism)
    {
        ScanCounters counters;
        lock (_countersLock)
        {
            counters = _counters;
        }

        return new ScanProgress(
            State,
            counters,
            _currentFile,
            elapsed,
            parallelism,
            _questions?.Waiting ?? 0);
    }

    private void OnLargeFile(ScanItem item, long size) =>
        WarningRaised?.Invoke(this, new LiveWarning(
            $"Очень большой файл ({Common.Format.Size(size)}) — проверка займёт больше времени",
            item.FullPath,
            IssueCode.LargeFile));

    private void RaiseWarningsFor(FileCheckResult result)
    {
        foreach (CheckIssue issue in result.Issues)
        {
            // О большом файле уже сообщили до начала его проверки.
            if (issue.Code is IssueCode.LargeFile or IssueCode.None)
            {
                continue;
            }

            if (issue.Severity is CheckStatus.Ok)
            {
                continue;
            }

            WarningRaised?.Invoke(this, new LiveWarning(issue.Message, result.FullPath, issue.Code));
        }
    }

    private ScanSummary BuildSummary(
        DiscoveryResult discovery,
        DateTimeOffset startedAt,
        TimeSpan duration,
        int parallelism,
        IReadOnlyDictionary<string, CheckStatus> statuses,
        IReadOnlyList<PlaylistCheckResult> playlists,
        string? criticalFailure,
        AppSettings settings)
    {
        Dictionary<string, (int Files, int Bad)> byFolder = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string path, CheckStatus status) in statuses)
        {
            string folder = Path.GetDirectoryName(path) ?? discovery.RootPath;
            byFolder.TryGetValue(folder, out (int Files, int Bad) stat);
            byFolder[folder] = (stat.Files + 1, stat.Bad + (status == CheckStatus.Corrupted ? 1 : 0));
        }

        List<FolderStat> folders = [.. byFolder
            .Select(kv => new FolderStat(kv.Key, kv.Value.Files, kv.Value.Bad))
            .OrderByDescending(f => f.BadCount)
            .ThenByDescending(f => f.FileCount)];

        ScanCounters counters;
        lock (_countersLock)
        {
            counters = _counters;
        }

        return new ScanSummary
        {
            RootPath = discovery.RootPath,
            Counters = counters,
            StartedAt = startedAt,
            Duration = duration,
            Parallelism = parallelism,
            Depth = settings.CheckDepth,
            ContainerIntegrityChecked = settings.VerifyContainerIntegrity,
            WasStopped = State == ScanState.Stopped,
            CriticalFailure = criticalFailure,
            Folders = folders,
            PlaylistCount = playlists.Count,
            PlaylistMissingLinks = playlists.Sum(p => p.MissingCount),
            InaccessibleFolders = discovery.InaccessibleFolders,
        };
    }
}
