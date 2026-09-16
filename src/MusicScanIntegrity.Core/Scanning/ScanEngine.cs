using System.Collections.Concurrent;
using System.Diagnostics;
using MusicScanIntegrity.Core.Discovery;
using MusicScanIntegrity.Core.Locking;
using MusicScanIntegrity.Core.Models;
using MusicScanIntegrity.Core.Playlists;
using MusicScanIntegrity.Core.Resources;
using MusicScanIntegrity.Core.Settings;

namespace MusicScanIntegrity.Core.Scanning;

/// <summary>Scan engine: parallelism, pause, stop and batched result delivery.</summary>
public interface IScanEngine
{
    /// <summary>Current state.</summary>
    ScanState State { get; }

    /// <summary>Results arrive in batches rather than one at a time.</summary>
    event EventHandler<IReadOnlyList<FileCheckResult>>? ResultsReady;

    /// <summary>Playlists have been checked.</summary>
    event EventHandler<IReadOnlyList<PlaylistCheckResult>>? PlaylistsReady;

    /// <summary>Counters and progress updated.</summary>
    event EventHandler<ScanProgress>? ProgressChanged;

    /// <summary>A warning raised during the scan.</summary>
    event EventHandler<LiveWarning>? WarningRaised;

    /// <summary>The scan ended: finished, stopped or aborted.</summary>
    event EventHandler<ScanSummary>? Finished;

    /// <summary>Starts a scan from the results of the folder walk.</summary>
    Task RunAsync(DiscoveryResult discovery, AppSettings settings, CancellationToken cancellationToken = default);

    /// <summary>Pauses the scan.</summary>
    void Pause();

    /// <summary>Resumes after a pause.</summary>
    void Resume();

    /// <summary>Aborts the scan; results already gathered are kept.</summary>
    void Stop();
}

/// <inheritdoc cref="IScanEngine" />
public sealed class ScanEngine : IScanEngine, IDisposable
{
    /// <summary>
    /// How often gathered results are handed to the UI. Delivering them one by
    /// one would freeze the UI on a collection of hundreds of thousands.
    /// </summary>
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>Largest result batch.</summary>
    private const int MaxBatchSize = 250;

    private readonly IFileChecker _fileChecker;
    private readonly IPlaylistService _playlistService;
    private readonly ILockedFileDecisionProvider _decisionProvider;

    private readonly ConcurrentQueue<FileCheckResult> _pending = new();

    /// <summary>
    /// Status of every checked file, filled as results arrive: the summary and
    /// the playlist check must see all files, not just the last batch.
    /// </summary>
    private readonly ConcurrentDictionary<string, CheckStatus> _statuses = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Durations of checked files, used to validate cue sheet track marks.
    /// </summary>
    private readonly ConcurrentDictionary<string, double> _durations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _countersLock = new();

    private PauseTokenSource? _pause;
    private CancellationTokenSource? _stop;
    private LockedFileQuestionQueue? _questions;
    private ScanCounters _counters;
    private volatile string? _currentFile;
    private volatile bool _stopRequested;

    /// <summary>Creates the scan engine.</summary>
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
            throw new InvalidOperationException(Strings.Engine_AlreadyRunning);
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
        // The thread count is not settings alone: on a spinning disk parallel
        // reads get in each other's way.
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
                    // Pausing affects new files only; those in flight finish.
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

            // Playlists come after the files so the main scan statuses are
            // known and no file ends up with two different results.
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
            // Critical failure: the scan stops, the application keeps running
            // and everything gathered so far is kept.
            State = ScanState.Failed;
            criticalFailure = ex.Message;
        }
        catch (Exception ex)
        {
            State = ScanState.Failed;
            criticalFailure = Common.Format.Text(Strings.Engine_UnexpectedFailure, ex.Message);
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
                // Expected: the delivery loop stops along with the scan.
            }

            // Drain whatever is left and publish progress one last time.
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

        // Lift the pause before stopping, or threads waiting on it never see
        // the cancellation. Stop has to work while paused too.
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
                    Common.Format.Text(Strings.Engine_PlaylistMissing, result.MissingCount),
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

    /// <summary>Periodically publishes gathered results and progress.</summary>
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
            // Normal shutdown of the loop along with the scan.
        }
    }

    /// <summary>
    /// Publishes everything queued in batches of at most
    /// <see cref="MaxBatchSize"/>. The queue must be drained fully: one batch
    /// is not enough when more results arrived during the interval.
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
            Common.Format.Text(Strings.Engine_LargeFile, Common.Format.Size(size)),
            item.FullPath,
            IssueCode.LargeFile));

    private void RaiseWarningsFor(FileCheckResult result)
    {
        foreach (CheckIssue issue in result.Issues)
        {
            // The large file was already reported before checking started.
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
