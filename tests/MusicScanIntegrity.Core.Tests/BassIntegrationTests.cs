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
/// Проверки с настоящей библиотекой BASS: остальные тесты работают с подставным
/// декодером, а здесь проверяется, что реальное декодирование действительно
/// отличает исправный файл от битого.
/// </summary>
/// <remarks>
/// Если библиотеки BASS не скачаны (tools\fetch-bass.ps1), тесты пропускаются:
/// падать из-за отсутствующей внешней зависимости они не должны.
/// </remarks>
public sealed class BassIntegrationTests : IDisposable
{
    /// <summary>
    /// Тест, который выполняется только при наличии native-библиотек BASS.
    /// Без них он помечается пропущенным, а не падает: внешняя зависимость
    /// скачивается отдельно (tools/fetch-bass.ps1).
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

    /// <summary>Пишет корректный WAV — такой файл декодер обязан прочитать.</summary>
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
    public void Библиотека_bass_загружается()
    {
        Assert.Null(_unavailable);

        Assert.True(_probe.IsAvailable);
        Assert.NotEmpty(_probe.LoadedPlugins);
    }

    [BassFact]
    public void Корректный_wav_декодируется()
    {
        using TempDirectory temp = new();
        string path = WriteWav(temp, "ok.wav", 1.5);

        AudioProbeResult result = _probe.Probe(path, DecodeScope.Quick, CancellationToken.None);

        Assert.Equal(AudioProbeOutcome.Ok, result.Outcome);
        Assert.True(result.DecodedSeconds > 0.5, $"прочитано всего {result.DecodedSeconds:0.00} с");
        Assert.Equal("WAV", result.DetectedFormat);
    }

    [BassFact]
    public void Выборочная_проверка_читает_несколько_окон()
    {
        using TempDirectory temp = new();

        // Файл длиннее, чем пять окон по полторы секунды: иначе выборочная
        // проверка честно перейдёт в полную.
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
    public void Обрезанный_WAV_ловится_разбором_структуры()
    {
        // Декодер здесь бесполезен: длину WAV он берёт из размера файла, а не
        // из заголовка, поэтому нехватки данных не видит. Зато её видно по
        // объявленной длине части data — этим и занимается разбор структуры.
        using TempDirectory temp = new();
        string path = WriteWav(temp, "обрезанный.wav", seconds: 30);

        byte[] full = File.ReadAllBytes(path);
        File.WriteAllBytes(path, full[..(full.Length / 2)]);

        ContainerValidation result = new ContainerIntegrityChecker().Check(path, CancellationToken.None);

        Assert.Equal(ContainerVerdict.Damaged, result.Verdict);
        Assert.True(result.Truncated);
    }

    [BassFact]
    public void Мусор_вместо_аудио_не_декодируется()
    {
        using TempDirectory temp = new();
        string path = temp.WriteBytes("broken.mp3", new byte[4096]);

        AudioProbeResult result = _probe.Probe(path, DecodeScope.Quick, CancellationToken.None);

        Assert.NotEqual(AudioProbeOutcome.Ok, result.Outcome);
        Assert.NotNull(result.Message);
    }

    [BassFact]
    public void Пустой_файл_не_декодируется()
    {
        using TempDirectory temp = new();
        string path = temp.WriteBytes("empty.ogg");

        AudioProbeResult result = _probe.Probe(path, DecodeScope.Quick, CancellationToken.None);

        Assert.NotEqual(AudioProbeOutcome.Ok, result.Outcome);
    }

    [BassFact]
    public async Task Полный_проход_по_папке_разделяет_исправные_и_битые_файлы()
    {
        using TempDirectory temp = new();

        WriteWav(temp, Path.Combine("Альбом", "01 целый.wav"), 1.2);
        WriteWav(temp, Path.Combine("Альбом", "02 целый.wav"), 0.8);
        temp.WriteBytes(Path.Combine("Альбом", "03 битый.mp3"), new byte[4096]);
        temp.WriteBytes(Path.Combine("Альбом", "04 пустой.ogg"));
        temp.WriteText(Path.Combine("Альбом", "список.m3u"), "01 целый.wav\nнет-такого.wav\n");

        AppSettings settings = new()
        {
            // Сверку расширения выключаем: битый MP3 из нулей и так не откроется,
            // а тест здесь про разделение исправных и битых.
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

        // Плейлист нашёл один отсутствующий путь.
        Assert.Equal(1, summary.PlaylistMissingLinks);
        Assert.Equal(1, summary.PlaylistCount);
    }
}
