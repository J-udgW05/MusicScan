using MusicScanIntegrity.Core.Analysis;
using MusicScanIntegrity.Core.History;
using MusicScanIntegrity.Core.Audio;
using MusicScanIntegrity.Core.Locking;
using MusicScanIntegrity.Core.Models;
using MusicScanIntegrity.Core.Scanning;
using MusicScanIntegrity.Core.Integrity;
using MusicScanIntegrity.Core.Settings;
using Xunit;

namespace MusicScanIntegrity.Core.Tests;

public sealed class FileCheckerTests
{
    private static FileChecker Create(
        IAudioProbe? probe = null,
        IMetadataReader? metadata = null,
        ITempCopyManager? tempCopies = null,
        IScanHistory? history = null) => new(
            probe ?? new FakeAudioProbe(),
            metadata ?? new FakeMetadataReader(new TrackMetadata("Название", "Исполнитель", "Альбом", 214)),
            new UnknownOwnerDetector(),
            tempCopies ?? new TempCopyManager(),
            new ContainerIntegrityChecker(),
            history ?? NoHistory.Instance);

    [Theory]
    [InlineData(CheckDepth.Quick, DecodeScope.Quick)]
    [InlineData(CheckDepth.Sampled, DecodeScope.Sampled)]
    [InlineData(CheckDepth.Full, DecodeScope.Full)]
    public async Task Настройка_глубины_доходит_до_декодера(CheckDepth depth, DecodeScope expected)
    {
        FakeAudioProbe probe = new();
        FileChecker checker = Create(probe);
        using TempDirectory temp = new();
        string path = temp.WriteBytes("трек.flac", new byte[2048]);

        await checker.CheckAsync(
            Item(path),
            Context(new AppSettings { CheckDepth = depth, VerifyContainerIntegrity = false }),
            CancellationToken.None);

        Assert.Equal(expected, probe.LastScope);
    }

    [Fact]
    public async Task Обрыв_по_данным_декодера_делает_файл_повреждённым()
    {
        FakeAudioProbe probe = new(_ =>
            AudioProbeResult.Success(12, "MP3", declared: 210) with { Truncated = true });

        FileChecker checker = Create(probe);
        using TempDirectory temp = new();
        string path = temp.WriteBytes("обрыв.mp3", new byte[2048]);

        FileCheckResult result = await checker.CheckAsync(
            Item(path),
            Context(new AppSettings { VerifyContainerIntegrity = false }),
            CancellationToken.None);

        Assert.Equal(CheckStatus.Corrupted, result.Status);
        Assert.Contains(result.Issues, i => i.Code == IssueCode.Truncated);
    }

    [Fact]
    public async Task Об_обрыве_не_сообщается_дважды()
    {
        // Разбор структуры уже назвал файл обрезанным — декодер не должен
        // добавлять второе такое же замечание.
        FakeAudioProbe probe = new(_ =>
            AudioProbeResult.Success(2, "FLAC", declared: 200) with { Truncated = true });

        FileChecker checker = Create(probe);
        using TempDirectory temp = new();

        // Обрезанный FLAC: подпись и описание потока есть, кадров нет.
        byte[] flac = [.. "fLaC"u8, 0x80, 0, 0, 34, .. new byte[34]];
        string path = temp.WriteBytes("обрыв.flac", flac);

        FileCheckResult result = await checker.CheckAsync(
            Item(path),
            Context(new AppSettings { VerifyContainerIntegrity = true }),
            CancellationToken.None);

        Assert.Single(result.Issues, i => i.Code == IssueCode.Truncated);
    }

    [Fact]
    public async Task Лослесс_без_верхних_частот_вызывает_подозрение()
    {
        FakeAudioProbe probe = new(_ => AudioProbeResult.Success(60, "FLAC") with
        {
            Stats = new AudioStats(4_000_000, 0.8, 0.2, 0, 0, 0, 88_200),
            Spectrum = new SpectrumProfile(15_500, 22_050, 40),
        });

        FileChecker checker = Create(probe);
        using TempDirectory temp = new();
        string path = temp.WriteBytes("подделка.flac", new byte[2048]);

        FileCheckResult result = await checker.CheckAsync(
            Item(path),
            Context(new AppSettings { VerifyContainerIntegrity = false }),
            CancellationToken.None);

        CheckIssue issue = Assert.Single(result.Issues, i => i.Code == IssueCode.TranscodeSuspected);
        Assert.Contains("Похоже", issue.Message, StringComparison.Ordinal);
        Assert.Equal(CheckStatus.Warning, result.Status);
    }

    [Fact]
    public async Task Лослесс_с_полной_полосой_подозрений_не_вызывает()
    {
        FakeAudioProbe probe = new(_ => AudioProbeResult.Success(60, "FLAC") with
        {
            Stats = new AudioStats(4_000_000, 0.8, 0.2, 0, 0, 0, 88_200),
            Spectrum = new SpectrumProfile(21_000, 22_050, 40),
        });

        FileChecker checker = Create(probe);
        using TempDirectory temp = new();
        string path = temp.WriteBytes("настоящий.flac", new byte[2048]);

        FileCheckResult result = await checker.CheckAsync(
            Item(path),
            Context(new AppSettings { VerifyContainerIntegrity = false }),
            CancellationToken.None);

        Assert.DoesNotContain(result.Issues, i => i.Code == IssueCode.TranscodeSuspected);
    }

    [Fact]
    public async Task Тихая_запись_под_подозрение_не_попадает()
    {
        // На тихом куске верхних частот не видно просто потому, что там нечему
        // звучать. Обвинять такой файл в перекодировании нельзя.
        FakeAudioProbe probe = new(_ => AudioProbeResult.Success(60, "FLAC") with
        {
            Stats = new AudioStats(4_000_000, 0.02, 0.005, 0, 0, 0, 88_200),
            Spectrum = new SpectrumProfile(12_000, 22_050, 40),
        });

        FileChecker checker = Create(probe);
        using TempDirectory temp = new();
        string path = temp.WriteBytes("тихий.flac", new byte[2048]);

        FileCheckResult result = await checker.CheckAsync(
            Item(path),
            Context(new AppSettings { VerifyContainerIntegrity = false }),
            CancellationToken.None);

        Assert.DoesNotContain(result.Issues, i => i.Code == IssueCode.TranscodeSuspected);
    }

    [Fact]
    public async Task Короткое_измерение_спектра_в_расчёт_не_идёт()
    {
        FakeAudioProbe probe = new(_ => AudioProbeResult.Success(60, "FLAC") with
        {
            Stats = new AudioStats(4_000_000, 0.8, 0.2, 0, 0, 0, 88_200),
            Spectrum = new SpectrumProfile(9_000, 22_050, 3),
        });

        FileChecker checker = Create(probe);
        using TempDirectory temp = new();
        string path = temp.WriteBytes("короткий.flac", new byte[2048]);

        FileCheckResult result = await checker.CheckAsync(
            Item(path),
            Context(new AppSettings { VerifyContainerIntegrity = false }),
            CancellationToken.None);

        Assert.DoesNotContain(result.Issues, i => i.Code == IssueCode.TranscodeSuspected);
    }

    private static FileCheckContext Context(AppSettings? settings = null) =>
        new(settings ?? new AppSettings(), LockedQuestions: null);

    private static ScanItem Item(string path, long size = 1024) => new(path, size, ScanItemKind.Audio);

    [Fact]
    public async Task Здоровый_файл_получает_статус_в_порядке()
    {
        using TempDirectory temp = new();
        string path = temp.WriteBytes("ok.flac", 1, 2, 3, 4);

        FileCheckResult result = await Create().CheckAsync(Item(path, 4), Context(), CancellationToken.None);

        Assert.Equal(CheckStatus.Ok, result.Status);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task Исчезнувший_файл_даёт_ошибку_файл_не_найден_а_не_повреждение_аудио()
    {
        using TempDirectory temp = new();
        string path = Path.Combine(temp.Path, "пропал.flac");

        FileCheckResult result = await Create().CheckAsync(Item(path), Context(), CancellationToken.None);

        Assert.Equal(CheckStatus.Corrupted, result.Status);
        Assert.Equal(IssueCode.FileNotFound, result.Issues[0].Code);
        Assert.Contains("не найден", result.Description);
    }

    [Fact]
    public async Task Пустой_файл_отмечается_отдельной_причиной()
    {
        using TempDirectory temp = new();
        string path = temp.WriteBytes("empty.ogg");

        FileCheckResult result = await Create().CheckAsync(Item(path, 0), Context(), CancellationToken.None);

        Assert.Equal(CheckStatus.Corrupted, result.Status);
        Assert.Equal(IssueCode.EmptyFile, result.Issues[0].Code);
    }

    [Fact]
    public async Task Ошибка_декодера_даёт_статус_повреждён()
    {
        using TempDirectory temp = new();
        string path = temp.WriteBytes("bad.mp3", 1, 2, 3);

        FakeAudioProbe probe = new(_ => new AudioProbeResult(
            AudioProbeOutcome.OpenFailed, "Не удалось начать декодирование.", "BASS → FileFormat"));

        FileCheckResult result = await Create(probe).CheckAsync(Item(path, 3), Context(), CancellationToken.None);

        Assert.Equal(CheckStatus.Corrupted, result.Status);
        Assert.Equal(IssueCode.DecodeStartFailed, result.Issues[0].Code);
        Assert.Equal("BASS → FileFormat", result.TechnicalDetail);
    }

    [Fact]
    public async Task Защищённый_файл_это_предупреждение_а_не_повреждение()
    {
        using TempDirectory temp = new();
        string path = temp.WriteBytes("drm.wma", 1, 2, 3);

        FakeAudioProbe probe = new(_ => new AudioProbeResult(
            AudioProbeOutcome.PasswordProtected, "Файл защищён лицензией."));

        FileCheckResult result = await Create(probe).CheckAsync(Item(path, 3), Context(), CancellationToken.None);

        Assert.Equal(CheckStatus.Warning, result.Status);
        Assert.Equal(IssueCode.PasswordProtected, result.Issues[0].Code);
    }

    [Fact]
    public async Task Таймаут_одного_файла_даёт_предупреждение_и_не_ломает_проверку()
    {
        using TempDirectory temp = new();
        string path = temp.WriteBytes("slow.wv", 1, 2, 3);

        FakeAudioProbe probe = new() { Delay = TimeSpan.FromSeconds(10) };
        AppSettings settings = new() { FileTimeoutSeconds = 1 };

        FileCheckResult result = await Create(probe).CheckAsync(Item(path, 3), Context(settings), CancellationToken.None);

        Assert.Equal(CheckStatus.Warning, result.Status);
        Assert.Equal(IssueCode.CheckTimeout, result.Issues[0].Code);
    }

    [Fact]
    public async Task Отсутствие_тегов_это_предупреждение_а_не_повреждение()
    {
        using TempDirectory temp = new();
        string path = temp.WriteBytes("notags.flac", 1, 2, 3);

        FileChecker checker = Create(metadata: new FakeMetadataReader(new TrackMetadata(null, null, null, 100)));
        AppSettings settings = new() { CheckMetadata = true, VerifyExtensionMatchesContent = false };

        FileCheckResult result = await checker.CheckAsync(Item(path, 3), Context(settings), CancellationToken.None);

        Assert.Equal(CheckStatus.Warning, result.Status);
        Assert.Equal(IssueCode.MetadataProblem, result.Issues[0].Code);
        Assert.Contains("не повреждение", result.Description);
        Assert.Equal("Нет тегов", result.StatusLabel);
    }

    [Fact]
    public async Task Теги_не_проверяются_если_настройка_выключена()
    {
        using TempDirectory temp = new();
        string path = temp.WriteBytes("notags.flac", 1, 2, 3);

        FileChecker checker = Create(metadata: new FakeMetadataReader(null, "нет тегов"));

        FileCheckResult result = await checker.CheckAsync(Item(path, 3), Context(), CancellationToken.None);

        Assert.Equal(CheckStatus.Ok, result.Status);
    }

    [Fact]
    public async Task Несовпадение_расширения_и_содержимого_даёт_предупреждение()
    {
        using TempDirectory temp = new();
        string path = temp.WriteBytes("track.mp3", 1, 2, 3);

        // Декодер говорит FLAC, а расширение .mp3 — это ровно тот случай из макета.
        FakeAudioProbe probe = new(_ => AudioProbeResult.Success(2, "FLAC"));

        FileCheckResult result = await Create(probe).CheckAsync(Item(path, 3), Context(), CancellationToken.None);

        Assert.Equal(CheckStatus.Warning, result.Status);
        Assert.Equal(IssueCode.ExtensionMismatch, result.Issues[0].Code);
        Assert.Equal("FLAC?", result.Format);
    }

    [Fact]
    public async Task Файл_меньше_порога_не_поднимает_предупреждение_о_размере()
    {
        using TempDirectory temp = new();
        string path = temp.WriteBytes("small.wav", new byte[2048]);

        AppSettings settings = new() { LargeFileThresholdMb = 1, VerifyExtensionMatchesContent = false };

        bool notified = false;
        FileCheckContext context = new(settings, null, (_, _) => notified = true);

        FakeAudioProbe probe = new(_ => AudioProbeResult.Success(2, "WAV"));
        FileCheckResult result = await Create(probe).CheckAsync(Item(path, 2048), context, CancellationToken.None);

        Assert.False(notified);
        Assert.Equal(CheckStatus.Ok, result.Status);
    }

    [Fact]
    public async Task Без_ограничения_размера_предупреждение_о_большом_файле_не_появляется()
    {
        using TempDirectory temp = new();
        string path = temp.WriteBytes("huge.wav", new byte[4096]);

        // 0 означает «без ограничений» — порог не срабатывает никогда.
        AppSettings settings = new() { LargeFileThresholdMb = 0, VerifyExtensionMatchesContent = false };

        bool notified = false;
        FileCheckContext context = new(settings, null, (_, _) => notified = true);

        FakeAudioProbe probe = new(_ => AudioProbeResult.Success(2, "WAV"));
        FileCheckResult result = await Create(probe).CheckAsync(Item(path, 4096), context, CancellationToken.None);

        Assert.False(notified);
        Assert.Equal(CheckStatus.Ok, result.Status);
    }

    [Fact]
    public async Task Файл_больше_порога_помечается_предупреждением_и_проверяется()
    {
        using TempDirectory temp = new();
        byte[] payload = new byte[3 * 1024 * 1024];
        string path = temp.WriteBytes("huge.wav", payload);

        AppSettings settings = new() { LargeFileThresholdMb = 1, VerifyExtensionMatchesContent = false };

        List<string> notified = [];
        FileCheckContext context = new(settings, null, (item, _) => notified.Add(item.FullPath));

        FakeAudioProbe probe = new(_ => AudioProbeResult.Success(2, "WAV"));
        FileCheckResult result = await Create(probe).CheckAsync(Item(path, payload.Length), context, CancellationToken.None);

        Assert.Single(notified);
        Assert.Equal(CheckStatus.Warning, result.Status);
        Assert.Contains(result.Issues, i => i.Code == IssueCode.LargeFile);
        // Проверка всё равно выполнена — декодер вызывался.
        Assert.Equal(1, probe.Calls);
    }

    [Fact]
    public async Task Критический_сбой_декодера_прерывает_проверку_особым_исключением()
    {
        using TempDirectory temp = new();
        string path = temp.WriteBytes("any.flac", 1, 2, 3);

        FakeAudioProbe probe = new(_ => new AudioProbeResult(
            AudioProbeOutcome.EngineFailure, "Декодер отказал.", "BASS → Memory"));

        await Assert.ThrowsAsync<AudioEngineFailureException>(
            () => Create(probe).CheckAsync(Item(path, 3), Context(), CancellationToken.None));
    }

    [Fact]
    public async Task Непредвиденное_исключение_становится_строкой_результата_а_не_падением()
    {
        using TempDirectory temp = new();
        string path = temp.WriteBytes("any.flac", 1, 2, 3);

        FakeAudioProbe probe = new(_ => throw new InvalidOperationException("что-то сломалось"));

        FileCheckResult result = await Create(probe).CheckAsync(Item(path, 3), Context(), CancellationToken.None);

        Assert.Equal(CheckStatus.Corrupted, result.Status);
        Assert.Equal(IssueCode.UnexpectedError, result.Issues[0].Code);
    }

    [Fact]
    public async Task Занятый_файл_пропускается_если_так_настроено()
    {
        using TempDirectory temp = new();
        string path = temp.WriteBytes("locked.wav", 1, 2, 3);

        // Держим файл эксклюзивно — так его видит другая программа.
        using FileStream hold = new(path, FileMode.Open, FileAccess.Read, FileShare.None);

        AppSettings settings = new() { LockedFileAction = LockedFileAction.Skip };
        FileCheckResult result = await Create().CheckAsync(Item(path, 3), Context(settings), CancellationToken.None);

        Assert.Equal(CheckStatus.Skipped, result.Status);
        Assert.Equal(IssueCode.LockedSkippedByUser, result.Issues[0].Code);
    }

    [Fact]
    public async Task Если_копию_занятого_файла_сделать_нельзя_программа_говорит_об_этом_честно()
    {
        using TempDirectory temp = new();
        using TempDirectory copies = new();
        string path = temp.WriteBytes("locked.wav", 1, 2, 3, 4, 5);

        // Владелец держит файл вообще без разделения доступа: прочитать его нельзя
        // ни напрямую, ни для копирования. Программа обязана сказать об этом прямо,
        // а не делать вид, что проверила файл.
        using FileStream hold = new(path, FileMode.Open, FileAccess.Read, FileShare.None);

        TempCopyManager manager = new(copies.Path);
        AppSettings settings = new() { LockedFileAction = LockedFileAction.TempCopy, VerifyExtensionMatchesContent = false };

        FakeAudioProbe probe = new(_ => AudioProbeResult.Success(2, "WAV"));
        FileCheckResult result = await Create(probe, tempCopies: manager)
            .CheckAsync(Item(path, 5), Context(settings), CancellationToken.None);

        Assert.Contains(result.Issues, i => i.Code == IssueCode.LockedCopyFailed);
        Assert.Equal(0, probe.Calls);

        // И главное: незавершённая копия не осталась на диске.
        Assert.Empty(Directory.GetFiles(copies.Path));
    }

    [Fact]
    public async Task Файл_открытый_другой_программой_на_чтение_проверяется_как_обычно()
    {
        using TempDirectory temp = new();
        string path = temp.WriteBytes("playing.wav", 1, 2, 3, 4, 5);

        // Обычный случай: проигрыватель держит файл, но разрешает чтение.
        // Считать такой файл «занятым» и дёргать пользователя вопросом — неверно.
        using FileStream hold = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        AppSettings settings = new() { VerifyExtensionMatchesContent = false };
        FakeAudioProbe probe = new(_ => AudioProbeResult.Success(2, "WAV"));

        FileCheckResult result = await Create(probe)
            .CheckAsync(Item(path, 5), Context(settings), CancellationToken.None);

        Assert.Equal(CheckStatus.Ok, result.Status);
        Assert.Equal(1, probe.Calls);
    }
}
