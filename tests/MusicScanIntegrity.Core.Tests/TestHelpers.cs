using MusicScanIntegrity.Core.Audio;
using MusicScanIntegrity.Core.History;
using MusicScanIntegrity.Core.Locking;
using MusicScanIntegrity.Core.Models;

namespace MusicScanIntegrity.Core.Tests;

/// <summary>Temporary folder that cleans up after itself.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "msi-tests-" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    /// <summary>Creates a text file and returns its full path.</summary>
    public string WriteText(string relativePath, string content, System.Text.Encoding? encoding = null)
    {
        string full = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content, encoding ?? new System.Text.UTF8Encoding(false));
        return full;
    }

    /// <summary>Creates a zero-filled file of the given size.</summary>
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
            // Something holds the folder; it does not affect the test result.
        }
    }
}

/// <summary>Fake decoder that answers by a rule set in the test.</summary>
internal sealed class FakeAudioProbe(Func<string, AudioProbeResult>? behaviour = null) : IAudioProbe
{
    private int _calls;

    public bool IsAvailable { get; set; } = true;

    /// <summary>How many times Probe was called; handy for counting parallelism.</summary>
    public int Calls => Volatile.Read(ref _calls);

    /// <summary>Delay before answering, to simulate a slow file.</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    public string? Initialize() => null;

    /// <summary>Depth requested by the most recent call.</summary>
    public DecodeScope LastScope { get; private set; } = DecodeScope.Quick;

    public AudioProbeResult Probe(string filePath, DecodeScope scope, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        LastScope = scope;

        if (Delay > TimeSpan.Zero)
        {
            // Honour cancellation so tests can exercise the timeout.
            cancellationToken.WaitHandle.WaitOne(Delay);
            cancellationToken.ThrowIfCancellationRequested();
        }

        return behaviour?.Invoke(filePath) ?? AudioProbeResult.Success(2, "FLAC");
    }
}

/// <summary>Fake tag reader.</summary>
internal sealed class FakeMetadataReader(TrackMetadata? metadata = null, string? error = null) : IMetadataReader
{
    public TrackMetadata? Read(string filePath, out string? readError)
    {
        readError = error;
        return metadata;
    }
}

/// <summary>Lock detector that always honestly answers "unknown".</summary>
internal sealed class UnknownOwnerDetector : ILockOwnerDetector
{
    public LockOwnerResult Detect(string filePath) => LockOwnerResult.Unknown("тест");
}

/// <summary>A history that does not exist.</summary>
/// <remarks>
/// The file checker takes a history dependency most tests do not care about.
/// This used to be a real ScanHistory, never opened and never disposed; the
/// stub makes it explicit that history plays no part.
/// </remarks>
internal sealed class NoHistory : IScanHistory
{
    /// <summary>Shared instance: the stub has no state and nothing to dispose.</summary>
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
