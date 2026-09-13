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
    /// <summary>Проверка файла, поведение которой полностью задаёт тест.</summary>
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
    public async Task Все_файлы_проверяются_и_счётчики_сходятся()
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
    public async Task Результаты_приходят_порциями_а_не_по_одному()
    {
        ScriptedChecker checker = new((item, _) => Task.FromResult(Ok(item)));
        using ScanEngine engine = CreateEngine(checker);

        int batches = 0;
        int total = 0;
        engine.ResultsReady += (_, batch) => { Interlocked.Increment(ref batches); Interlocked.Add(ref total, batch.Count); };

        await engine.RunAsync(Discovery(400), new AppSettings());

        Assert.Equal(400, total);
        // 400 результатов не должны прийти четырьмястами событиями.
        Assert.True(batches < 400, $"порций {batches} — ожидалась пакетная отдача");
    }

    [Fact]
    public async Task Ошибка_на_одном_файле_не_останавливает_остальные()
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
    public async Task Стоп_прерывает_проверку_и_сохраняет_уже_полученные_результаты()
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
    public async Task Пауза_не_обрывает_начатые_проверки_но_не_берёт_новые()
    {
        TaskCompletionSource started = new();
        int completed = 0;

        ScriptedChecker checker = new(async (item, token) =>
        {
            started.TrySetResult();
            await Task.Delay(20, token);
            Interlocked.Increment(ref completed);
            return Ok(item);
        });

        using ScanEngine engine = CreateEngine(checker);
        AppSettings settings = new() { AutoParallelism = false, ManualParallelism = 2 };

        Task run = engine.RunAsync(Discovery(500), settings);
        await started.Task;
        engine.Pause();

        Assert.Equal(ScanState.Paused, engine.State);

        // Уже начатые файлы должны докрутиться, а новые — не браться.
        await Task.Delay(200);
        int afterPause = Volatile.Read(ref completed);
        await Task.Delay(200);

        Assert.Equal(afterPause, Volatile.Read(ref completed));
        Assert.True(afterPause > 0, "начатые проверки обязаны были завершиться");

        engine.Resume();
        Assert.Equal(ScanState.Running, engine.State);

        engine.Stop();
        await run;
    }

    [Fact]
    public async Task Стоп_работает_даже_когда_проверка_стоит_на_паузе()
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

        // Без снятия паузы потоки зависли бы навсегда — проверка не должна таймаутиться.
        await run.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ScanState.Stopped, engine.State);
    }

    [Fact]
    public async Task Критический_сбой_останавливает_проверку_но_сохраняет_накопленное()
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
    public async Task Параллельность_не_превышает_заданную()
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
    public async Task Предупреждения_приходят_по_ходу_проверки_а_не_только_в_конце()
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
    public async Task Плейлисты_проверяются_после_файлов_и_видят_их_статусы()
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
    public async Task Повторный_запуск_во_время_проверки_запрещён()
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
    public async Task Вопросы_задаются_по_одному_а_не_пачкой()
    {
        RecordingProvider provider = new(LockedFileDecision.Skip);
        using LockedFileQuestionQueue queue = new(provider);

        await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            queue.AskAsync($@"D:\file{i}.wav", 1024, LockOwnerResult.Unknown(), CancellationToken.None)));

        Assert.Equal(6, provider.Calls);
        Assert.Equal(1, provider.MaxConcurrent);
    }

    [Fact]
    public async Task Ответ_ко_всем_избавляет_от_остальных_вопросов()
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
    public async Task Остановка_из_вопроса_прекращает_дальнейшие_вопросы()
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
    public async Task Копия_удаляется_даже_если_проверка_упала()
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
    public void Осиротевшие_копии_от_прошлого_запуска_подчищаются()
    {
        using TempDirectory copies = new();
        File.WriteAllBytes(Path.Combine(copies.Path, "abcdef123456.tmp"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(copies.Path, "fedcba654321.tmp"), [1, 2, 3]);

        TempCopyManager manager = new(copies.Path);

        Assert.Equal(2, manager.CleanupOrphans());
        Assert.Empty(Directory.GetFiles(copies.Path));
    }
}
