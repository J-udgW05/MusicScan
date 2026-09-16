using System.Text;
using MusicScanIntegrity.Core.Audio;
using MusicScanIntegrity.Core.Discovery;
using MusicScanIntegrity.Core.History;
using MusicScanIntegrity.Core.Integrity;
using MusicScanIntegrity.Core.Locking;
using MusicScanIntegrity.Core.Models;
using MusicScanIntegrity.Core.Playlists;
using MusicScanIntegrity.Core.Scanning;
using MusicScanIntegrity.Core.Settings;
using Xunit;

namespace MusicScanIntegrity.Core.Tests;

/// <summary>
/// Tests against the real BASS library; the rest use a fake decoder. These check
/// that real decoding actually tells a healthy file from a broken one.
/// </summary>
/// <remarks>
/// Skipped when the BASS binaries are not downloaded (tools/fetch-bass.ps1): a
/// missing external dependency is no reason to fail.
/// </remarks>
public sealed class BassIntegrationTests : IDisposable
{
    /// <summary>
    /// A fact that runs only when the native BASS libraries are present, and is
    /// reported as skipped otherwise.
    /// </summary>
    private sealed class BassFactAttribute : FactAttribute
    {
        public BassFactAttribute()
        {
            string folder = Path.Combine(AppContext.BaseDirectory, "bass");

            if (!File.Exists(Path.Combine(folder, "bass.dll")))
            {
                Skip = "Библиотеки BASS не скачаны — выполните tools/fetch-bass.ps1";
            }
        }
    }

    private readonly BassAudioProbe _probe = new();
    private readonly string? _unavailable;

    public BassIntegrationTests() => _unavailable = _probe.Initialize();

    public void Dispose() => _probe.Dispose();

    /// <summary>Writes a valid WAV that the decoder must be able to read.</summary>
    private static string WriteWav(TempDirectory temp, string name, double seconds = 1.0, int rate = 8000)
    {
        int samples = (int)(rate * seconds);
        using MemoryStream body = new();
        using (BinaryWriter writer = new(body, Encoding.ASCII, leaveOpen: true))
        {
            for (int i = 0; i < samples; i++)
            {
                writer.Write((short)(i / 40 % 2 == 0 ? -3000 : 3000));
            }
        }

        byte[] data = body.ToArray();

        using MemoryStream file = new();
        using (BinaryWriter writer = new(file, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write("RIFF"u8.ToArray());
            writer.Write(36 + data.Length);
            writer.Write("WAVE"u8.ToArray());
            writer.Write("fmt "u8.ToArray());
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(rate);
            writer.Write(rate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write("data"u8.ToArray());
            writer.Write(data.Length);
            writer.Write(data);
        }

        return temp.WriteBytes(name, file.ToArray());
    }

    [BassFact]
    public void Bass_library_loads()
    {
        Assert.Null(_unavailable);

        Assert.True(_probe.IsAvailable);
        Assert.NotEmpty(_probe.LoadedPlugins);
    }

    [BassFact]
    public void Valid_wav_decodes()
    {
        using TempDirectory temp = new();
        string path = WriteWav(temp, "ok.wav", 1.5);

        AudioProbeResult result = _probe.Probe(path, DecodeScope.Quick, CancellationToken.None);

        Assert.Equal(AudioProbeOutcome.Ok, result.Outcome);
        Assert.True(result.DecodedSeconds > 0.5, $"прочитано всего {result.DecodedSeconds:0.00} с");
        Assert.Equal("WAV", result.DetectedFormat);
    }

    [BassFact]
    public void Sampled_check_reads_several_windows()
    {
        using TempDirectory temp = new();

        // Longer than five 1.5 s windows, or a sampled check falls back to reading the
        // whole file.
        string path = WriteWav(temp, "длинный.wav", seconds: 30);

        AudioProbeResult quick = _probe.Probe(path, DecodeScope.Quick, CancellationToken.None);
        AudioProbeResult sampled = _probe.Probe(path, DecodeScope.Sampled, CancellationToken.None);
        AudioProbeResult full = _probe.Probe(path, DecodeScope.Full, CancellationToken.None);

        Assert.Equal(AudioProbeOutcome.Ok, sampled.Outcome);
        Assert.True(
            sampled.DecodedSeconds > quick.DecodedSeconds,
            $"выборочная прочитала {sampled.DecodedSeconds:0.0} с, быстрая {quick.DecodedSeconds:0.0} с");
        Assert.True(
            sampled.DecodedSeconds < full.DecodedSeconds,
            $"выборочная прочитала {sampled.DecodedSeconds:0.0} с, полная {full.DecodedSeconds:0.0} с");
        Assert.False(sampled.Truncated);
        Assert.False(full.Truncated);
    }

    [Fact]
    public void Truncated_wav_is_caught_by_structure_check()
    {
        // The decoder is no help here: it derives WAV length from the file size, not
        // the header, so it cannot see missing data. The declared length of the data
        // chunk shows it, which is what the structure check reads.
        using TempDirectory temp = new();
        string path = WriteWav(temp, "обрезанный.wav", seconds: 30);

        byte[] full = File.ReadAllBytes(path);
        File.WriteAllBytes(path, full[..(full.Length / 2)]);

        ContainerValidation result = new ContainerIntegrityChecker().Check(path, CancellationToken.None);

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.True(result.Truncated);
    }

    [BassFact]
    public void Garbage_does_not_decode()
    {
        using TempDirectory temp = new();
        string path = temp.WriteBytes("broken.mp3", new byte[4096]);

        AudioProbeResult result = _probe.Probe(path, DecodeScope.Quick, CancellationToken.None);

        Assert.NotEqual(AudioProbeOutcome.Ok, result.Outcome);
        Assert.NotNull(result.Message);
    }

    [BassFact]
    public void Empty_file_does_not_decode()
    {
        using TempDirectory temp = new();
        string path = temp.WriteBytes("empty.ogg");

        AudioProbeResult result = _probe.Probe(path, DecodeScope.Quick, CancellationToken.None);

        Assert.NotEqual(AudioProbeOutcome.Ok, result.Outcome);
    }

    [BassFact]
    public async Task Full_folder_scan_separates_healthy_and_broken_files()
    {
        using TempDirectory temp = new();

        WriteWav(temp, Path.Combine("Альбом", "01 целый.wav"), 1.2);
        WriteWav(temp, Path.Combine("Альбом", "02 целый.wav"), 0.8);
        temp.WriteBytes(Path.Combine("Альбом", "03 битый.mp3"), new byte[4096]);
        temp.WriteBytes(Path.Combine("Альбом", "04 пустой.ogg"));
        temp.WriteText(Path.Combine("Альбом", "список.m3u"), "01 целый.wav\nнет-такого.wav\n");

        AppSettings settings = new()
        {
            // Extension check off: a zero-filled MP3 will not open anyway, and this test
            // is about separating healthy from broken files.
            VerifyExtensionMatchesContent = false,
            LockedFileAction = LockedFileAction.Skip,
        };

        FileDiscoveryService discovery = new();
        DiscoveryResult found = await discovery.DiscoverAsync(temp.Path, settings);

        Assert.Equal(4, found.AudioItems.Count);
        Assert.Single(found.Playlists);

        FileChecker checker = new(
            _probe,
            new TagLibMetadataReader(),
            new RestartManagerLockDetector(),
            new TempCopyManager(),
            new ContainerIntegrityChecker(),
            NoHistory.Instance);

        using ScanEngine engine = new(
            checker,
            new PlaylistService(),
            new AlwaysSkipDecisionProvider());

        List<FileCheckResult> results = [];
        engine.ResultsReady += (_, batch) => { lock (results) { results.AddRange(batch); } };

        ScanSummary? summary = null;
        engine.Finished += (_, s) => summary = s;

        await engine.RunAsync(found, settings);

        Assert.NotNull(summary);
        Assert.Equal(4, results.Count);

        Assert.Equal(2, results.Count(r => r.Status == CheckStatus.Ok));
        Assert.Equal(2, results.Count(r => r.Status == CheckStatus.Corrupted));

        // The playlist found one missing path.
        Assert.Equal(1, summary.PlaylistMissingLinks);
        Assert.Equal(1, summary.PlaylistCount);
    }
}
