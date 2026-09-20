using System.Buffers;
using System.Runtime.InteropServices;
using ManagedBass;
using MusicScanIntegrity.Core.Analysis;
using MusicScanIntegrity.Core.Resources;

namespace MusicScanIntegrity.Core.Audio;

/// <summary>
/// Checks files through the BASS library.
/// </summary>
/// <remarks>
/// <para>
/// Nothing is played back: streams are created with
/// <see cref="BassFlags.Decode"/> against the "no sound" device, so no audio is
/// heard and the sound card stays free.
/// </para>
/// <para>
/// The native libraries (bass.dll and the format plug-ins) live in the
/// <c>bass\</c> folder next to the executable and are loaded from there
/// explicitly. They are not committed — <c>tools\fetch-bass.ps1</c> downloads
/// them under the un4seen licence.
/// </para>
/// </remarks>
public sealed class BassAudioProbe : IAudioProbe, IDisposable
{
    /// <summary>Seconds of audio read by the quick scan.</summary>
    private const double SecondsToDecode = 2.0;

    /// <summary>Read buffer size; reading in chunks keeps cancellation responsive.</summary>
    private const int ReadChunkBytes = 64 * 1024;

    /// <summary>Length of one window in a sampled scan.</summary>
    private const double SampleWindowSeconds = 1.5;

    /// <summary>How many windows a sampled scan reads.</summary>
    private const int SampleWindows = 5;

    /// <summary>
    /// How far short of the declared length the audio may fall before it
    /// counts as truncated. Headers round the duration, and quibbling over
    /// tenths of a second would condemn healthy files.
    /// </summary>
    private const double TruncationToleranceSeconds = 1.0;

    /// <summary>The "no sound" device: decode without any output.</summary>
    private const int NoSoundDevice = 0;

    /// <summary>Tracker formats open through MusicLoad rather than CreateStream.</summary>
    private static readonly HashSet<string> TrackerExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mod", ".xm", ".it", ".s3m", ".mtm", ".umx",
    };

    /// <summary>How long shutdown waits for reads already in flight.</summary>
    private static readonly TimeSpan ShutdownWait = TimeSpan.FromSeconds(3);

    private readonly List<int> _plugins = [];
    private readonly Lock _initLock = new();

    private bool _initialized;
    private bool _disposed;

    /// <summary>How many reads are in flight right now.</summary>
    /// <remarks>
    /// Needed at shutdown: <c>Bass.Free</c> tears the library down whole, and
    /// if a worker thread is inside the decoder at that moment the process
    /// dies outright rather than throwing. The counter lets shutdown wait.
    /// </remarks>
    private int _activeProbes;

    /// <inheritdoc />
    public bool IsAvailable => Volatile.Read(ref _initialized);

    /// <summary>Folder the native libraries were loaded from.</summary>
    public string NativeFolder { get; } = Path.Combine(AppContext.BaseDirectory, "bass");

    /// <summary>Names of the plug-ins that loaded, shown in the about window.</summary>
    public IReadOnlyList<string> LoadedPlugins { get; private set; } = [];

    /// <inheritdoc />
    public string? Initialize()
    {
        lock (_initLock)
        {
            if (_initialized)
            {
                return null;
            }

            if (!Directory.Exists(NativeFolder))
            {
                return Common.Format.Text(Strings.Bass_FolderMissing, NativeFolder);
            }

            // Add bass\ to the DLL search path explicitly: LoadLibrary only
            // looks next to the exe and in the system folders. Failing here is
            // not fatal — the ordinary search may still find it — but if
            // initialisation then fails, this is the cause to report.
            string? searchPathNote = SetDllDirectory(NativeFolder)
                ? null
                : Common.Format.Text(Strings.Bass_SearchPathFailed, NativeFolder, Marshal.GetLastWin32Error());

            try
            {
                // Nothing is played back, so the background buffer-update
                // threads are pointless; skipping them saves measurable CPU on
                // large collections.
                Bass.Configure(Configuration.UpdateThreads, 0);
                Bass.Configure(Configuration.UpdatePeriod, 0);
                Bass.Configure(Configuration.MusicVirtual, 0);

                if (!Bass.Init(NoSoundDevice, 44100, DeviceInitFlags.Default, IntPtr.Zero))
                {
                    Errors error = Bass.LastError;
                    if (error != Errors.Already)
                    {
                        return Common.Format.Text(Strings.Bass_InitFailed, Describe(error), error, searchPathNote);
                    }
                }

                LoadPlugins();
                _initialized = true;

                return null;
            }
            catch (DllNotFoundException ex)
            {
                return Common.Format.Text(Strings.Bass_DllMissing, NativeFolder, ex.Message, searchPathNote);
            }
            catch (BadImageFormatException ex)
            {
                return Common.Format.Text(Strings.Bass_WrongBitness, ex.Message);
            }
            catch (Exception ex)
            {
                return Common.Format.Text(Strings.Bass_StartFailed, ex.Message);
            }
        }
    }

    /// <inheritdoc />
    public AudioProbeResult Probe(string filePath, DecodeScope scope, CancellationToken cancellationToken)
    {
        if (!Volatile.Read(ref _initialized) || Volatile.Read(ref _disposed))
        {
            return new AudioProbeResult(
                AudioProbeOutcome.EngineFailure,
                Strings.Bass_NotStarted,
                Strings.Bass_NotInitialised);
        }

        int handle = 0;
        bool isTracker = TrackerExtensions.Contains(Path.GetExtension(filePath));

        // Registered before touching the library: shutdown waits for this
        // counter to reach zero before freeing BASS. The disposed flag is read
        // again after registering, or a shutdown that started in between would
        // free the library under this read.
        Interlocked.Increment(ref _activeProbes);

        if (Volatile.Read(ref _disposed))
        {
            Interlocked.Decrement(ref _activeProbes);
            return new AudioProbeResult(
                AudioProbeOutcome.EngineFailure,
                Strings.Bass_NotStarted,
                Strings.Bass_NotInitialised);
        }

        try
        {
            handle = isTracker
                ? Bass.MusicLoad(filePath, 0, 0, BassFlags.Decode | BassFlags.MusicNoSample | BassFlags.Float, 0)
                : Bass.CreateStream(filePath, 0, 0, BassFlags.Decode | BassFlags.Float);

            if (handle == 0)
            {
                return FromOpenError(Bass.LastError);
            }

            string? format = DetectFormat(handle);

            // Length declared by the header. BASS returns −1 for streams
            // without one, leaving nothing to compare against.
            long totalBytes = Bass.ChannelGetLength(handle);
            double declared = totalBytes > 0 ? Bass.ChannelBytes2Seconds(handle, totalBytes) : 0;

            // The samples pass through the read buffer anyway, so level,
            // clipping and dropouts come for free.
            ChannelInfo info = Bass.ChannelGetInfo(handle);
            AudioStatsAccumulator stats = new(Math.Max(1, info.Frequency * Math.Max(1, info.Channels)));

            // The spectrum is built from the same buffers; it shows how high
            // the audio reaches and exposes "lossless" built from a lossy
            // source.
            SpectrumAnalyzer spectrum = new(info.Frequency, Math.Max(1, info.Channels));
            SampleSink sink = new(stats, spectrum);

            AudioProbeResult result = scope switch
            {
                DecodeScope.Full => ReadWholeFile(handle, format, declared, sink, cancellationToken),
                DecodeScope.Sampled => ReadSamples(handle, format, totalBytes, declared, sink, cancellationToken),
                _ => ReadBeginning(handle, format, declared, sink, cancellationToken),
            };

            return result with
            {
                Stats = stats.Build(),
                Spectrum = spectrum.Build(),
                BitrateKbps = ReadBitrate(handle),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // One bad file must not bring the whole scan down.
            return new AudioProbeResult(
                AudioProbeOutcome.ReadFailed,
                Strings.Bass_UnexpectedRead,
                $"{ex.GetType().Name} · {ex.Message}");
        }
        finally
        {
            if (handle != 0)
            {
                if (isTracker)
                {
                    Bass.MusicFree(handle);
                }
                else
                {
                    Bass.StreamFree(handle);
                }
            }

            Interlocked.Decrement(ref _activeProbes);
        }
    }

    /// <summary>Quick scan: the first seconds only.</summary>
    private static AudioProbeResult ReadBeginning(
        int handle,
        string? format,
        double declared,
        SampleSink sink,
        CancellationToken cancellationToken)
    {
        long target = Bass.ChannelSeconds2Bytes(handle, SecondsToDecode);
        if (target <= 0)
        {
            // The decoder could not convert seconds to bytes; read a fixed chunk.
            target = ReadChunkBytes * 4;
        }

        ReadOutcome read = ReadRun(handle, target, sink, cancellationToken);

        if (read.Failure is { } failure)
        {
            return Failed(handle, failure, read.Bytes, format, declared);
        }

        return read.Bytes == 0
            ? Empty(format, declared)
            : AudioProbeResult.Success(Bass.ChannelBytes2Seconds(handle, read.Bytes), format, declared);
    }

    /// <summary>Full scan: the file is read to the end.</summary>
    private static AudioProbeResult ReadWholeFile(
        int handle,
        string? format,
        double declared,
        SampleSink sink,
        CancellationToken cancellationToken)
    {
        ReadOutcome read = ReadRun(handle, long.MaxValue, sink, cancellationToken);

        if (read.Failure is { } failure)
        {
            return Failed(handle, failure, read.Bytes, format, declared);
        }

        if (read.Bytes == 0)
        {
            return Empty(format, declared);
        }

        double decoded = Bass.ChannelBytes2Seconds(handle, read.Bytes);

        return AudioProbeResult.Success(decoded, format, declared) with
        {
            Truncated = IsShort(decoded, declared),
        };
    }

    /// <summary>
    /// Sampled scan: start, end and several places in between.
    /// </summary>
    /// <remarks>
    /// The trade between quick and full: damage in the middle of a track is
    /// caught while only seconds are read. The last window sits at the very
    /// end, which is where partial downloads break off.
    /// </remarks>
    private static AudioProbeResult ReadSamples(
        int handle,
        string? format,
        long totalBytes,
        double declared,
        SampleSink sink,
        CancellationToken cancellationToken)
    {
        long windowBytes = Bass.ChannelSeconds2Bytes(handle, SampleWindowSeconds);
        if (windowBytes <= 0)
        {
            windowBytes = ReadChunkBytes * 2;
        }

        // Shorter than a few windows: reading it whole is both faster and more accurate.
        if (totalBytes <= 0 || totalBytes <= windowBytes * SampleWindows)
        {
            return ReadWholeFile(handle, format, declared, sink, cancellationToken);
        }

        long decodedBytes = 0;
        bool seekFailed = false;

        for (int i = 0; i < SampleWindows; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The last window is pinned to the end; the rest are spread evenly.
            long position = i == SampleWindows - 1
                ? Math.Max(0, totalBytes - windowBytes)
                : totalBytes / SampleWindows * i;

            if (i > 0 && !Bass.ChannelSetPosition(handle, position, PositionFlags.Bytes))
            {
                Errors error = Bass.LastError;

                if (i == SampleWindows - 1)
                {
                    // Could not reach the end: shorter than the header promises.
                    return AudioProbeResult
                        .Success(Bass.ChannelBytes2Seconds(handle, decodedBytes), format, declared) with
                    {
                        Truncated = true,
                        TechnicalDetail = $"BASS_ChannelSetPosition({position}) → {error}",
                    };
                }

                seekFailed = true;
                break;
            }

            ReadOutcome read = ReadRun(handle, windowBytes, sink, cancellationToken);
            decodedBytes += read.Bytes;

            if (read.Failure is { } failure)
            {
                return Failed(handle, failure, decodedBytes, format, declared);
            }
        }

        if (decodedBytes == 0)
        {
            return Empty(format, declared);
        }

        double decoded = Bass.ChannelBytes2Seconds(handle, decodedBytes);

        return AudioProbeResult.Success(decoded, format, declared) with
        {
            TechnicalDetail = seekFailed
                ? Strings.Bass_SeekUnavailable
                : null,
        };
    }

    /// <summary>Bitrate per the decoder, in kilobits per second.</summary>
    private static int ReadBitrate(int handle) =>
        Bass.ChannelGetAttribute(handle, ChannelAttribute.Bitrate, out float bitrate)
            ? (int)Math.Round(bitrate)
            : 0;

    /// <summary>Reads the stream in chunks until enough is gathered or the audio ends.</summary>
    private static ReadOutcome ReadRun(
        int handle,
        long targetBytes,
        SampleSink sink,
        CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(ReadChunkBytes);
        try
        {
            return ReadRun(handle, targetBytes, sink, buffer, cancellationToken);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static ReadOutcome ReadRun(
        int handle,
        long targetBytes,
        SampleSink sink,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        long total = 0;

        while (total < targetBytes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int wanted = (int)Math.Min(ReadChunkBytes, targetBytes - total);
            int read = Bass.ChannelGetData(handle, buffer, wanted);

            if (read < 0)
            {
                Errors error = Bass.LastError;

                // End of file is not an error: short tracks finish early.
                return error == Errors.Ended
                    ? new ReadOutcome(total, null)
                    : new ReadOutcome(total, error);
            }

            if (read == 0)
            {
                break;
            }

            // The stream is opened with the Float flag, so the buffer bytes
            // are already samples in the −1…1 range.
            sink.Add(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(buffer.AsSpan(0, read)));

            total += read;
        }

        return new ReadOutcome(total, null);
    }

    /// <summary>Duration falls noticeably short of the declared one.</summary>
    private static bool IsShort(double decoded, double declared) =>
        declared > 1 && decoded < declared - TruncationToleranceSeconds;

    private static AudioProbeResult Failed(
        int handle,
        Errors error,
        long decodedBytes,
        string? format,
        double declared) =>
        new(
            AudioProbeOutcome.ReadFailed,
            Common.Format.Text(Strings.Bass_ReadInterrupted, Describe(error)),
            $"BASS_ChannelGetData → {error}",
            Bass.ChannelBytes2Seconds(handle, decodedBytes),
            format,
            declared);

    private static AudioProbeResult Empty(string? format, double declared) =>
        new(
            AudioProbeOutcome.Empty,
            Strings.Bass_NoSamples,
            Strings.Bass_NoSamples_Detail,
            0,
            format,
            declared);

    /// <summary>How much was read and which error ended it.</summary>
    private readonly record struct ReadOutcome(long Bytes, Errors? Failure);

    /// <summary>
    /// Hands the samples that were read to everything that measures them.
    /// </summary>
    /// <remarks>
    /// Keeps the read paths from carrying two separate accumulators: one
    /// buffer deserves one pass.
    /// </remarks>
    private sealed class SampleSink(AudioStatsAccumulator stats, SpectrumAnalyzer spectrum)
    {
        public void Add(ReadOnlySpan<float> samples)
        {
            stats.Add(samples);
            spectrum.Add(samples);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_initLock)
        {
            if (_disposed)
            {
                return;
            }

            Volatile.Write(ref _disposed, true);

            if (!_initialized)
            {
                return;
            }

            // Wait for reads in flight; no new ones start because Probe checks
            // _disposed. If a thread is stuck for good, free anyway — the app is
            // closing, and hanging is worse.
            SpinWait spin = default;
            long deadline = Environment.TickCount64 + (long)ShutdownWait.TotalMilliseconds;
            while (Volatile.Read(ref _activeProbes) > 0 && Environment.TickCount64 < deadline)
            {
                spin.SpinOnce();
            }

            foreach (int plugin in _plugins)
            {
                Bass.PluginFree(plugin);
            }

            _plugins.Clear();
            Bass.Free();
            Volatile.Write(ref _initialized, false);
        }
    }

    /// <summary>Loads every format plug-in found in the <c>bass\</c> folder.</summary>
    private void LoadPlugins()
    {
        List<string> loaded = [];

        foreach (string dll in Directory.EnumerateFiles(NativeFolder, "bass*.dll"))
        {
            string name = Path.GetFileNameWithoutExtension(dll);

            // bass.dll itself is not a plug-in.
            if (name.Equals("bass", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int plugin = Bass.PluginLoad(dll);
            if (plugin != 0)
            {
                _plugins.Add(plugin);
                loaded.Add(name);
            }
            else
            {
                // Not every bass*.dll is a format plug-in (basswasapi, say);
                // a failure here is normal, not an error.
            }
        }

        LoadedPlugins = loaded;
    }

    /// <summary>Format the decoder itself identified, for the extension check.</summary>
    private static string? DetectFormat(int handle)
    {
        if (!Bass.ChannelGetInfo(handle, out ChannelInfo info))
        {
            return null;
        }

        return info.ChannelType switch
        {
            ChannelType.MP1 => "MP1",
            ChannelType.MP2 => "MP2",
            ChannelType.MP3 => "MP3",
            ChannelType.OGG => "OGG",
            ChannelType.AIFF => "AIFF",
            ChannelType.WMA or ChannelType.WMA_MP3 => "WMA",
            ChannelType.WV or ChannelType.WV_H or ChannelType.WV_L or ChannelType.WV_LH => "WV",
            ChannelType.APE => "APE",
            ChannelType.FLAC or ChannelType.FLAC_OGG => "FLAC",
            ChannelType.MPC => "MPC",
            ChannelType.AAC => "AAC",
            ChannelType.MP4 => "MP4",
            ChannelType.ALAC => "ALAC",
            ChannelType.TTA => "TTA",
            ChannelType.OPUS => "OPUS",
            ChannelType.DSD => "DSD",
            ChannelType.MIDI => "MIDI",
            ChannelType.MOD => "MOD",
            ChannelType.MTM => "MTM",
            ChannelType.S3M => "S3M",
            ChannelType.XM => "XM",
            ChannelType.IT => "IT",
            ChannelType.Wave or ChannelType.WavePCM or ChannelType.WaveFloat => "WAV",
            ChannelType.CA => "ALAC",
            _ => null,
        };
    }

    /// <summary>Turns a stream-open error into a meaningful outcome.</summary>
    private static AudioProbeResult FromOpenError(Errors error) => error switch
    {
        Errors.WmaLicense or Errors.WmaAccesDenied or Errors.WmaIndividual => new AudioProbeResult(
            AudioProbeOutcome.PasswordProtected,
            Strings.Bass_Protected,
            $"BASS → {error}"),

        Errors.Empty => new AudioProbeResult(
            AudioProbeOutcome.Empty,
            Strings.Bass_Empty,
            $"BASS → {error}"),

        Errors.FileOpen => new AudioProbeResult(
            AudioProbeOutcome.OpenFailed,
            Strings.Bass_CannotOpen,
            $"BASS → {error}"),

        Errors.Memory or Errors.Init or Errors.NotAvailable => new AudioProbeResult(
            AudioProbeOutcome.EngineFailure,
            Common.Format.Text(Strings.Bass_EngineFailed, Describe(error)),
            $"BASS → {error}"),

        _ => new AudioProbeResult(
            AudioProbeOutcome.OpenFailed,
            Common.Format.Text(Strings.Bass_DecodeStartFailed, Describe(error)),
            $"BASS → {error}"),
    };

    /// <summary>Human wording for a BASS error code; no "Error 0x…" in the main text.</summary>
    internal static string Describe(Errors error) => error switch
    {
        Errors.OK => Strings.Bass_Error_OK,
        Errors.FileOpen => Strings.Bass_Error_FileOpen,
        Errors.FileFormat => Strings.Bass_Error_FileFormat,
        Errors.Codec => Strings.Bass_Error_Codec,
        Errors.Empty => Strings.Bass_Error_Empty,
        Errors.Memory => Strings.Bass_Error_Memory,
        Errors.Init => Strings.Bass_Error_Init,
        Errors.NotAvailable => Strings.Bass_Error_NotAvailable,
        Errors.Unstreamable => Strings.Bass_Error_Unstreamable,
        Errors.WmaLicense => Strings.Bass_Error_WmaLicense,
        Errors.WmaAccesDenied => Strings.Bass_Error_WmaAccessDenied,
        Errors.WmaIndividual => Strings.Bass_Error_WmaIndividual,
        Errors.Timeout => Strings.Bass_Error_Timeout,
        Errors.Ended => Strings.Bass_Error_Ended,
        Errors.Handle => Strings.Bass_Error_Handle,
        Errors.Position => Strings.Bass_Error_Position,
        Errors.SampleFormat => Strings.Bass_Error_SampleFormat,
        Errors.Mp4NoStream => Strings.Bass_Error_Mp4NoStream,
        Errors.Unknown => Strings.Bass_Error_Unknown,
        _ => Common.Format.Text(Strings.Bass_Error_Code, error),
    };

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDllDirectory(string lpPathName);
}
