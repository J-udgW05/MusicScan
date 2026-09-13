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
    public void Запись_читается_обратно()
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
    public void Повторная_запись_обновляет_прежнюю()
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
    public void База_переживает_закрытие_и_открытие()
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
    public void Очистка_убирает_все_записи()
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
    public void Закрытая_история_ничего_не_знает()
    {
        using ScanHistory history = new();

        Assert.False(history.IsOpen);
        Assert.Null(history.Find(@"D:\Music\трек.flac"));
        Assert.Equal(0, history.Count());
    }

    [Fact]
    public void Отпечаток_меняется_вместе_с_содержимым()
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
    public void Отпечаток_одинакового_содержимого_совпадает()
    {
        using TempDirectory temp = new();
        string one = temp.WriteBytes("один.bin", [7, 7, 7, 7]);
        string two = temp.WriteBytes("два.bin", [7, 7, 7, 7]);

        Assert.Equal(
            FileHasher.Compute(one, CancellationToken.None),
            FileHasher.Compute(two, CancellationToken.None));
    }

    [Fact]
    public async Task Тихая_порча_находится_по_отпечатку()
    {
        using TempDirectory temp = new();
        using ScanHistory history = new();
        history.Open(Path.Combine(temp.Path, "history.db"));

        string path = temp.WriteBytes("трек.flac", [.. Enumerable.Repeat((byte)42, 4096)]);
        FileInfo info = new(path);

        // Прошлая проверка: тот же размер и та же дата, но другое содержимое.
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
    public async Task Неизменившийся_файл_повторно_не_проверяется()
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
    public async Task Изменённый_файл_проверяется_заново()
    {
        using TempDirectory temp = new();
        using ScanHistory history = new();
        history.Open(Path.Combine(temp.Path, "history.db"));

        string path = temp.WriteBytes("трек.flac", [.. Enumerable.Repeat((byte)7, 4096)]);
        FileInfo info = new(path);

        // Содержимое и дата изменились — это обычная правка, а не порча.
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
    public async Task Без_настройки_история_не_трогается()
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
    public void Дата_вне_файлового_времени_не_ломает_запись()
    {
        // Дата до 1601 года не переводится в файловое время Windows: раньше
        // ToFileTimeUtc бросал исключение, и исправный файл получал в отчёте
        // «непредвиденную ошибку». Такие даты в коллекциях встречаются —
        // испорченная запись файловой системы, распаковка архива без дат.
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

        // Дату мы не выдумываем: она заведомо не совпадёт с настоящей датой
        // файла, поэтому «тихой порчей» такой файл объявлен не будет.
        Assert.NotEqual(entry.ModifiedUtc, found.ModifiedUtc);
    }

    [Fact]
    public void Очистка_и_счёт_на_закрытой_базе_не_бросают()
    {
        using ScanHistory history = new();

        // Кнопка «Очистить историю» вызывает это из потока окна: исключение
        // отсюда осталось бы непойманным и уронило бы программу.
        history.Clear();

        Assert.Equal(0, history.Count());
        Assert.False(history.IsOpen);
    }

    [Fact]
    public async Task Закрытие_во_время_записи_не_ломает_соединение()
    {
        // Настоящий случай: проверка пишет в базу из рабочих потоков, а
        // выключатель «Следить за порчей» закрывает базу из потока окна.
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
