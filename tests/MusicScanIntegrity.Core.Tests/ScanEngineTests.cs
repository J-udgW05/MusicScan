using MusicScanIntegrity.Core.Audio;
using MusicScanIntegrity.Core.Locking;
using MusicScanIntegrity.Core.Models;
using MusicScanIntegrity.Core.Playlists;
using MusicScanIntegrity.Core.Scanning;
using MusicScanIntegrity.Core.Settings;
using Xunit;

namespace MusicScanIntegrity.Core.Tests;

public sealed class ScanEngineTests
{
    /// <summary>File check whose behaviour the test fully controls.</summary>
    private sealed class ScriptedChecker(Func<ScanItem, CancellationToken, Task<FileCheckResult>> body) : IFileChecker
    {
        public int Started;
        public int Finished;

        public async Task<FileCheckResult> CheckAsync(ScanItem item, FileCheckContext context, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Started);
            FileCheckResult result = await body(item, cancellationToken);
            Interlocked.Increment(ref Finished);
            return result;
        }
    }

    private static FileCheckResult Ok(ScanItem item) =>
        FileCheckResult.From(item, [], TimeSpan.FromMilliseconds(1), "FLAC");

    private static FileCheckResult WithIssue(ScanItem item, CheckIssue issue) =>
        FileCheckResult.From(item, [issue], TimeSpan.FromMilliseconds(1), "FLAC");

    private static DiscoveryResult Discovery(int fileCount, string root = @"D:\Music") => new()
    {
        RootPath = root,
        AudioItems = [.. Enumerable.Range(0, fileCount).Select(i => new ScanItem($@"{root}\track{i:0000}.flac", 1024, ScanItemKind.Audio))],
        Playlists = [],
        FolderCount = 1,
        InaccessibleFolders = [],
    };

    private static ScanEngine CreateEngine(IFileChecker checker) => new(
        checker,
        new PlaylistService(),
        new AlwaysSkipDecisionProvider());

    [Fact]
    public async Task All_files_are_checked_and_counters_match()
    {
        ScriptedChecker checker = new((item, _) => Task.FromResult(Ok(item)));
        using ScanEngine engine = CreateEngine(checker);

        List<FileCheckResult> received = [];
        engine.ResultsReady += (_, batch) => { lock (received) { received.AddRange(batch); } };

        ScanSummary? summary = null;
        engine.Finished += (_, s) => summary = s;

        await engine.RunAsync(Discovery(50), new AppSettings());

        Assert.Equal(ScanState.Completed, engine.State);
        Assert.Equal(50, received.Count);
        Assert.NotNull(summary);
        Assert.Equal(50, summary.Counters.Checked);
        Assert.Equal(50, summary.Counters.Ok);
        Assert.Equal(0, summary.Counters.Corrupted);
    }

    [Fact]
    public async Task Results_arrive_in_batches()
    {
        ScriptedChecker checker = new((item, _) => Task.FromResult(Ok(item)));
        using ScanEngine engine = CreateEngine(checker);

        int batches = 0;
        int total = 0;
        engine.ResultsReady += (_, batch) => { Interlocked.Increment(ref batches); Interlocked.Add(ref total, batch.Count); };

        await engine.RunAsync(Discovery(400), new AppSettings());

        Assert.Equal(400, total);
        // 400 results must not arrive as 400 events.
        Assert.True(batches < 400, $"порций {batches} — ожидалась пакетная отдача");
    }

    [Fact]
    public async Task Error_on_one_file_does_not_stop_others()
    {
        ScriptedChecker checker = new((item, _) => Task.FromResult(
            item.FullPath.EndsWith("track0005.flac", StringComparison.Ordinal)
                ? WithIssue(item, new CheckIssue(IssueCode.DecodeStartFailed, "не открылся"))
                : Ok(item)));

        using ScanEngine engine = CreateEngine(checker);

        ScanSummary? summary = null;
        engine.Finished += (_, s) => summary = s;

        await engine.RunAsync(Discovery(20), new AppSettings());

        Assert.Equal(ScanState.Completed, engine.State);
        Assert.Equal(20, summary!.Counters.Checked);
        Assert.Equal(1, summary.Counters.Corrupted);
        Assert.Equal(19, summary.Counters.Ok);
    }

    [Fact]
    public async Task Stop_aborts_scan_and_keeps_results()
    {
        TaskCompletionSource gate = new();

        ScriptedChecker checker = new(async (item, token) =>
        {
            if (item.FullPath.EndsWith("track0000.flac", StringComparison.Ordinal))
            {
                gate.TrySetResult();
            }

            await Task.Delay(30, token);
            return Ok(item);
        });

        using ScanEngine engine = CreateEngine(checker);

        List<FileCheckResult> received = [];
        engine.ResultsReady += (_, batch) => { lock (received) { received.AddRange(batch); } };

        Task run = engine.RunAsync(Discovery(2000), new AppSettings());
        await gate.Task;
        await Task.Delay(80);
        engine.Stop();
        await run;

        Assert.Equal(ScanState.Stopped, engine.State);
        Assert.True(checker.Finished < 2000, "проверка должна была прерваться, а не доработать до конца");
        Assert.NotEmpty(received);
    }

    [Fact]
    public async Task Pause_finishes_running_checks_and_takes_no_new_ones()
    {
        TaskCompletionSource started = new();
        int completed = 0;
        int running = 0;

        ScriptedChecker checker = new(async (item, token) =>
        {
            Interlocked.Increment(ref running);
            try
            {
                started.TrySetResult();
                await Task.Delay(20, token);
                Interlocked.Increment(ref completed);
                return Ok(item);
            }
            finally
            {
                Interlocked.Decrement(ref running);
            }
        });

        using ScanEngine engine = CreateEngine(checker);
        AppSettings settings = new() { AutoParallelism = false, ManualParallelism = 2 };

        Task run = engine.RunAsync(Discovery(500), settings);
        await started.Task;
        engine.Pause();

        Assert.Equal(ScanState.Paused, engine.State);

        // Files in flight must finish, and no new ones may be taken. Wait for the
        // in-flight checks themselves rather than a fixed delay: a slow CI runner
        // can take longer than any fixed guess.
        // A worker may have taken a file just before the pause without having
        // registered yet, so idleness has to hold for a while, not just once.
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        DateTime idleSince = DateTime.MaxValue;
        while (DateTime.UtcNow < deadline)
        {
            if (Volatile.Read(ref running) > 0)
            {
                idleSince = DateTime.MaxValue;
            }
            else if (idleSince == DateTime.MaxValue)
            {
                idleSince = DateTime.UtcNow;
            }
            else if (DateTime.UtcNow - idleSince > TimeSpan.FromMilliseconds(150))
            {
                break;
            }

            await Task.Delay(10);
        }

        Assert.Equal(0, Volatile.Read(ref running));
        int afterPause = Volatile.Read(ref completed);
        await Task.Delay(200);

        Assert.Equal(afterPause, Volatile.Read(ref completed));
        Assert.True(afterPause > 0, "checks in flight must have completed");

        engine.Resume();
        Assert.Equal(ScanState.Running, engine.State);

        engine.Stop();
        await run;
    }

    [Fact]
    public async Task Stop_works_while_paused()
    {
        TaskCompletionSource started = new();

        ScriptedChecker checker = new(async (item, token) =>
        {
            started.TrySetResult();
            await Task.Delay(10, token);
            return Ok(item);
        });

        using ScanEngine engine = CreateEngine(checker);

        Task run = engine.RunAsync(Discovery(5000), new AppSettings());
        await started.Task;
        engine.Pause();
        await Task.Delay(50);

        engine.Stop();

        // Without lifting the pause the threads would hang forever; the scan must not time out.
        await run.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ScanState.Stopped, engine.State);
    }

    [Fact]
    public async Task Critical_failure_stops_scan_and_keeps_results()
    {
        int checkedCount = 0;

        ScriptedChecker checker = new((item, _) =>
        {
            if (Interlocked.Increment(ref checkedCount) > 10)
            {
                throw new AudioEngineFailureException("Декодер отказал.", "BASS → Memory");
            }

            return Task.FromResult(Ok(item));
        });

        using ScanEngine engine = CreateEngine(checker);

        ScanSummary? summary = null;
        engine.Finished += (_, s) => summary = s;

        await engine.RunAsync(Discovery(100), new AppSettings { AutoParallelism = false, ManualParallelism = 1 });

        Assert.Equal(ScanState.Failed, engine.State);
        Assert.NotNull(summary);
        Assert.Equal("Декодер отказал.", summary.CriticalFailure);
        Assert.True(summary.Counters.Ok > 0, "уже проверенные файлы должны сохраниться");
    }

    [Fact]
    public async Task Parallelism_does_not_exceed_limit()
    {
        int current = 0;
        int peak = 0;

        ScriptedChecker checker = new(async (item, token) =>
        {
            int now = Interlocked.Increment(ref current);
            InterlockedMax(ref peak, now);
            await Task.Delay(15, token);
            Interlocked.Decrement(ref current);
            return Ok(item);
        });

        using ScanEngine engine = CreateEngine(checker);
        await engine.RunAsync(Discovery(60), new AppSettings { AutoParallelism = false, ManualParallelism = 3 });

        Assert.InRange(peak, 1, 3);
    }

    [Fact]
    public async Task Warnings_arrive_during_scan()
    {
        ScriptedChecker checker = new((item, _) => Task.FromResult(
            WithIssue(item, new CheckIssue(IssueCode.MetadataProblem, "нет тегов"))));

        using ScanEngine engine = CreateEngine(checker);

        List<LiveWarning> warnings = [];
        engine.WarningRaised += (_, w) => { lock (warnings) { warnings.Add(w); } };

        await engine.RunAsync(Discovery(5), new AppSettings());

        Assert.Equal(5, warnings.Count);
        Assert.All(warnings, w => Assert.Equal(IssueCode.MetadataProblem, w.Code));
    }

    [Fact]
    public async Task Playlists_are_checked_after_files_and_see_their_statuses()
    {
        using TempDirectory temp = new();
        string track = temp.WriteBytes("track.flac", 1, 2, 3);
        string playlist = temp.WriteText("list.m3u", "track.flac\nmissing.flac\n");

        DiscoveryResult discovery = new()
        {
            RootPath = temp.Path,
            AudioItems = [new ScanItem(track, 3, ScanItemKind.Audio)],
            Playlists = [new ScanItem(playlist, 32, ScanItemKind.Playlist)],
            FolderCount = 1,
            InaccessibleFolders = [],
        };

        ScriptedChecker checker = new((item, _) => Task.FromResult(
            WithIssue(item, new CheckIssue(IssueCode.DecodeStartFailed, "битый"))));

        using ScanEngine engine = CreateEngine(checker);

        IReadOnlyList<PlaylistCheckResult>? playlists = null;
        engine.PlaylistsReady += (_, p) => playlists = p;

        ScanSummary? summary = null;
        engine.Finished += (_, s) => summary = s;

        await engine.RunAsync(discovery, new AppSettings());

        Assert.NotNull(playlists);
        Assert.Single(playlists);
        Assert.Equal(1, playlists[0].MissingCount);
        Assert.Equal(CheckStatus.Corrupted, playlists[0].Entries[0].KnownStatus);
        Assert.Equal(1, summary!.PlaylistCount);
        Assert.Equal(1, summary.PlaylistMissingLinks);
    }

    [Fact]
    public async Task Starting_during_scan_is_rejected()
    {
        TaskCompletionSource started = new();

        ScriptedChecker checker = new(async (item, token) =>
        {
            started.TrySetResult();
            await Task.Delay(20, token);
            return Ok(item);
        });

        using ScanEngine engine = CreateEngine(checker);
        Task run = engine.RunAsync(Discovery(500), new AppSettings());
        await started.Task;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => engine.RunAsync(Discovery(10), new AppSettings()));

        engine.Stop();
        await run;
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current = Volatile.Read(ref target);
        while (value > current)
        {
            int previous = Interlocked.CompareExchange(ref target, value, current);
            if (previous == current)
            {
                return;
            }

            current = previous;
        }
    }
}

public sealed class LockedFileQuestionQueueTests
{
    private sealed class RecordingProvider(LockedFileDecision decision) : ILockedFileDecisionProvider
    {
        public int Calls;
        public int MaxConcurrent;
        private int _current;

        public async Task<LockedFileDecision> AskAsync(LockedFileQuestion question, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            int now = Interlocked.Increment(ref _current);
            MaxConcurrent = Math.Max(MaxConcurrent, now);

            await Task.Delay(20, cancellationToken);

            Interlocked.Decrement(ref _current);
            return decision;
        }
    }

    [Fact]
    public async Task Questions_are_asked_one_at_a_time()
    {
        RecordingProvider provider = new(LockedFileDecision.Skip);
        using LockedFileQuestionQueue queue = new(provider);

        await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            queue.AskAsync($@"D:\file{i}.wav", 1024, LockOwnerResult.Unknown(), CancellationToken.None)));

        Assert.Equal(6, provider.Calls);
        Assert.Equal(1, provider.MaxConcurrent);
    }

    [Fact]
    public async Task Apply_to_all_answers_remaining_questions()
    {
        RecordingProvider provider = new(new LockedFileDecision(LockedFileAction.Skip, ApplyToAll: true));
        using LockedFileQuestionQueue queue = new(provider);

        for (int i = 0; i < 5; i++)
        {
            LockedFileDecision decision = await queue.AskAsync(
                $@"D:\file{i}.wav", 1024, LockOwnerResult.Unknown(), CancellationToken.None);

            Assert.Equal(LockedFileAction.Skip, decision.Action);
        }

        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task Stop_from_question_ends_further_questions()
    {
        RecordingProvider provider = new(new LockedFileDecision(LockedFileAction.Skip, StopScan: true));
        using LockedFileQuestionQueue queue = new(provider);

        LockedFileDecision first = await queue.AskAsync(@"D:\a.wav", 1, LockOwnerResult.Unknown(), CancellationToken.None);
        LockedFileDecision second = await queue.AskAsync(@"D:\b.wav", 1, LockOwnerResult.Unknown(), CancellationToken.None);

        Assert.True(first.StopScan);
        Assert.True(second.StopScan);
        Assert.Equal(1, provider.Calls);
    }
}

public sealed class TempCopyManagerTests
{
    [Fact]
    public async Task Copy_is_deleted_even_when_check_fails()
    {
        using TempDirectory source = new();
        using TempDirectory copies = new();
        string file = source.WriteBytes("track.wav", 1, 2, 3, 4);

        TempCopyManager manager = new(copies.Path);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using TempCopy copy = await manager.CreateAsync(file);
            Assert.True(copy.Created);
            Assert.True(File.Exists(copy.Path));
            throw new InvalidOperationException("проверка сорвалась");
        });

        Assert.Empty(Directory.GetFiles(copies.Path));
    }

    [Fact]
    public void Orphaned_copies_from_previous_run_are_cleaned()
    {
        using TempDirectory copies = new();
        File.WriteAllBytes(Path.Combine(copies.Path, "abcdef123456.tmp"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(copies.Path, "fedcba654321.tmp"), [1, 2, 3]);

        TempCopyManager manager = new(copies.Path);

        Assert.Equal(2, manager.CleanupOrphans());
        Assert.Empty(Directory.GetFiles(copies.Path));
    }
}
