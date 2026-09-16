using MusicScanIntegrity.Core.Audio;
using MusicScanIntegrity.Core.Integrity;
using MusicScanIntegrity.Core.Locking;
using MusicScanIntegrity.Core.History;
using MusicScanIntegrity.Core.Models;
using MusicScanIntegrity.Core.Scanning;
using MusicScanIntegrity.Core.Settings;
using Xunit;

namespace MusicScanIntegrity.Core.Tests;

public sealed class ScanHistoryTests
{
    private static FileHistoryEntry Entry(string path, string hash = "aabbccdd", long size = 1024) =>
        new(path, size, new DateTime(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc), hash, CheckStatus.Ok, DateTimeOffset.Now);

    [Fact]
    public void Entry_reads_back()
    {
        using TempDirectory temp = new();
        using ScanHistory history = new();

        Assert.Null(history.Open(Path.Combine(temp.Path, "history.db")));
        history.Save(Entry(@"D:\Music\трек.flac"));

        FileHistoryEntry? found = history.Find(@"D:\Music\трек.flac");

        Assert.NotNull(found);
        Assert.Equal("aabbccdd", found!.Hash);
        Assert.Equal(1024, found.SizeBytes);
        Assert.Equal(CheckStatus.Ok, found.Status);
    }

    [Fact]
    public void Second_write_updates_entry()
    {
        using TempDirectory temp = new();
        using ScanHistory history = new();
        history.Open(Path.Combine(temp.Path, "history.db"));

        history.Save(Entry(@"D:\Music\трек.flac", "старый"));
        history.Save(Entry(@"D:\Music\трек.flac", "новый"));

        Assert.Equal(1, history.Count());
        Assert.Equal("новый", history.Find(@"D:\Music\трек.flac")!.Hash);
    }

    [Fact]
    public void Database_survives_close_and_reopen()
    {
        using TempDirectory temp = new();
        string path = Path.Combine(temp.Path, "history.db");

        using (ScanHistory first = new())
        {
            first.Open(path);
            first.Save(Entry(@"D:\Music\трек.flac"));
        }

        using ScanHistory second = new();
        second.Open(path);

        Assert.Equal(1, second.Count());
    }

    [Fact]
    public void Clear_removes_all_entries()
    {
        using TempDirectory temp = new();
        using ScanHistory history = new();
        history.Open(Path.Combine(temp.Path, "history.db"));

        history.Save(Entry(@"D:\Music\один.flac"));
        history.Save(Entry(@"D:\Music\два.flac"));
        history.Clear();

        Assert.Equal(0, history.Count());
    }

    [Fact]
    public void Closed_history_knows_nothing()
    {
        using ScanHistory history = new();

        Assert.False(history.IsOpen);
        Assert.Null(history.Find(@"D:\Music\трек.flac"));
        Assert.Equal(0, history.Count());
    }

    [Fact]
    public void Fingerprint_changes_with_contents()
    {
        using TempDirectory temp = new();
        string path = temp.WriteBytes("файл.bin", [1, 2, 3, 4, 5]);

        string? first = FileHasher.Compute(path, CancellationToken.None);
        File.WriteAllBytes(path, [1, 2, 3, 4, 6]);
        string? second = FileHasher.Compute(path, CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Identical_contents_share_fingerprint()
    {
        using TempDirectory temp = new();
        string one = temp.WriteBytes("один.bin", [7, 7, 7, 7]);
        string two = temp.WriteBytes("два.bin", [7, 7, 7, 7]);

        Assert.Equal(
            FileHasher.Compute(one, CancellationToken.None),
            FileHasher.Compute(two, CancellationToken.None));
    }

    [Fact]
    public async Task Silent_corruption_is_found_by_fingerprint()
    {
        using TempDirectory temp = new();
        using ScanHistory history = new();
        history.Open(Path.Combine(temp.Path, "history.db"));

        string path = temp.WriteBytes("трек.flac", [.. Enumerable.Repeat((byte)42, 4096)]);
        FileInfo info = new(path);

        // Previous scan: same size and date, different contents.
        history.Save(new FileHistoryEntry(
            path,
            info.Length,
            info.LastWriteTimeUtc,
            "0000000000000000",
            CheckStatus.Ok,
            DateTimeOffset.Now.AddDays(-30)));

        FileChecker checker = new(
            new FakeAudioProbe(),
            new FakeMetadataReader(),
            new UnknownOwnerDetector(),
            new TempCopyManager(),
            new ContainerIntegrityChecker(),
            history);

        FileCheckResult result = await checker.CheckAsync(
            new ScanItem(path, info.Length, ScanItemKind.Audio),
            new FileCheckContext(new AppSettings { TrackChanges = true, VerifyContainerIntegrity = false }, null),
            CancellationToken.None);

        Assert.Contains(result.Issues, i => i.Code == IssueCode.SilentCorruption);
        Assert.Equal(CheckStatus.Corrupted, result.Status);
    }

    [Fact]
    public async Task Unchanged_file_is_not_rechecked()
    {
        using TempDirectory temp = new();
        using ScanHistory history = new();
        history.Open(Path.Combine(temp.Path, "history.db"));

        string path = temp.WriteBytes("трек.flac", [.. Enumerable.Repeat((byte)7, 4096)]);
        FileInfo info = new(path);

        history.Save(new FileHistoryEntry(
            path,
            info.Length,
            info.LastWriteTimeUtc,
            FileHasher.Compute(path, CancellationToken.None)!,
            CheckStatus.Ok,
            DateTimeOffset.Now.AddDays(-1)));

        FakeAudioProbe probe = new();
        FileChecker checker = new(
            probe,
            new FakeMetadataReader(),
            new UnknownOwnerDetector(),
            new TempCopyManager(),
            new ContainerIntegrityChecker(),
            history);

        FileCheckResult result = await checker.CheckAsync(
            new ScanItem(path, info.Length, ScanItemKind.Audio),
            new FileCheckContext(new AppSettings { TrackChanges = true }, null),
            CancellationToken.None);

        Assert.True(result.Unchanged);
        Assert.Equal(CheckStatus.Ok, result.Status);
        Assert.Equal(0, probe.Calls);
        Assert.Contains("не изменился", result.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Changed_file_is_rechecked()
    {
        using TempDirectory temp = new();
        using ScanHistory history = new();
        history.Open(Path.Combine(temp.Path, "history.db"));

        string path = temp.WriteBytes("трек.flac", [.. Enumerable.Repeat((byte)7, 4096)]);
        FileInfo info = new(path);

        // Contents and date both changed: an ordinary edit, not corruption.
        history.Save(new FileHistoryEntry(
            path,
            info.Length,
            info.LastWriteTimeUtc.AddDays(-5),
            "0000000000000000",
            CheckStatus.Ok,
            DateTimeOffset.Now.AddDays(-5)));

        FakeAudioProbe probe = new();
        FileChecker checker = new(
            probe,
            new FakeMetadataReader(),
            new UnknownOwnerDetector(),
            new TempCopyManager(),
            new ContainerIntegrityChecker(),
            history);

        FileCheckResult result = await checker.CheckAsync(
            new ScanItem(path, info.Length, ScanItemKind.Audio),
            new FileCheckContext(new AppSettings { TrackChanges = true, VerifyContainerIntegrity = false }, null),
            CancellationToken.None);

        Assert.False(result.Unchanged);
        Assert.Equal(1, probe.Calls);
        Assert.DoesNotContain(result.Issues, i => i.Code == IssueCode.SilentCorruption);
    }

    [Fact]
    public async Task History_is_untouched_when_setting_is_off()
    {
        using TempDirectory temp = new();
        using ScanHistory history = new();
        history.Open(Path.Combine(temp.Path, "history.db"));

        string path = temp.WriteBytes("трек.flac", [.. Enumerable.Repeat((byte)7, 1024)]);

        FileChecker checker = new(
            new FakeAudioProbe(),
            new FakeMetadataReader(),
            new UnknownOwnerDetector(),
            new TempCopyManager(),
            new ContainerIntegrityChecker(),
            history);

        await checker.CheckAsync(
            new ScanItem(path, 1024, ScanItemKind.Audio),
            new FileCheckContext(new AppSettings { TrackChanges = false }, null),
            CancellationToken.None);

        Assert.Equal(0, history.Count());
    }

    [Fact]
    public void Date_outside_file_time_does_not_break_write()
    {
        // Dates before 1601 cannot be converted to Windows file time. ToFileTimeUtc
        // used to throw, and a healthy file got an "unexpected error" in the report.
        // Such dates do occur: damaged filesystem records, archives without dates.
        using TempDirectory temp = new();
        using ScanHistory history = new();
        history.Open(Path.Combine(temp.Path, "history.db"));

        FileHistoryEntry entry = new(
            @"D:\Music\трек.flac",
            1024,
            new DateTime(1500, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            "aabbccdd",
            CheckStatus.Ok,
            DateTimeOffset.Now);

        history.Save(entry);

        FileHistoryEntry? found = history.Find(@"D:\Music\трек.flac");

        Assert.NotNull(found);
        Assert.Equal("aabbccdd", found!.Hash);

        // The date is not invented: it cannot match the file's real date, so the file
        // will not be reported as silently corrupted.
        Assert.NotEqual(entry.ModifiedUtc, found.ModifiedUtc);
    }

    [Fact]
    public void Clear_and_count_on_closed_database_do_not_throw()
    {
        using ScanHistory history = new();

        // The "Clear history" button calls this on the UI thread, where an escaping
        // exception would crash the app.
        history.Clear();

        Assert.Equal(0, history.Count());
        Assert.False(history.IsOpen);
    }

    [Fact]
    public async Task Closing_during_write_does_not_break_connection()
    {
        // Real case: the scan writes from worker threads while the corruption-
        // tracking toggle closes the database from the UI thread.
        using TempDirectory temp = new();
        using ScanHistory history = new();
        history.Open(Path.Combine(temp.Path, "history.db"));

        using CancellationTokenSource stop = new(TimeSpan.FromSeconds(2));
        List<Exception> failures = [];

        Task writer = Task.Run(() =>
        {
            int i = 0;
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    history.Save(Entry($@"D:\Music\{i++}.flac"));
                    history.Find(@"D:\Music\0.flac");
                }
                catch (Exception ex)
                {
                    lock (failures)
                    {
                        failures.Add(ex);
                    }

                    return;
                }
            }
        });

        Task closer = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    history.Close();
                    history.Open(Path.Combine(temp.Path, "history.db"));
                }
                catch (Exception ex)
                {
                    lock (failures)
                    {
                        failures.Add(ex);
                    }

                    return;
                }
            }
        });

        await Task.WhenAll(writer, closer);

        Assert.Empty(failures);
    }
}
