using System.Diagnostics;
using MusicScanIntegrity.Core.Audio;
using MusicScanIntegrity.Core.Discovery;
using MusicScanIntegrity.Core.History;
using MusicScanIntegrity.Core.Integrity;
using MusicScanIntegrity.Core.Locking;
using MusicScanIntegrity.Core.Models;
using MusicScanIntegrity.Core.Resources;
using MusicScanIntegrity.Core.Settings;

namespace MusicScanIntegrity.Core.Scanning;

/// <summary>Checks one file end to end: access, decoding, tags, extension.</summary>
public interface IFileChecker
{
    /// <summary>
    /// Runs the whole check sequence for one file.
    /// </summary>
    Task<FileCheckResult> CheckAsync(ScanItem item, FileCheckContext context, CancellationToken cancellationToken);
}

/// <summary>What the file check needs besides the file itself.</summary>
/// <param name="OnLargeFile">Notification raised while the scan runs.</param>
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
    /// <summary>Silent gap length that counts as lost data.</summary>
    private const double DropoutSeconds = 1.0;

    /// <summary>Below this mean level a track counts as quiet and dropouts are not sought.</summary>
    private const double QuietRms = 0.01;

    /// <summary>Share of samples at full scale that counts as clipping.</summary>
    private const double ClippedShareThreshold = 0.001;

    /// <summary>DC offset worth warning about.</summary>
    private const double DcOffsetThreshold = 0.02;

    /// <summary>
    /// Spectral edge below which a lossless file becomes suspicious.
    /// </summary>
    /// <remarks>
    /// 320 kbps cuts around 20 kHz, 192 near 19, 128 near 16. A 17.5 kHz
    /// threshold leaves room: genuine lossless rarely falls below it, while
    /// something built from MP3 almost always does.
    /// </remarks>
    private const double LosslessCutoffHz = 17_500;

    /// <summary>Spectral edge that makes even a high bitrate suspicious.</summary>
    private const double HighBitrateCutoffHz = 16_500;

    /// <summary>Bitrate from which a wide band is expected.</summary>
    private const int HighBitrateKbps = 256;

    /// <summary>Below this sample rate bandwidth says nothing.</summary>
    private const int SpectrumMinSampleRate = 44_100;

    /// <summary>Formats that store audio losslessly.</summary>
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
            // 1. The file may have gone since the walk listed it.
            FileInfo file = new(item.FullPath);
            if (!file.Exists)
            {
                issues.Add(new CheckIssue(
                    IssueCode.FileNotFound,
                    Strings.Check_FileNotFound,
                    "File.Exists == false"));

                return FileCheckResult.From(item, issues, stopwatch.Elapsed, format);
            }

            long size = TryGetLength(file, item.SizeBytes);

            // 2. Large file: warn up front, but check it anyway.
            if (settings.LargeFileThresholdBytes is { } threshold && size > threshold)
            {
                if (settings.WarnAboutLargeFiles)
                {
                    context.OnLargeFile?.Invoke(item, size);
                    issues.Add(new CheckIssue(
                        IssueCode.LargeFile,
                        Common.Format.Text(Strings.Check_LargeFile, Common.Format.Size(size)),
                        Common.Format.Text(Strings.Check_LargeFile_Detail, size, threshold)));
                }
            }

            // 3. Access; if locked, follow the user's rule.
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

            // 4. An empty file is its own clear cause, not a decoder error.
            if (size == 0)
            {
                issues.Add(new CheckIssue(
                    IssueCode.EmptyFile,
                    Strings.Check_EmptyFile,
                    Strings.Check_EmptyFile_Detail));

                return FileCheckResult.From(item, issues, stopwatch.Elapsed, format, actualSize: size);
            }

            // 5. History: the content fingerprint, which is what catches
            // silent corruption — bytes changed, size and date unchanged.
            HistoryOutcome historyOutcome = await Task.Run(
                () => UseHistory(item.FullPath, file, size, settings, issues, cancellationToken),
                cancellationToken).ConfigureAwait(false);

            if (historyOutcome.Unchanged)
            {
                // Fingerprint matches the previous scan, so there is nothing
                // to re-check. The old status is reused, but findings from this
                // run — the size warning, say — still apply.
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

            // 6. Decoding under this file's own timeout. The depth comes from
            // settings: start only, sampled windows or the whole file.
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
                // This file timed out; the rest carry on.
                timedOut = true;
                probe = new AudioProbeResult(AudioProbeOutcome.Ok);
            }

            if (timedOut)
            {
                issues.Add(new CheckIssue(
                    IssueCode.CheckTimeout,
                    Common.Format.Text(Strings.Check_Timeout, settings.FileTimeoutSeconds),
                    Common.Format.Text(Strings.Check_Timeout_Detail, settings.FileTimeoutSeconds)));
            }
            else if (probe.ToIssue() is { } probeIssue)
            {
                issues.Add(probeIssue);

                if (probe.Outcome == AudioProbeOutcome.EngineFailure)
                {
                    // Critical decoder failure; the engine stops the scan.
                    throw new AudioEngineFailureException(probe.Message ?? Strings.Check_DecoderFailed, probe.TechnicalDetail);
                }
            }

            bool decoded = !timedOut && probe.Outcome == AudioProbeOutcome.Ok;

            // 7. Validation against the file's own format: checksums and
            // container integrity. Independent of the decoder, and definite
            // where the decoder only answers "it opened".
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
                    // The timeout landed during container parsing. That does
                    // not make the file damaged, but it cannot be called
                    // checked either.
                    timedOut = true;
                    integrity = null;
                    issues.Add(new CheckIssue(
                        IssueCode.CheckTimeout,
                        Common.Format.Text(Strings.Check_Timeout, settings.FileTimeoutSeconds),
                        Common.Format.Text(Strings.Check_Timeout_StructureDetail, settings.FileTimeoutSeconds)));
                }
            }

            // Audio ended before the header promised: truncated or a partial
            // download. Checked after the structure pass, which often says the
            // same thing, so a second identical finding would be noise.
            if (probe.Truncated && !issues.Any(i => i.Code == IssueCode.Truncated))
            {
                issues.Add(new CheckIssue(
                    IssueCode.Truncated,
                    Strings.Check_Truncated,
                    Common.Format.Text(Strings.Check_Truncated_Detail, probe.DeclaredSeconds, probe.DecodedSeconds)));
            }

            // 8. What the samples themselves show. Computed from data the
            // decoder already produced, so no second disk read.
            if (decoded && probe.Stats is { Samples: > 0 } stats)
            {
                AddSoundIssues(issues, stats, settings, ToScope(settings.CheckDepth));

                if (settings.DetectTranscode && probe.Spectrum is { } spectrum)
                {
                    AddSpectrumIssue(issues, spectrum, stats, probe);
                }
            }

            // 9. Extension against contents, only if the file reads at all.
            if (settings.VerifyExtensionMatchesContent && decoded)
            {
                string? actual = probe.DetectedFormat ?? ContentTypeSniffer.DetectFromFile(pathToProbe);
                if (actual is not null && !ContentTypeSniffer.Matches(item.Extension, actual))
                {
                    format = $"{actual}?";
                    issues.Add(new CheckIssue(
                        IssueCode.ExtensionMismatch,
                        Common.Format.Text(Strings.Check_ExtensionMismatch, actual, AudioFormats.DisplayName(item.Extension)),
                        Common.Format.Text(Strings.Check_ExtensionMismatch_Detail, actual)));
                }
            }

            // 10. Tags. Missing tags are a warning, not damage.
            TrackMetadata? metadata = null;
            if (settings.CheckMetadata && decoded)
            {
                metadata = metadataReader.Read(pathToProbe, out string? metadataError);

                if (metadata is null)
                {
                    issues.Add(new CheckIssue(
                        IssueCode.MetadataProblem,
                        Strings.Check_TagsUnreadable,
                        metadataError));
                }
                else if (!metadata.IsComplete)
                {
                    issues.Add(new CheckIssue(
                        IssueCode.MetadataProblem,
                        Common.Format.Text(Strings.Check_TagsMissing, string.Join(", ", metadata.MissingFields)),
                        null));
                }

                if (metadata is not null)
                {
                    IReadOnlyList<string> broken = Analysis.TextIntegrity.BrokenFields(
                        (Strings.Tag_Field_Title, metadata.Title),
                        (Strings.Tag_Field_Artist, metadata.Artist),
                        (Strings.Tag_Field_Album, metadata.Album));

                    if (broken.Count > 0)
                    {
                        issues.Add(new CheckIssue(
                            IssueCode.BrokenTagText,
                            Common.Format.Text(Strings.Check_BrokenTagText, string.Join(", ", broken)),
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
            // An unexpected error on one file is a row in the results, not a
            // reason to stop the scan.
            issues.Add(new CheckIssue(
                IssueCode.UnexpectedError,
                Strings.Check_Unexpected,
                $"{ex.GetType().Name} · {ex.Message}"));

            return FileCheckResult.From(item, issues, stopwatch.Elapsed, format);
        }
    }

    /// <summary>
    /// Adds findings visible in the samples themselves.
    /// </summary>
    /// <remarks>
    /// Silence and dropouts are only reported when more than the start was
    /// read: a track with a long intro is supposed to be quiet for the first
    /// two seconds, and calling it empty would be untrue.
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
                    Strings.Check_DigitalSilence,
                    Common.Format.Text(Strings.Check_DigitalSilence_Detail, stats.Peak)));
            }
            else if (stats.LongestSilentSeconds >= DropoutSeconds && stats.Rms > QuietRms)
            {
                issues.Add(new CheckIssue(
                    IssueCode.AudioDropout,
                    Common.Format.Text(Strings.Check_Dropout, stats.LongestSilentSeconds),
                    Common.Format.Text(Strings.Check_Dropout_Detail, stats.LongestSilentRun, stats.Rms)));
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
                Common.Format.Text(Strings.Check_Clipping, stats.ClippedShare * 100),
                Common.Format.Text(Strings.Check_Clipping_Detail, stats.ClippedSamples, stats.Samples)));
        }

        if (Math.Abs(stats.DcOffset) > DcOffsetThreshold)
        {
            issues.Add(new CheckIssue(
                IssueCode.DcOffset,
                Strings.Check_DcOffset,
                Common.Format.Text(Strings.Check_DcOffset_Detail, stats.DcOffset)));
        }
    }

    /// <summary>
    /// Flags suspected re-encoding when the top end is missing where it
    /// should be present.
    /// </summary>
    /// <remarks>
    /// A suspicion only: the rule can misjudge quiet and old recordings, so
    /// the status is a warning and the wording is hedged.
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
                Common.Format.Text(Strings.Check_TranscodeLossless, spectrum.CutoffHz / 1000),
                Common.Format.Text(Strings.Check_TranscodeLossless_Detail, spectrum.CutoffHz, spectrum.NyquistHz, spectrum.Blocks)));

            return;
        }

        if (!lossless && probe.BitrateKbps >= HighBitrateKbps && spectrum.CutoffHz < HighBitrateCutoffHz)
        {
            issues.Add(new CheckIssue(
                IssueCode.TranscodeSuspected,
                Common.Format.Text(Strings.Check_TranscodeBitrate, probe.BitrateKbps, spectrum.CutoffHz / 1000),
                Common.Format.Text(Strings.Check_TranscodeBitrate_Detail, spectrum.CutoffHz, spectrum.Blocks)));
        }
    }

    /// <summary>
    /// Compares the file with the history: fingerprints it, detects silent
    /// corruption and decides whether the contents need re-checking.
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
                Strings.Check_SilentCorruption,
                Common.Format.Text(Strings.Check_SilentCorruption_Detail, previous.Hash, hash, previous.CheckedAt)));

            return new HistoryOutcome(hash, false, CheckStatus.Ok);
        }

        // Only previously healthy files are skipped: for a damaged one the
        // cause lives in the findings rather than the database, and the status
        // alone would explain nothing.
        bool unchanged = previous.Hash == hash && previous.Status == CheckStatus.Ok;

        return new HistoryOutcome(hash, unchanged, previous.Status);
    }

    /// <summary>Records the file in the history.</summary>
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

    /// <summary>Outcome of the history comparison.</summary>
    /// <param name="Hash">Content fingerprint; <see langword="null" /> when not computed.</param>
    private readonly record struct HistoryOutcome(string? Hash, bool Unchanged, CheckStatus PreviousStatus);

    /// <summary>Maps the depth setting onto a decode scope.</summary>
    private static DecodeScope ToScope(CheckDepth depth) => depth switch
    {
        CheckDepth.Quick => DecodeScope.Quick,
        CheckDepth.Full => DecodeScope.Full,
        _ => DecodeScope.Sampled,
    };

    /// <summary>Turns a validator verdict into a file finding.</summary>
    /// <remarks>
    /// "Checksum matched", "structure intact" and "no validator" produce no
    /// finding — none of them is a problem. The difference shows in the
    /// details pane.
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
            validation.Message ?? Strings.Check_FileDamaged,
            validation.TechnicalDetail),

        _ => null,
    };

    /// <summary>What to do after resolving access to the file.</summary>
    private enum LockOutcome
    {
        /// <summary>Safe to check.</summary>
        Proceed,

        /// <summary>The file is skipped.</summary>
        Skip,

        /// <summary>Cannot be checked; a finding has already been added.</summary>
        Failed,
    }

    private readonly record struct LockResolution(LockOutcome Outcome, TempCopy? Copy = null);

    /// <summary>
    /// Probes access and, when the file is locked, follows the chosen rule:
    /// ask, skip, wait, temporary copy or close the owner.
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
                    Strings.Check_FileNotFound,
                    access.TechnicalDetail));
                return new LockResolution(LockOutcome.Failed);

            case FileAccessState.AccessDenied:
                issues.Add(new CheckIssue(
                    IssueCode.AccessDenied,
                    Strings.Check_AccessDenied,
                    access.TechnicalDetail));
                return new LockResolution(LockOutcome.Failed);
        }

        // From here on the file is locked by another process.
        LockedFileAction action = settings.LockedFileAction;

        if (action == LockedFileAction.Ask)
        {
            LockOwnerResult owner = settings.DetectOwnerProcess
                ? lockOwnerDetector.Detect(item.FullPath)
                : LockOwnerResult.Unknown(Strings.Lock_OwnerDetectionOff);

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
                    Strings.Lock_Skipped,
                    access.TechnicalDetail));
                return new LockResolution(LockOutcome.Skip);

            case LockedFileAction.Wait:
                return await WaitForReleaseAsync(item, settings, issues, access, cancellationToken).ConfigureAwait(false);

            case LockedFileAction.TempCopy:
            case LockedFileAction.CloseOwner:
                // Closing the owner is never done automatically: killing
                // someone else's process is the user's decision, made in the
                // dialog. Reaching here falls back to a temporary copy.
                TempCopy copy = await tempCopyManager.CreateAsync(item.FullPath, cancellationToken).ConfigureAwait(false);

                if (!copy.Created)
                {
                    issues.Add(new CheckIssue(
                        IssueCode.LockedCopyFailed,
                        Strings.Lock_CopyFailed,
                        copy.Error));
                    return new LockResolution(LockOutcome.Failed, copy);
                }

                issues.Add(new CheckIssue(
                    IssueCode.LockedCheckedViaCopy,
                    Strings.Lock_CheckedViaCopy,
                    access.TechnicalDetail));
                return new LockResolution(LockOutcome.Proceed, copy);

            default:
                issues.Add(new CheckIssue(
                    IssueCode.LockedSkippedByUser,
                    Strings.Lock_Skipped,
                    access.TechnicalDetail));
                return new LockResolution(LockOutcome.Skip);
        }
    }

    /// <summary>Waits for the lock to clear, for the configured number of retries.</summary>
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
            Common.Format.Text(Strings.Lock_WaitTimeout, settings.LockedWaitSeconds),
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
/// Critical decoder failure: the scan cannot continue, but the application
/// stays open.
/// </summary>
public sealed class AudioEngineFailureException(string message, string? technicalDetail = null)
    : Exception(message)
{
    /// <summary>Technical cause for the details pane.</summary>
    public string? TechnicalDetail { get; } = technicalDetail;
}
