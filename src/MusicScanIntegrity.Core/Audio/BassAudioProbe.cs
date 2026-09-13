using System.Runtime.InteropServices;
using ManagedBass;
using MusicScanIntegrity.Core.Analysis;

namespace MusicScanIntegrity.Core.Audio;

/// <summary>
/// Проверка файлов через библиотеку BASS (зафиксированный выбор,
/// 01_SPECIFICATION.md, раздел 10).
/// </summary>
/// <remarks>
/// <para>
/// Файл не проигрывается, а декодируется «вхолостую»: поток создаётся с флагом
/// <see cref="BassFlags.Decode"/>, устройство вывода — «no sound» (номер 0),
/// поэтому звука не слышно и звуковая карта не занимается.
/// </para>
/// <para>
/// Native-библиотеки (bass.dll и плагины форматов) лежат в подпапке <c>bass\</c>
/// рядом с исполняемым файлом и загружаются оттуда явно — в репозиторий они не
/// коммитятся, их скачивает <c>tools\fetch-bass.ps1</c> (см. README).
/// </para>
/// </remarks>
public sealed class BassAudioProbe : IAudioProbe, IDisposable
{
    /// <summary>Сколько секунд аудио читаем: спецификация требует «первые одна-две секунды».</summary>
    private const double SecondsToDecode = 2.0;

    /// <summary>Размер буфера чтения — читаем порциями, чтобы реагировать на отмену.</summary>
    private const int ReadChunkBytes = 64 * 1024;

    /// <summary>Длина одного окна при выборочной проверке.</summary>
    private const double SampleWindowSeconds = 1.5;

    /// <summary>Сколько окон читать при выборочной проверке.</summary>
    private const int SampleWindows = 5;

    /// <summary>
    /// Насколько прочитанное может быть короче заявленного, чтобы это ещё не
    /// считалось обрывом. Заголовки округляют длительность, и придираться к
    /// десятым долям секунды значило бы объявлять повреждёнными исправные файлы.
    /// </summary>
    private const double TruncationToleranceSeconds = 1.0;

    /// <summary>Устройство «no sound»: декодируем без вывода звука.</summary>
    private const int NoSoundDevice = 0;

    /// <summary>Трекерные форматы открываются через MusicLoad, а не CreateStream.</summary>
    private static readonly HashSet<string> TrackerExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mod", ".xm", ".it", ".s3m", ".mtm", ".umx",
    };

    /// <summary>Сколько ждать завершения начатых чтений при закрытии.</summary>
    private static readonly TimeSpan ShutdownWait = TimeSpan.FromSeconds(3);

    private readonly List<int> _plugins = [];
    private readonly Lock _initLock = new();

    private bool _initialized;
    private bool _disposed;

    /// <summary>Сколько чтений идёт прямо сейчас.</summary>
    /// <remarks>
    /// Нужен закрытию: <c>Bass.Free</c> обрывает библиотеку целиком, и если в
    /// этот момент рабочий поток сидит внутри декодера, программа падает не
    /// исключением, а вместе с процессом. Счётчик даёт закрытию дождаться
    /// начатых чтений.
    /// </remarks>
    private int _activeProbes;

    /// <inheritdoc />
    public bool IsAvailable => Volatile.Read(ref _initialized);

    /// <summary>Папка, из которой загружались native-библиотеки.</summary>
    public string NativeFolder { get; } = Path.Combine(AppContext.BaseDirectory, "bass");

    /// <summary>Имена успешно загруженных плагинов — показываются в окне «О программе».</summary>
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
                return $"Не найдена папка с библиотеками BASS: {NativeFolder}. " +
                       "Запустите tools\\fetch-bass.ps1 или скопируйте библиотеки вручную (см. README).";
            }

            // Явно добавляем bass\ в пути поиска DLL: иначе LoadLibrary ищет
            // bass.dll только рядом с exe и в системных папках. Неудача сама
            // по себе ещё не приговор — библиотеку может найти и обычный поиск,
            // — но если инициализация потом не задастся, причину надо назвать.
            string? searchPathNote = SetDllDirectory(NativeFolder)
                ? null
                : $" Не удалось добавить {NativeFolder} в пути поиска библиотек " +
                  $"(код {Marshal.GetLastWin32Error()}).";

            try
            {
                // Проверка не проигрывает звук, поэтому фоновые потоки обновления
                // буферов не нужны — это заметно экономит процессор на больших коллекциях.
                Bass.Configure(Configuration.UpdateThreads, 0);
                Bass.Configure(Configuration.UpdatePeriod, 0);
                Bass.Configure(Configuration.MusicVirtual, 0);

                if (!Bass.Init(NoSoundDevice, 44100, DeviceInitFlags.Default, IntPtr.Zero))
                {
                    Errors error = Bass.LastError;
                    if (error != Errors.Already)
                    {
                        return $"BASS не удалось инициализировать: {Describe(error)} ({error}).{searchPathNote}";
                    }
                }

                LoadPlugins();
                _initialized = true;

                return null;
            }
            catch (DllNotFoundException ex)
            {
                return $"Не найдена библиотека bass.dll в {NativeFolder}. " +
                       $"Запустите tools\\fetch-bass.ps1 (см. README). Подробности: {ex.Message}{searchPathNote}";
            }
            catch (BadImageFormatException ex)
            {
                return "Библиотека bass.dll не подходит по разрядности — нужна 64-битная версия. " +
                       $"Подробности: {ex.Message}";
            }
            catch (Exception ex)
            {
                return $"Не удалось запустить механизм декодирования: {ex.Message}";
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
                "Механизм декодирования аудио не запущен.",
                "BASS не инициализирована");
        }

        int handle = 0;
        bool isTracker = TrackerExtensions.Contains(Path.GetExtension(filePath));

        // Отмечаемся до первого обращения к библиотеке: закрытие ждёт, пока
        // счётчик обнулится, и только потом освобождает BASS.
        Interlocked.Increment(ref _activeProbes);

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

            // Длина, заявленная заголовком файла. У потоков без длины BASS
            // возвращает −1 — тогда сравнивать будет не с чем.
            long totalBytes = Bass.ChannelGetLength(handle);
            double declared = totalBytes > 0 ? Bass.ChannelBytes2Seconds(handle, totalBytes) : 0;

            // Отсчёты и так проходят через буфер чтения, поэтому громкость,
            // перегрузка и провалы в тишину достаются без второго прохода.
            ChannelInfo info = Bass.ChannelGetInfo(handle);
            AudioStatsAccumulator stats = new(Math.Max(1, info.Frequency * Math.Max(1, info.Channels)));

            // Спектр набирается из тех же буферов: он отвечает на вопрос, до
            // какой частоты в файле есть звук, и выдаёт «лослесс», собранный
            // из сжатого с потерями.
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
            // Один плохой файл не должен ронять проверку целиком
            // (02_ARCHITECTURE.md, раздел 4).
            return new AudioProbeResult(
                AudioProbeOutcome.ReadFailed,
                "Непредвиденная ошибка при чтении файла.",
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

    /// <summary>Быстрая проверка: только первые секунды.</summary>
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
            // Декодер не смог перевести секунды в байты — читаем фиксированный кусок.
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

    /// <summary>Полная проверка: файл читается до конца.</summary>
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
    /// Выборочная проверка: начало, конец и несколько мест в середине.
    /// </summary>
    /// <remarks>
    /// Размен между быстрой и полной: повреждение в середине трека слышно, а
    /// читается при этом несколько секунд, а не весь файл. Последнее окно берётся
    /// у самого конца — недокачанные файлы обрываются именно там.
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

        // Файл короче нескольких окон — читать его целиком и быстрее, и точнее.
        if (totalBytes <= 0 || totalBytes <= windowBytes * SampleWindows)
        {
            return ReadWholeFile(handle, format, declared, sink, cancellationToken);
        }

        long decodedBytes = 0;
        bool seekFailed = false;

        for (int i = 0; i < SampleWindows; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Последнее окно прижимается к концу файла, остальные раскладываются равномерно.
            long position = i == SampleWindows - 1
                ? Math.Max(0, totalBytes - windowBytes)
                : totalBytes / SampleWindows * i;

            if (i > 0 && !Bass.ChannelSetPosition(handle, position, PositionFlags.Bytes))
            {
                Errors error = Bass.LastError;

                if (i == SampleWindows - 1)
                {
                    // До конца файла доехать не удалось: он короче, чем обещает заголовок.
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
                ? "Перемотка недоступна — прочитано только начало"
                : null,
        };
    }

    /// <summary>Битрейт по данным декодера, килобит в секунду.</summary>
    private static int ReadBitrate(int handle) =>
        Bass.ChannelGetAttribute(handle, ChannelAttribute.Bitrate, out float bitrate)
            ? (int)Math.Round(bitrate)
            : 0;

    /// <summary>Читает поток порциями, пока не наберёт нужное или не кончится звук.</summary>
    private static ReadOutcome ReadRun(
        int handle,
        long targetBytes,
        SampleSink sink,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[ReadChunkBytes];
        long total = 0;

        while (total < targetBytes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int wanted = (int)Math.Min(ReadChunkBytes, targetBytes - total);
            int read = Bass.ChannelGetData(handle, buffer, wanted);

            if (read < 0)
            {
                Errors error = Bass.LastError;

                // Конец файла — это не ошибка: короткие треки заканчиваются раньше.
                return error == Errors.Ended
                    ? new ReadOutcome(total, null)
                    : new ReadOutcome(total, error);
            }

            if (read == 0)
            {
                break;
            }

            // Поток открыт с флагом Float, поэтому байты буфера — это отсчёты
            // в диапазоне −1…1, и пересчитывать их не нужно.
            sink.Add(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(buffer.AsSpan(0, read)));

            total += read;
        }

        return new ReadOutcome(total, null);
    }

    /// <summary>Длительность заметно меньше заявленной заголовком.</summary>
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
            $"Чтение аудиоданных прервалось: {Describe(error)}.",
            $"BASS_ChannelGetData → {error}",
            Bass.ChannelBytes2Seconds(handle, decodedBytes),
            format,
            declared);

    private static AudioProbeResult Empty(string? format, double declared) =>
        new(
            AudioProbeOutcome.Empty,
            "Декодер не получил ни одного отсчёта — аудиоданных в файле нет.",
            "BASS_ChannelGetData вернул 0 байт",
            0,
            format,
            declared);

    /// <summary>Сколько удалось прочитать и с какой ошибкой это закончилось.</summary>
    private readonly record struct ReadOutcome(long Bytes, Errors? Failure);

    /// <summary>
    /// Раздаёт прочитанные отсчёты всем, кто их считает.
    /// </summary>
    /// <remarks>
    /// Нужен, чтобы не тащить через все способы чтения два отдельных
    /// накопителя: буфер один, проход по нему тоже должен быть один.
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

            // Ждём начатые чтения. Новые не начнутся: Probe смотрит на _disposed.
            // Если поток застрял насовсем, освобождаем всё равно — программа
            // уже закрывается, и висеть в ожидании хуже.
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

    /// <summary>Загружает все найденные плагины форматов из папки <c>bass\</c>.</summary>
    private void LoadPlugins()
    {
        List<string> loaded = [];

        foreach (string dll in Directory.EnumerateFiles(NativeFolder, "bass*.dll"))
        {
            string name = Path.GetFileNameWithoutExtension(dll);

            // Сама bass.dll — не плагин.
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
                // Не все bass*.dll — плагины формата (например, basswasapi):
                // это не ошибка, просто такой файл нам не нужен.
            }
        }

        LoadedPlugins = loaded;
    }

    /// <summary>Формат, который определил сам декодер, — для сверки с расширением.</summary>
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

    /// <summary>Разбирает ошибку открытия потока в понятный итог.</summary>
    private static AudioProbeResult FromOpenError(Errors error) => error switch
    {
        Errors.WmaLicense or Errors.WmaAccesDenied or Errors.WmaIndividual => new AudioProbeResult(
            AudioProbeOutcome.PasswordProtected,
            "Файл защищён лицензией или паролем — декодировать его нельзя.",
            $"BASS → {error}"),

        Errors.Empty => new AudioProbeResult(
            AudioProbeOutcome.Empty,
            "Файл пустой — аудиоданных в нём нет.",
            $"BASS → {error}"),

        Errors.FileOpen => new AudioProbeResult(
            AudioProbeOutcome.OpenFailed,
            "Файл не удалось открыть для чтения.",
            $"BASS → {error}"),

        Errors.Memory or Errors.Init or Errors.NotAvailable => new AudioProbeResult(
            AudioProbeOutcome.EngineFailure,
            $"Механизм декодирования отказал: {Describe(error)}.",
            $"BASS → {error}"),

        _ => new AudioProbeResult(
            AudioProbeOutcome.OpenFailed,
            $"Не удалось начать декодирование: {Describe(error)}.",
            $"BASS → {error}"),
    };

    /// <summary>Человеческое описание кода ошибки BASS — без «Error 0x…» в основном тексте.</summary>
    internal static string Describe(Errors error) => error switch
    {
        Errors.OK => "ошибок нет",
        Errors.FileOpen => "файл не открывается",
        Errors.FileFormat => "формат файла не распознан",
        Errors.Codec => "нет декодера для этого формата",
        Errors.Empty => "файл пустой",
        Errors.Memory => "не хватило памяти",
        Errors.Init => "звуковая подсистема не инициализирована",
        Errors.NotAvailable => "функция недоступна",
        Errors.Unstreamable => "файл нельзя декодировать потоком",
        Errors.WmaLicense => "нужна лицензия на воспроизведение",
        Errors.WmaAccesDenied => "доступ к защищённому файлу запрещён",
        Errors.WmaIndividual => "требуется индивидуализация проигрывателя",
        Errors.Timeout => "истекло время ожидания",
        Errors.Ended => "данные закончились",
        Errors.Handle => "неверный дескриптор потока",
        Errors.Position => "неверная позиция в потоке",
        Errors.SampleFormat => "формат отсчётов не поддерживается",
        Errors.Mp4NoStream => "в контейнере MP4 нет аудиодорожки",
        Errors.Unknown => "неизвестная ошибка декодера",
        _ => $"код {error}",
    };

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDllDirectory(string lpPathName);
}
