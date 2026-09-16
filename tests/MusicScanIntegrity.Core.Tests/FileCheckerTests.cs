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
    public async Task Depth_setting_reaches_decoder(CheckDepth depth, DecodeScope expected)
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
    public async Task Decoder_truncation_marks_file_corrupted()
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
    public async Task Truncation_is_not_reported_twice()
    {
        // The structure check already reported truncation; the decoder must not add a
        // second identical finding.
        FakeAudioProbe probe = new(_ =>
            AudioProbeResult.Success(2, "FLAC", declared: 200) with { Truncated = true });

        FileChecker checker = Create(probe);
        using TempDirectory temp = new();

        // Truncated FLAC: signature and STREAMINFO present, no frames.
        byte[] flac = [.. "fLaC"u8, 0x80, 0, 0, 34, .. new byte[34]];
        string path = temp.WriteBytes("обрыв.flac", flac);

        FileCheckResult result = await checker.CheckAsync(
            Item(path),
            Context(new AppSettings { VerifyContainerIntegrity = true }),
            CancellationToken.None);

        Assert.Single(result.Issues, i => i.Code == IssueCode.Truncated);
    }

    [Fact]
    public async Task Lossless_without_top_end_is_suspected()
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
    public async Task Full_band_lossless_is_not_suspected()
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
    public async Task Quiet_recording_is_not_suspected()
    {
        // A quiet passage lacks top end only because nothing is sounding; that is no
        // grounds to call the file re-encoded.
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
    public async Task Short_spectrum_measurement_is_ignored()
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
    public async Task Healthy_file_is_ok()
    {
        using TempDirectory temp = new();
        string path = temp.WriteBytes("ok.flac", 1, 2, 3, 4);

        FileCheckResult result = await Create().CheckAsync(Item(path, 4), Context(), CancellationToken.None);

        Assert.Equal(CheckStatus.Ok, result.Status);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task Vanished_file_reports_not_found_rather_than_damaged_audio()
    {
        using TempDirectory temp = new();
        string path = Path.Combine(temp.Path, "пропал.flac");

        FileCheckResult result = await Create().CheckAsync(Item(path), Context(), CancellationToken.None);

        Assert.Equal(CheckStatus.Corrupted, result.Status);
        Assert.Equal(IssueCode.FileNotFound, result.Issues[0].Code);
        Assert.Contains("не найден", result.Description);
    }

    [Fact]
    public async Task Empty_file_has_its_own_cause()
    {
        using TempDirectory temp = new();
        string path = temp.WriteBytes("empty.ogg");

        FileCheckResult result = await Create().CheckAsync(Item(path, 0), Context(), CancellationToken.None);

        Assert.Equal(CheckStatus.Corrupted, result.Status);
        Assert.Equal(IssueCode.EmptyFile, result.Issues[0].Code);
    }

    [Fact]
    public async Task Decoder_error_marks_file_corrupted()
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
    public async Task Protected_file_is_a_warning_not_damage()
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
    public async Task Single_file_timeout_warns_without_breaking_scan()
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
    public async Task Missing_tags_are_a_warning_not_damage()
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
    public async Task Tags_are_not_checked_when_setting_is_off()
    {
        using TempDirectory temp = new();
        string path = temp.WriteBytes("notags.flac", 1, 2, 3);

        FileChecker checker = Create(metadata: new FakeMetadataReader(null, "нет тегов"));

        FileCheckResult result = await checker.CheckAsync(Item(path, 3), Context(), CancellationToken.None);

        Assert.Equal(CheckStatus.Ok, result.Status);
    }

    [Fact]
    public async Task Extension_mismatch_warns()
    {
        using TempDirectory temp = new();
        string path = temp.WriteBytes("track.mp3", 1, 2, 3);

        // The decoder says FLAC while the extension is .mp3.
        FakeAudioProbe probe = new(_ => AudioProbeResult.Success(2, "FLAC"));

        FileCheckResult result = await Create(probe).CheckAsync(Item(path, 3), Context(), CancellationToken.None);

        Assert.Equal(CheckStatus.Warning, result.Status);
        Assert.Equal(IssueCode.ExtensionMismatch, result.Issues[0].Code);
        Assert.Equal("FLAC?", result.Format);
    }

    [Fact]
    public async Task File_below_threshold_raises_no_size_warning()
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
    public async Task No_size_limit_means_no_large_file_warning()
    {
        using TempDirectory temp = new();
        string path = temp.WriteBytes("huge.wav", new byte[4096]);

        // 0 means no limit; the threshold never fires.
        AppSettings settings = new() { LargeFileThresholdMb = 0, VerifyExtensionMatchesContent = false };

        bool notified = false;
        FileCheckContext context = new(settings, null, (_, _) => notified = true);

        FakeAudioProbe probe = new(_ => AudioProbeResult.Success(2, "WAV"));
        FileCheckResult result = await Create(probe).CheckAsync(Item(path, 4096), context, CancellationToken.None);

        Assert.False(notified);
        Assert.Equal(CheckStatus.Ok, result.Status);
    }

    [Fact]
    public async Task File_above_threshold_warns_and_is_checked()
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
        // The check still ran: the decoder was called.
        Assert.Equal(1, probe.Calls);
    }

    [Fact]
    public async Task Critical_decoder_failure_throws_dedicated_exception()
    {
        using TempDirectory temp = new();
        string path = temp.WriteBytes("any.flac", 1, 2, 3);

        FakeAudioProbe probe = new(_ => new AudioProbeResult(
            AudioProbeOutcome.EngineFailure, "Декодер отказал.", "BASS → Memory"));

        await Assert.ThrowsAsync<AudioEngineFailureException>(
            () => Create(probe).CheckAsync(Item(path, 3), Context(), CancellationToken.None));
    }

    [Fact]
    public async Task Unexpected_exception_becomes_result_row_not_crash()
    {
        using TempDirectory temp = new();
        string path = temp.WriteBytes("any.flac", 1, 2, 3);

        FakeAudioProbe probe = new(_ => throw new InvalidOperationException("что-то сломалось"));

        FileCheckResult result = await Create(probe).CheckAsync(Item(path, 3), Context(), CancellationToken.None);

        Assert.Equal(CheckStatus.Corrupted, result.Status);
        Assert.Equal(IssueCode.UnexpectedError, result.Issues[0].Code);
    }

    [Fact]
    public async Task Locked_file_is_skipped_when_configured()
    {
        using TempDirectory temp = new();
        string path = temp.WriteBytes("locked.wav", 1, 2, 3);

        // Hold the file exclusively, as another program would.
        using FileStream hold = new(path, FileMode.Open, FileAccess.Read, FileShare.None);

        AppSettings settings = new() { LockedFileAction = LockedFileAction.Skip };
        FileCheckResult result = await Create().CheckAsync(Item(path, 3), Context(settings), CancellationToken.None);

        Assert.Equal(CheckStatus.Skipped, result.Status);
        Assert.Equal(IssueCode.LockedSkippedByUser, result.Issues[0].Code);
    }

    [Fact]
    public async Task Uncopyable_locked_file_is_reported_honestly()
    {
        using TempDirectory temp = new();
        using TempDirectory copies = new();
        string path = temp.WriteBytes("locked.wav", 1, 2, 3, 4, 5);

        // The owner holds the file with no sharing at all: it cannot be read directly
        // or copied. The app must say so plainly rather than pretend it checked.
        using FileStream hold = new(path, FileMode.Open, FileAccess.Read, FileShare.None);

        TempCopyManager manager = new(copies.Path);
        AppSettings settings = new() { LockedFileAction = LockedFileAction.TempCopy, VerifyExtensionMatchesContent = false };

        FakeAudioProbe probe = new(_ => AudioProbeResult.Success(2, "WAV"));
        FileCheckResult result = await Create(probe, tempCopies: manager)
            .CheckAsync(Item(path, 5), Context(settings), CancellationToken.None);

        Assert.Contains(result.Issues, i => i.Code == IssueCode.LockedCopyFailed);
        Assert.Equal(0, probe.Calls);

        // Crucially, no partial copy is left on disk.
        Assert.Empty(Directory.GetFiles(copies.Path));
    }

    [Fact]
    public async Task File_open_for_reading_elsewhere_is_checked_normally()
    {
        using TempDirectory temp = new();
        string path = temp.WriteBytes("playing.wav", 1, 2, 3, 4, 5);

        // The usual case: a player holds the file but allows reading. Treating it as
        // locked and asking the user would be wrong.
        using FileStream hold = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        AppSettings settings = new() { VerifyExtensionMatchesContent = false };
        FakeAudioProbe probe = new(_ => AudioProbeResult.Success(2, "WAV"));

        FileCheckResult result = await Create(probe)
            .CheckAsync(Item(path, 5), Context(settings), CancellationToken.None);

        Assert.Equal(CheckStatus.Ok, result.Status);
        Assert.Equal(1, probe.Calls);
    }
}
