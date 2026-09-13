using System.Diagnostics;
using MusicScanIntegrity.Core.Audio;
using MusicScanIntegrity.Core.Discovery;
using MusicScanIntegrity.Core.History;
using MusicScanIntegrity.Core.Integrity;
using MusicScanIntegrity.Core.Locking;
using MusicScanIntegrity.Core.Models;
using MusicScanIntegrity.Core.Settings;

namespace MusicScanIntegrity.Core.Scanning;

/// <summary>Проверка одного файла целиком: доступ, декодирование, теги, расширение.</summary>
public interface IFileChecker
{
    /// <summary>
    /// Выполняет весь порядок проверки одного файла из
    /// 02_ARCHITECTURE.md, раздел 1.
    /// </summary>
    Task<FileCheckResult> CheckAsync(ScanItem item, FileCheckContext context, CancellationToken cancellationToken);
}

/// <summary>Что нужно проверке файла помимо самого файла.</summary>
/// <param name="Settings">Текущие настройки.</param>
/// <param name="LockedQuestions">Очередь вопросов о занятых файлах.</param>
/// <param name="OnLargeFile">Уведомление о большом файле — показывается по ходу проверки.</param>
public sealed record FileCheckContext(
    AppSettings Settings,
    LockedFileQuestionQueue? LockedQuestions,
    Action<ScanItem, long>? OnLargeFile = null);

/// <inheritdoc cref="IFileChecker" />
public sealed class FileChecker(
    IAudioProbe audioProbe,
    IMetadataReader metadataReader,
    ILockOwnerDetector lockOwnerDetector,
    ITempCopyManager tempCopyManager,
    IContainerIntegrityChecker integrityChecker,
    IScanHistory history) : IFileChecker
{
    /// <summary>С какой длины провал в тишину считается потерянным куском.</summary>
    private const double DropoutSeconds = 1.0;

    /// <summary>Ниже этой средней громкости трек считается тихим, и провалы в нём не ищутся.</summary>
    private const double QuietRms = 0.01;

    /// <summary>Доля отсчётов на пределе шкалы, после которой это уже перегрузка.</summary>
    private const double ClippedShareThreshold = 0.001;

    /// <summary>Постоянная составляющая, после которой стоит предупредить.</summary>
    private const double DcOffsetThreshold = 0.02;

    /// <summary>
    /// Граница спектра, ниже которой «лослесс» вызывает подозрение.
    /// </summary>
    /// <remarks>
    /// 320 кбит/с обрезает примерно на 20 кГц, 192 — около 19, 128 — около 16.
    /// Порог 17,5 кГц оставляет запас: настоящий лослесс ниже него опускается
    /// редко, а вот собранный из MP3 — почти всегда.
    /// </remarks>
    private const double LosslessCutoffHz = 17_500;

    /// <summary>Граница спектра, ниже которой подозрителен уже высокий битрейт.</summary>
    private const double HighBitrateCutoffHz = 16_500;

    /// <summary>С какого битрейта ждём широкой полосы.</summary>
    private const int HighBitrateKbps = 256;

    /// <summary>Ниже этой частоты дискретизации о полосе говорить нечего.</summary>
    private const int SpectrumMinSampleRate = 44_100;

    /// <summary>Форматы, которые хранят звук без потерь.</summary>
    private static readonly HashSet<string> LosslessFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "FLAC", "WAV", "AIFF", "WV", "APE", "ALAC", "MP4", "DSD",
    };

    /// <inheritdoc />
    public async Task<FileCheckResult> CheckAsync(
        ScanItem item,
        FileCheckContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        Stopwatch stopwatch = Stopwatch.StartNew();
        List<CheckIssue> issues = [];
        AppSettings settings = context.Settings;
        string format = AudioFormats.DisplayName(item.Extension);

        try
        {
            // 1. Файл мог исчезнуть уже после того, как попал в список.
            FileInfo file = new(item.FullPath);
            if (!file.Exists)
            {
                issues.Add(new CheckIssue(
                    IssueCode.FileNotFound,
                    "Файл не найден — он исчез с диска после того, как программа его нашла.",
                    "File.Exists == false"));

                return FileCheckResult.From(item, issues, stopwatch.Elapsed, format);
            }

            long size = TryGetLength(file, item.SizeBytes);

            // 2. Большой файл — предупреждаем заранее, но проверяем всё равно.
            if (settings.LargeFileThresholdBytes is { } threshold && size > threshold)
            {
                if (settings.WarnAboutLargeFiles)
                {
                    context.OnLargeFile?.Invoke(item, size);
                    issues.Add(new CheckIssue(
                        IssueCode.LargeFile,
                        $"Очень большой файл ({Common.Format.Size(size)}) — проверка займёт больше времени.",
                        $"Размер {size} Б, порог {threshold} Б"));
                }
            }

            // 3. Доступ к файлу; если занят — действуем по правилам пользователя.
            LockResolution lockResolution = await ResolveAccessAsync(item, size, context, issues, cancellationToken)
                .ConfigureAwait(false);

            if (lockResolution.Outcome == LockOutcome.Skip)
            {
                return FileCheckResult.From(item, issues, stopwatch.Elapsed, format, actualSize: size);
            }

            await using TempCopy? copy = lockResolution.Copy;
            string pathToProbe = copy is { Created: true } ? copy.Path : item.FullPath;

            if (lockResolution.Outcome == LockOutcome.Failed)
            {
                return FileCheckResult.From(item, issues, stopwatch.Elapsed, format, actualSize: size);
            }

            // 4. Пустой файл — отдельная, понятная причина, а не «ошибка декодера».
            if (size == 0)
            {
                issues.Add(new CheckIssue(
                    IssueCode.EmptyFile,
                    "Файл пустой (0 байт) — аудиоданных в нём нет.",
                    "Размер файла равен нулю"));

                return FileCheckResult.From(item, issues, stopwatch.Elapsed, format, actualSize: size);
            }

            // 5. История: отпечаток содержимого. Он и ловит тихую порчу —
            // случай, когда байты изменились, а размер и дата остались прежними.
            HistoryOutcome historyOutcome = await Task.Run(
                () => UseHistory(item.FullPath, file, size, settings, issues, cancellationToken),
                cancellationToken).ConfigureAwait(false);

            if (historyOutcome.Unchanged)
            {
                // Файл совпал с прошлой проверкой по отпечатку: перепроверять
                // нечего, содержимое то же самое. Статус берётся прошлый, но
                // замечания этой проверки (например, о размере) остаются.
                CheckStatus carried = historyOutcome.PreviousStatus;
                foreach (CheckIssue issue in issues)
                {
                    carried = carried.Combine(issue.Severity);
                }

                return new FileCheckResult
                {
                    FullPath = item.FullPath,
                    FileName = Path.GetFileName(item.FullPath),
                    DirectoryPath = Path.GetDirectoryName(item.FullPath) ?? string.Empty,
                    SizeBytes = size,
                    Status = carried,
                    Issues = issues,
                    Duration = stopwatch.Elapsed,
                    Format = format,
                    Kind = item.Kind,
                    Unchanged = true,
                };
            }

            // 6. Декодирование с таймаутом именно на этот файл. Глубина —
            // из настроек: только начало, выборочные окна или весь файл.
            using CancellationTokenSource fileTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            fileTimeout.CancelAfter(settings.FileTimeout);

            AudioProbeResult probe;
            bool timedOut = false;

            try
            {
                probe = await Task.Run(
                    () => audioProbe.Probe(pathToProbe, ToScope(settings.CheckDepth), fileTimeout.Token),
                    fileTimeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Сработал таймаут по этому файлу — остальные проверяются дальше.
                timedOut = true;
                probe = new AudioProbeResult(AudioProbeOutcome.Ok);
            }

            if (timedOut)
            {
                issues.Add(new CheckIssue(
                    IssueCode.CheckTimeout,
                    $"Проверка заняла больше {settings.FileTimeoutSeconds} с и была прервана — файл подозрительный.",
                    $"Таймаут {settings.FileTimeoutSeconds} с"));
            }
            else if (probe.ToIssue() is { } probeIssue)
            {
                issues.Add(probeIssue);

                if (probe.Outcome == AudioProbeOutcome.EngineFailure)
                {
                    // Критический сбой декодера — движок остановит проверку целиком.
                    throw new AudioEngineFailureException(probe.Message ?? "Механизм декодирования отказал.", probe.TechnicalDetail);
                }
            }

            bool decoded = !timedOut && probe.Outcome == AudioProbeOutcome.Ok;

            // 7. Проверка файла его собственными средствами: контрольные суммы
            // формата и целостность контейнера. Она не зависит от декодера и
            // отвечает точно там, где он отвечает лишь «открылось».
            ContainerValidation? integrity = null;

            if (settings.VerifyContainerIntegrity && !timedOut)
            {
                try
                {
                    integrity = await Task.Run(
                        () => integrityChecker.Check(pathToProbe, fileTimeout.Token),
                        fileTimeout.Token).ConfigureAwait(false);

                    if (ToIssue(integrity) is { } integrityIssue)
                    {
                        issues.Add(integrityIssue);
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Таймаут по файлу пришёлся на разбор контейнера: сам файл
                    // от этого не становится повреждённым, но и проверенным
                    // называть его нельзя.
                    timedOut = true;
                    integrity = null;
                    issues.Add(new CheckIssue(
                        IssueCode.CheckTimeout,
                        $"Проверка заняла больше {settings.FileTimeoutSeconds} с и была прервана — файл подозрительный.",
                        $"Таймаут {settings.FileTimeoutSeconds} с на разборе структуры"));
                }
            }

            // Звук закончился раньше, чем обещает заголовок, — файл недокачан
            // или обрезан. Проверяется после разбора структуры: тот часто
            // говорит то же самое, и второе такое же замечание было бы шумом.
            if (probe.Truncated && !issues.Any(i => i.Code == IssueCode.Truncated))
            {
                issues.Add(new CheckIssue(
                    IssueCode.Truncated,
                    "Файл обрывается: звука в нём меньше, чем обещает заголовок.",
                    $"Заявлено {probe.DeclaredSeconds:0.#} с, прочитано {probe.DecodedSeconds:0.#} с"));
            }

            // 8. Что слышно в самих отсчётах. Считается по данным, которые уже
            // прошли через декодер, поэтому второго чтения диска не требует.
            if (decoded && probe.Stats is { Samples: > 0 } stats)
            {
                AddSoundIssues(issues, stats, settings, ToScope(settings.CheckDepth));

                if (settings.DetectTranscode && probe.Spectrum is { } spectrum)
                {
                    AddSpectrumIssue(issues, spectrum, stats, probe);
                }
            }

            // 9. Сверка расширения с содержимым — только если файл вообще читается.
            if (settings.VerifyExtensionMatchesContent && decoded)
            {
                string? actual = probe.DetectedFormat ?? ContentTypeSniffer.DetectFromFile(pathToProbe);
                if (actual is not null && !ContentTypeSniffer.Matches(item.Extension, actual))
                {
                    format = $"{actual}?";
                    issues.Add(new CheckIssue(
                        IssueCode.ExtensionMismatch,
                        $"Расширение не совпадает с содержимым: это {actual}, а не {AudioFormats.DisplayName(item.Extension)}.",
                        $"Сигнатура → {actual}"));
                }
            }

            // 10. Теги. Отсутствие тегов — мягкое замечание, а не повреждение.
            TrackMetadata? metadata = null;
            if (settings.CheckMetadata && decoded)
            {
                metadata = metadataReader.Read(pathToProbe, out string? metadataError);

                if (metadata is null)
                {
                    issues.Add(new CheckIssue(
                        IssueCode.MetadataProblem,
                        "Теги не читаются — с самим аудио при этом всё в порядке.",
                        metadataError));
                }
                else if (!metadata.IsComplete)
                {
                    issues.Add(new CheckIssue(
                        IssueCode.MetadataProblem,
                        $"Не заполнены теги: {string.Join(", ", metadata.MissingFields)}. Это не повреждение файла.",
                        null));
                }

                if (metadata is not null)
                {
                    IReadOnlyList<string> broken = Analysis.TextIntegrity.BrokenFields(
                        ("Название", metadata.Title),
                        ("Исполнитель", metadata.Artist),
                        ("Альбом", metadata.Album));

                    if (broken.Count > 0)
                    {
                        issues.Add(new CheckIssue(
                            IssueCode.BrokenTagText,
                            $"Теги прочитаны не в той кодировке — вместо букв кракозябры: {string.Join(", ", broken)}.",
                            Analysis.TextIntegrity.Describe(metadata.Title)
                                ?? Analysis.TextIntegrity.Describe(metadata.Artist)
                                ?? Analysis.TextIntegrity.Describe(metadata.Album)));
                    }
                }
            }

            FileCheckResult result = FileCheckResult.From(
                item,
                issues,
                stopwatch.Elapsed,
                format,
                metadata,
                size,
                integrity,
                probe.DeclaredSeconds > 0 ? probe.DeclaredSeconds : metadata?.DurationSeconds ?? 0);

            RememberInHistory(item.FullPath, file, size, historyOutcome.Hash, result.Status, settings);

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AudioEngineFailureException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Непредвиденная ошибка на одном файле — это строка в результатах,
            // а не остановка всей проверки (02_ARCHITECTURE.md, раздел 4).
            issues.Add(new CheckIssue(
                IssueCode.UnexpectedError,
                "Проверить файл не удалось из-за непредвиденной ошибки.",
                $"{ex.GetType().Name} · {ex.Message}"));

            return FileCheckResult.From(item, issues, stopwatch.Elapsed, format);
        }
    }

    /// <summary>
    /// Добавляет замечания, видные по самим отсчётам.
    /// </summary>
    /// <remarks>
    /// Про тишину и провалы говорим только тогда, когда прочитано не одно
    /// начало: у трека с длинной подводкой первые две секунды и должны быть
    /// тихими, и объявлять его пустым было бы неправдой.
    /// </remarks>
    private static void AddSoundIssues(
        List<CheckIssue> issues,
        AudioStats stats,
        AppSettings settings,
        DecodeScope scope)
    {
        if (settings.DetectSilence && scope != DecodeScope.Quick)
        {
            if (stats.IsSilent)
            {
                issues.Add(new CheckIssue(
                    IssueCode.DigitalSilence,
                    "Файл читается, но звука в нём нет — сплошная тишина.",
                    $"Наибольший уровень {stats.Peak:0.####}"));
            }
            else if (stats.LongestSilentSeconds >= DropoutSeconds && stats.Rms > QuietRms)
            {
                issues.Add(new CheckIssue(
                    IssueCode.AudioDropout,
                    $"Внутри трека провал в тишину на {stats.LongestSilentSeconds:0.#} с — похоже на потерянный кусок.",
                    $"Тишина {stats.LongestSilentRun} отсчётов подряд при средней громкости {stats.Rms:0.###}"));
            }
        }

        if (!settings.DetectClipping)
        {
            return;
        }

        if (stats.ClippedShare > ClippedShareThreshold)
        {
            issues.Add(new CheckIssue(
                IssueCode.Clipping,
                $"Звук упирается в предел громкости: {stats.ClippedShare * 100:0.#} % отсчётов.",
                $"{stats.ClippedSamples} отсчётов из {stats.Samples}"));
        }

        if (Math.Abs(stats.DcOffset) > DcOffsetThreshold)
        {
            issues.Add(new CheckIssue(
                IssueCode.DcOffset,
                "У записи смещён ноль — признак плохой оцифровки.",
                $"Постоянная составляющая {stats.DcOffset:0.###}"));
        }
    }

    /// <summary>
    /// Добавляет подозрение на перекодирование, если верхних частот нет там,
    /// где они должны быть.
    /// </summary>
    /// <remarks>
    /// Ровно подозрение: тихую и старую запись это правило может оговорить
    /// зря, поэтому статус жёлтый, а формулировка — «похоже».
    /// </remarks>
    private static void AddSpectrumIssue(
        List<CheckIssue> issues,
        Analysis.SpectrumProfile spectrum,
        AudioStats stats,
        AudioProbeResult probe)
    {
        if (!spectrum.IsReliable || stats.IsSilent || stats.Rms < QuietRms)
        {
            return;
        }

        if (stats.SampleRate <= 0 || spectrum.NyquistHz * 2 < SpectrumMinSampleRate)
        {
            return;
        }

        bool lossless = probe.DetectedFormat is { } format && LosslessFormats.Contains(format);

        if (lossless && spectrum.CutoffHz < LosslessCutoffHz)
        {
            issues.Add(new CheckIssue(
                IssueCode.TranscodeSuspected,
                $"Похоже на перекодирование: формат без потерь, а звук обрывается на {spectrum.CutoffHz / 1000:0.#} кГц.",
                $"Верхняя граница {spectrum.CutoffHz:0} Гц из возможных {spectrum.NyquistHz:0} Гц, кусков {spectrum.Blocks}"));

            return;
        }

        if (!lossless && probe.BitrateKbps >= HighBitrateKbps && spectrum.CutoffHz < HighBitrateCutoffHz)
        {
            issues.Add(new CheckIssue(
                IssueCode.TranscodeSuspected,
                $"Похоже на перекодирование: битрейт {probe.BitrateKbps} кбит/с, а звук обрывается на {spectrum.CutoffHz / 1000:0.#} кГц.",
                $"Верхняя граница {spectrum.CutoffHz:0} Гц, кусков {spectrum.Blocks}"));
        }
    }

    /// <summary>
    /// Сверяет файл с историей: считает отпечаток, находит тихую порчу и
    /// решает, нужно ли перепроверять содержимое.
    /// </summary>
    private HistoryOutcome UseHistory(
        string path,
        FileInfo file,
        long size,
        AppSettings settings,
        List<CheckIssue> issues,
        CancellationToken cancellationToken)
    {
        if (!settings.TrackChanges || !history.IsOpen || size == 0)
        {
            return default;
        }

        string? hash = FileHasher.Compute(path, cancellationToken);

        if (hash is null)
        {
            return default;
        }

        FileHistoryEntry? previous = history.Find(path);

        if (previous is null)
        {
            return new HistoryOutcome(hash, false, CheckStatus.Ok);
        }

        bool sameOutside = previous.SizeBytes == size && previous.ModifiedUtc == file.LastWriteTimeUtc;

        if (sameOutside && previous.Hash != hash)
        {
            issues.Add(new CheckIssue(
                IssueCode.SilentCorruption,
                "Содержимое файла изменилось само собой: размер и дата прежние, а байты другие.",
                $"Было {previous.Hash}, стало {hash}; прошлая проверка {previous.CheckedAt:dd.MM.yyyy}"));

            return new HistoryOutcome(hash, false, CheckStatus.Ok);
        }

        // Пропускаем только то, что в прошлый раз было в порядке: у
        // повреждённого файла причина хранится не в базе, а в замечаниях,
        // и без них статус «повреждён» ничего не объяснит.
        bool unchanged = previous.Hash == hash && previous.Status == CheckStatus.Ok;

        return new HistoryOutcome(hash, unchanged, previous.Status);
    }

    /// <summary>Запоминает файл в истории.</summary>
    private void RememberInHistory(
        string path,
        FileInfo file,
        long size,
        string? hash,
        CheckStatus status,
        AppSettings settings)
    {
        if (!settings.TrackChanges || !history.IsOpen || hash is null)
        {
            return;
        }

        history.Save(new FileHistoryEntry(
            path,
            size,
            file.LastWriteTimeUtc,
            hash,
            status,
            DateTimeOffset.Now));
    }

    /// <summary>Что дала сверка с историей.</summary>
    /// <param name="Hash">Отпечаток содержимого; <see langword="null" /> — не считался.</param>
    /// <param name="Unchanged">Файл совпал с прошлой проверкой.</param>
    /// <param name="PreviousStatus">Статус прошлой проверки.</param>
    private readonly record struct HistoryOutcome(string? Hash, bool Unchanged, CheckStatus PreviousStatus);

    /// <summary>Переводит настройку глубины в область декодирования.</summary>
    private static DecodeScope ToScope(CheckDepth depth) => depth switch
    {
        CheckDepth.Quick => DecodeScope.Quick,
        CheckDepth.Full => DecodeScope.Full,
        _ => DecodeScope.Sampled,
    };

    /// <summary>Переводит вердикт разборщика в замечание по файлу.</summary>
    /// <remarks>
    /// Вердикты «сумма сошлась», «структура цела» и «разборщика нет» замечаний
    /// не дают: это не проблемы файла. Разница между ними видна в подробностях.
    /// </remarks>
    private static CheckIssue? ToIssue(ContainerValidation validation) => validation.Verdict switch
    {
        ContainerVerdict.Damaged => new CheckIssue(
            validation.Damage switch
            {
                ContainerDamage.Checksum => IssueCode.ChecksumMismatch,
                ContainerDamage.Truncation => IssueCode.Truncated,
                _ => IssueCode.ContainerDamaged,
            },
            validation.Message ?? "Файл повреждён.",
            validation.TechnicalDetail),

        _ => null,
    };

    /// <summary>Что делать дальше после разбора доступа к файлу.</summary>
    private enum LockOutcome
    {
        /// <summary>Можно проверять.</summary>
        Proceed,

        /// <summary>Файл пропускается.</summary>
        Skip,

        /// <summary>Проверить не получится, замечание уже добавлено.</summary>
        Failed,
    }

    private readonly record struct LockResolution(LockOutcome Outcome, TempCopy? Copy = null);

    /// <summary>
    /// Проверяет доступ и, если файл занят, действует по выбранному правилу:
    /// спросить / пропустить / подождать / временная копия / закрыть владельца.
    /// </summary>
    private async Task<LockResolution> ResolveAccessAsync(
        ScanItem item,
        long size,
        FileCheckContext context,
        List<CheckIssue> issues,
        CancellationToken cancellationToken)
    {
        AppSettings settings = context.Settings;
        FileAccessCheck access = FileAccessProbe.Check(item.FullPath);

        switch (access.State)
        {
            case FileAccessState.Available:
                return new LockResolution(LockOutcome.Proceed);

            case FileAccessState.NotFound:
                issues.Add(new CheckIssue(
                    IssueCode.FileNotFound,
                    "Файл не найден — он исчез с диска после того, как программа его нашла.",
                    access.TechnicalDetail));
                return new LockResolution(LockOutcome.Failed);

            case FileAccessState.AccessDenied:
                issues.Add(new CheckIssue(
                    IssueCode.AccessDenied,
                    "Windows не дал прочитать файл — проверить его не получилось.",
                    access.TechnicalDetail));
                return new LockResolution(LockOutcome.Failed);
        }

        // Дальше — файл занят другой программой.
        LockedFileAction action = settings.LockedFileAction;

        if (action == LockedFileAction.Ask)
        {
            LockOwnerResult owner = settings.DetectOwnerProcess
                ? lockOwnerDetector.Detect(item.FullPath)
                : LockOwnerResult.Unknown("Определение владельца выключено в настройках");

            if (context.LockedQuestions is null)
            {
                action = LockedFileAction.Skip;
            }
            else
            {
                LockedFileDecision decision = await context.LockedQuestions
                    .AskAsync(item.FullPath, size, owner, cancellationToken)
                    .ConfigureAwait(false);

                if (decision.StopScan)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                action = decision.Action;
            }
        }

        switch (action)
        {
            case LockedFileAction.Skip:
                issues.Add(new CheckIssue(
                    IssueCode.LockedSkippedByUser,
                    "Файл занят другой программой и пропущен.",
                    access.TechnicalDetail));
                return new LockResolution(LockOutcome.Skip);

            case LockedFileAction.Wait:
                return await WaitForReleaseAsync(item, settings, issues, access, cancellationToken).ConfigureAwait(false);

            case LockedFileAction.TempCopy:
            case LockedFileAction.CloseOwner:
                // «Закрыть владельца» программа сама не делает: убивать чужой процесс
                // опасно, и решение об этом принимает пользователь в диалоге.
                // Если сюда всё же дошли — проверяем по временной копии, это безопасно.
                TempCopy copy = await tempCopyManager.CreateAsync(item.FullPath, cancellationToken).ConfigureAwait(false);

                if (!copy.Created)
                {
                    issues.Add(new CheckIssue(
                        IssueCode.LockedCopyFailed,
                        "Файл занят, и сделать его временную копию не удалось.",
                        copy.Error));
                    return new LockResolution(LockOutcome.Failed, copy);
                }

                issues.Add(new CheckIssue(
                    IssueCode.LockedCheckedViaCopy,
                    "Файл был занят другой программой — проверен по временной копии.",
                    access.TechnicalDetail));
                return new LockResolution(LockOutcome.Proceed, copy);

            default:
                issues.Add(new CheckIssue(
                    IssueCode.LockedSkippedByUser,
                    "Файл занят другой программой и пропущен.",
                    access.TechnicalDetail));
                return new LockResolution(LockOutcome.Skip);
        }
    }

    /// <summary>Ждёт освобождения файла заданное число попыток.</summary>
    private async Task<LockResolution> WaitForReleaseAsync(
        ScanItem item,
        AppSettings settings,
        List<CheckIssue> issues,
        FileAccessCheck lastAccess,
        CancellationToken cancellationToken)
    {
        TimeSpan step = TimeSpan.FromSeconds(Math.Max(1, settings.LockedWaitSeconds / Math.Max(1, settings.LockedRetryCount)));

        for (int attempt = 1; attempt <= settings.LockedRetryCount; attempt++)
        {
            await Task.Delay(step, cancellationToken).ConfigureAwait(false);

            FileAccessCheck retry = FileAccessProbe.Check(item.FullPath);
            if (retry.State == FileAccessState.Available)
            {
                return new LockResolution(LockOutcome.Proceed);
            }

            lastAccess = retry;
        }

        issues.Add(new CheckIssue(
            IssueCode.LockedWaitTimeout,
            $"Файл оставался занят все {settings.LockedWaitSeconds} с ожидания — проверить его не удалось.",
            lastAccess.TechnicalDetail));

        return new LockResolution(LockOutcome.Failed);
    }

    private static long TryGetLength(FileInfo file, long fallback)
    {
        try
        {
            return file.Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return fallback;
        }
    }
}

/// <summary>
/// Критический отказ механизма декодирования: проверку продолжать нельзя,
/// но программа при этом не закрывается (03_IMPLEMENTATION_GUIDE.md, раздел 1).
/// </summary>
public sealed class AudioEngineFailureException(string message, string? technicalDetail = null)
    : Exception(message)
{
    /// <summary>Техническая причина для журнала и подробностей.</summary>
    public string? TechnicalDetail { get; } = technicalDetail;
}
