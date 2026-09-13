using MusicScanIntegrity.Core.Audio;
using MusicScanIntegrity.Core.History;
using MusicScanIntegrity.Core.Locking;
using MusicScanIntegrity.Core.Models;

namespace MusicScanIntegrity.Core.Tests;

/// <summary>Временная папка, которая сама за собой убирает.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "msi-tests-" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    /// <summary>Создаёт файл с текстовым содержимым и возвращает полный путь.</summary>
    public string WriteText(string relativePath, string content, System.Text.Encoding? encoding = null)
    {
        string full = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content, encoding ?? new System.Text.UTF8Encoding(false));
        return full;
    }

    /// <summary>Создаёт файл заданного размера, набитый нулями.</summary>
    public string WriteBytes(string relativePath, params byte[] content)
    {
        string full = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, content);
        return full;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Папку кто-то держит — на результат теста это не влияет.
        }
    }
}

/// <summary>Подставной декодер: отвечает по правилу, заданному тестом.</summary>
internal sealed class FakeAudioProbe(Func<string, AudioProbeResult>? behaviour = null) : IAudioProbe
{
    private int _calls;

    public bool IsAvailable { get; set; } = true;

    /// <summary>Сколько раз вызвали проверку — удобно считать параллельность.</summary>
    public int Calls => Volatile.Read(ref _calls);

    /// <summary>Задержка перед ответом — имитирует долгий файл.</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    public string? Initialize() => null;

    /// <summary>С какой глубиной звали проверку в последний раз.</summary>
    public DecodeScope LastScope { get; private set; } = DecodeScope.Quick;

    public AudioProbeResult Probe(string filePath, DecodeScope scope, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        LastScope = scope;

        if (Delay > TimeSpan.Zero)
        {
            // Ждём с учётом отмены: так тест может проверить срабатывание таймаута.
            cancellationToken.WaitHandle.WaitOne(Delay);
            cancellationToken.ThrowIfCancellationRequested();
        }

        return behaviour?.Invoke(filePath) ?? AudioProbeResult.Success(2, "FLAC");
    }
}

/// <summary>Подставной читатель тегов.</summary>
internal sealed class FakeMetadataReader(TrackMetadata? metadata = null, string? error = null) : IMetadataReader
{
    public TrackMetadata? Read(string filePath, out string? readError)
    {
        readError = error;
        return metadata;
    }
}

/// <summary>Определитель владельца, который всегда честно говорит «не знаю».</summary>
internal sealed class UnknownOwnerDetector : ILockOwnerDetector
{
    public LockOwnerResult Detect(string filePath) => LockOwnerResult.Unknown("тест");
}

/// <summary>История, которой нет.</summary>
/// <remarks>
/// Проверке файла история нужна как зависимость, но большинству тестов она
/// не интересна. Раньше в таких местах создавался настоящий ScanHistory —
/// незакрытый и никогда не открытый. Заглушка честнее: видно, что история
/// в этом тесте не участвует.
/// </remarks>
internal sealed class NoHistory : IScanHistory
{
    /// <summary>Общий экземпляр: состояния у заглушки нет, закрывать нечего.</summary>
    public static readonly NoHistory Instance = new();

    private NoHistory()
    {
    }

    public bool IsOpen => false;

    public string DatabasePath => string.Empty;

    public string? Open(string databasePath) => "история в тесте не используется";

    public FileHistoryEntry? Find(string path) => null;

    public void Save(FileHistoryEntry entry)
    {
    }

    public void Clear()
    {
    }

    public void Close()
    {
    }

    public int Count() => 0;

    public void Dispose()
    {
    }
}
