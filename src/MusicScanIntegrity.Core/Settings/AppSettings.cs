using System.Text.Json.Serialization;

namespace MusicScanIntegrity.Core.Settings;

/// <summary>
/// Every user setting. Sections mirror the settings window: general, scanning,
/// locked files, formats, reports and appearance.
/// </summary>
/// <remarks>
/// Deliberately flat and JSON-serializable: the settings file has to stay
/// readable and repairable by hand. Settable properties rather than a record,
/// because the settings window edits one field at a time.
/// </remarks>
public sealed class AppSettings
{
    /// <summary>Settings file format version, for future migrations.</summary>
    public int SchemaVersion { get; set; } = 1;

    // ── General ──────────────────────────────────────────────────────────────

    /// <summary>Remember the last folder and offer it at startup.</summary>
    public bool RememberLastFolder { get; set; } = true;

    /// <summary>Last folder the user picked.</summary>
    public string? LastFolder { get; set; }

    /// <summary>Offer to start the scan as soon as a folder is picked.</summary>
    public bool OfferStartAfterFolderSelected { get; set; } = true;

    /// <summary>Show the welcome tip on first run.</summary>
    public bool ShowFirstRunTip { get; set; } = true;

    /// <summary>Play the system sound when the scan finishes.</summary>
    public bool SoundOnFinish { get; set; }

    // ── Scanning ─────────────────────────────────────────────────────────────

    /// <summary>Walk subfolders.</summary>
    public bool Recursive { get; set; } = true;

    /// <summary>Check tags. Off by default: missing tags are not damage.</summary>
    public bool CheckMetadata { get; set; }

    /// <summary>Look for missing paths inside playlists.</summary>
    public bool CheckPlaylists { get; set; } = true;

    /// <summary>How much of each file the decoder reads.</summary>
    /// <remarks>
    /// Sampled by default. Reading only the start misses damage in the middle
    /// of a track; decoding a whole collection takes hours.
    /// </remarks>
    public CheckDepth CheckDepth { get; set; } = CheckDepth.Sampled;

    /// <summary>Report digital silence and dropouts inside a track.</summary>
    /// <remarks>
    /// On by default: a file that decodes to silence is formally healthy and
    /// has nothing to listen to. Measured from samples the decoder already
    /// produced, so it costs nothing.
    /// </remarks>
    public bool DetectSilence { get; set; } = true;

    /// <summary>Report clipping and DC offset.</summary>
    /// <remarks>
    /// Off by default on purpose: modern masters hit the top of the scale all
    /// the time, so enabling this would flag half of a typical collection.
    /// </remarks>
    public bool DetectClipping { get; set; }

    /// <summary>Look for signs of re-encoding: a spectrum cut off at the top.</summary>
    /// <remarks>
    /// The result is a suspicion, not a verdict: old and deliberately
    /// narrow-band recordings lack top end without any re-encoding, which is
    /// why the finding is a warning worded as "looks like".
    /// </remarks>
    public bool DetectTranscode { get; set; } = true;

    /// <summary>Inspect albums folder by folder and look for duplicates.</summary>
    /// <remarks>
    /// Computed from results already in hand; no file is read twice. Some of
    /// these checks need tags, which carry the track numbers and album names.
    /// </remarks>
    public bool InspectCollection { get; set; } = true;

    /// <summary>Track corruption: fingerprint every file and keep the history.</summary>
    /// <remarks>
    /// Off by default because it is expensive: the first scan reads every file
    /// whole and creates a database. In exchange it catches what nothing else
    /// does — contents changed while size and date stayed put, which is what a
    /// failing disk looks like.
    /// </remarks>
    public bool TrackChanges { get; set; }

    /// <summary>Verify the extension against the actual contents.</summary>
    public bool VerifyExtensionMatchesContent { get; set; } = true;

    /// <summary>Validate the file against its own checksums and structure.</summary>
    /// <remarks>
    /// On by default: the only check that gives a definite answer rather than
    /// "the decoder opened it". Reads the whole file but does not decompress,
    /// so it is bound by disk speed rather than CPU.
    /// </remarks>
    public bool VerifyContainerIntegrity { get; set; } = true;

    /// <summary>Large-file threshold in megabytes; 0 means no limit.</summary>
    public int LargeFileThresholdMb { get; set; } = 500;

    /// <summary>Per-file timeout in seconds.</summary>
    public int FileTimeoutSeconds { get; set; } = 60;

    /// <summary>Derive the thread count from the number of cores.</summary>
    public bool AutoParallelism { get; set; } = true;

    /// <summary>Manual thread count, used when <see cref="AutoParallelism"/> is off.</summary>
    public int ManualParallelism { get; set; } = 4;

    /// <summary>Take the drive type into account when choosing the thread count.</summary>
    /// <remarks>
    /// On a spinning disk eight parallel reads make the head seek back and
    /// forth, which is slower than two threads. An SSD has no such penalty.
    /// </remarks>
    public bool RespectDriveType { get; set; } = true;

    /// <summary>Warn about large files before checking them.</summary>
    public bool WarnAboutLargeFiles { get; set; } = true;

    // ── Locked files ─────────────────────────────────────────────────────────

    /// <summary>Default action for a locked file.</summary>
    public LockedFileAction LockedFileAction { get; set; } = LockedFileAction.Ask;

    /// <summary>Seconds to wait for the lock to clear before retrying.</summary>
    public int LockedWaitSeconds { get; set; } = 30;

    /// <summary>How many times to retry.</summary>
    public int LockedRetryCount { get; set; } = 3;

    /// <summary>Try to identify the process holding the file.</summary>
    public bool DetectOwnerProcess { get; set; } = true;

    /// <summary>
    /// Offer to close the owning process. Off by default: closing someone
    /// else's process can lose their work.
    /// </summary>
    public bool OfferCloseOwner { get; set; }

    /// <summary>Show the "apply to all locked files" checkbox in the prompt.</summary>
    public bool AllowApplyToAllLocked { get; set; } = true;

    // ── Formats ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Audio extensions the user turned off. Exclusions are stored rather than
    /// inclusions so newly supported formats are enabled automatically.
    /// </summary>
    public List<string> DisabledExtensions { get; set; } = [];

    /// <summary>Extra extensions added by the user, for example ".mpc".</summary>
    public List<string> CustomExtensions { get; set; } = [];

    /// <summary>
    /// Experimental ISO (SACD) disc image support. Off by default: an ordinary
    /// .iso is not an audio file.
    /// </summary>
    public bool EnableIsoSacd { get; set; }

    // ── Reports ──────────────────────────────────────────────────────────────

    /// <summary>Default report format.</summary>
    public ReportFormat DefaultReportFormat { get; set; } = ReportFormat.Html;

    /// <summary>Folder reports are saved to; empty means Documents.</summary>
    public string? ReportsFolder { get; set; }

    /// <summary>Include files with the "ok" status in the report.</summary>
    public bool IncludeOkFilesInReport { get; set; }

    /// <summary>Open the report as soon as it is saved.</summary>
    public bool OpenReportAfterSave { get; set; } = true;

    // ── Appearance ───────────────────────────────────────────────────────────

    /// <summary>Application theme.</summary>
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>
    /// User status colours as "#RRGGBB"; empty means take the theme colour.
    /// </summary>
    public StatusColorOverrides StatusColors { get; set; } = new();

    /// <summary>Row density of the results table.</summary>
    public ListDensity ListDensity { get; set; } = ListDensity.Normal;

    /// <summary>Show full paths; otherwise paths are trimmed from the left.</summary>
    public bool ShowFullPaths { get; set; } = true;

    /// <summary>Use a monospaced font for paths.</summary>
    public bool MonospacePaths { get; set; } = true;

    /// <summary>Show the status bar at the bottom of the window.</summary>
    public bool ShowStatusBar { get; set; } = true;

    /// <summary>Mica backdrop; <see langword="null" /> means never chosen.</summary>
    /// <remarks>
    /// Three states rather than two, and that matters: "never asked" and
    /// "turned off by the user" are different. The first means first run should
    /// look at the Windows effects setting; the second means the decision is
    /// already made. A null is resolved once at startup and written back.
    /// </remarks>
    public bool? MicaEffect { get; set; }

    /// <summary>Animations; <see langword="null" /> means never chosen.</summary>
    /// <remarks>
    /// Same three states as <see cref="MicaEffect" />: first run takes the
    /// Windows animation setting, afterwards the user's choice stands.
    /// </remarks>
    public bool? Animations { get; set; }

    /// <summary>Interface language code; <see langword="null" /> means never chosen.</summary>
    /// <remarks>
    /// Resolved once on first run from the Windows language and region, then
    /// written back; see <see cref="AppLanguage.Resolve" />.
    /// </remarks>
    public string? Language { get; set; }

    // ── Derived values ───────────────────────────────────────────────────────

    /// <summary>
    /// How many files are checked at once. Automatic means one per core,
    /// capped at eight.
    /// </summary>
    [JsonIgnore]
    public int EffectiveParallelism => AutoParallelism
        ? Math.Clamp(Environment.ProcessorCount, 1, 8)
        : Math.Clamp(ManualParallelism, 1, 64);

    /// <summary>Thread cap for a spinning disk.</summary>
    public const int HardDiskParallelism = 2;

    /// <summary>Large-file threshold in bytes; <see langword="null"/> means no limit.</summary>
    [JsonIgnore]
    public long? LargeFileThresholdBytes =>
        LargeFileThresholdMb <= 0 ? null : (long)LargeFileThresholdMb * 1024 * 1024;

    /// <summary>Per-file timeout.</summary>
    [JsonIgnore]
    public TimeSpan FileTimeout => TimeSpan.FromSeconds(Math.Clamp(FileTimeoutSeconds, 1, 3600));

    /// <summary>
    /// Deep copy. The settings window edits a draft and applies it only on
    /// save, so the lists must be copied rather than shared by reference.
    /// </summary>
    public AppSettings Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        RememberLastFolder = RememberLastFolder,
        LastFolder = LastFolder,
        OfferStartAfterFolderSelected = OfferStartAfterFolderSelected,
        ShowFirstRunTip = ShowFirstRunTip,
        SoundOnFinish = SoundOnFinish,
        Recursive = Recursive,
        CheckMetadata = CheckMetadata,
        CheckPlaylists = CheckPlaylists,
        CheckDepth = CheckDepth,
        InspectCollection = InspectCollection,
        TrackChanges = TrackChanges,
        DetectSilence = DetectSilence,
        DetectTranscode = DetectTranscode,
        DetectClipping = DetectClipping,
        VerifyExtensionMatchesContent = VerifyExtensionMatchesContent,
        VerifyContainerIntegrity = VerifyContainerIntegrity,
        LargeFileThresholdMb = LargeFileThresholdMb,
        FileTimeoutSeconds = FileTimeoutSeconds,
        AutoParallelism = AutoParallelism,
        ManualParallelism = ManualParallelism,
        WarnAboutLargeFiles = WarnAboutLargeFiles,
        RespectDriveType = RespectDriveType,
        LockedFileAction = LockedFileAction,
        LockedWaitSeconds = LockedWaitSeconds,
        LockedRetryCount = LockedRetryCount,
        DetectOwnerProcess = DetectOwnerProcess,
        OfferCloseOwner = OfferCloseOwner,
        AllowApplyToAllLocked = AllowApplyToAllLocked,
        DisabledExtensions = [.. DisabledExtensions],
        CustomExtensions = [.. CustomExtensions],
        EnableIsoSacd = EnableIsoSacd,
        DefaultReportFormat = DefaultReportFormat,
        ReportsFolder = ReportsFolder,
        IncludeOkFilesInReport = IncludeOkFilesInReport,
        OpenReportAfterSave = OpenReportAfterSave,
        Theme = Theme,
        StatusColors = StatusColors.Clone(),
        ListDensity = ListDensity,
        ShowFullPaths = ShowFullPaths,
        MicaEffect = MicaEffect,
        Animations = Animations,
        Language = Language,
        MonospacePaths = MonospacePaths,
        ShowStatusBar = ShowStatusBar,
    };
}

/// <summary>User status colours; <see langword="null"/> means the theme colour.</summary>
public sealed class StatusColorOverrides
{
    /// <summary>Colour of the "ok" status.</summary>
    public string? Ok { get; set; }

    /// <summary>Colour of the "corrupted" status.</summary>
    public string? Corrupted { get; set; }

    /// <summary>Colour of the "warning" status.</summary>
    public string? Warning { get; set; }

    /// <summary>Colour of the "skipped" status.</summary>
    public string? Skipped { get; set; }

    /// <summary>Every colour is left at the theme default.</summary>
    public bool IsEmpty => Ok is null && Corrupted is null && Warning is null && Skipped is null;

    /// <summary>Copy of the values.</summary>
    public StatusColorOverrides Clone() => new()
    {
        Ok = Ok,
        Corrupted = Corrupted,
        Warning = Warning,
        Skipped = Skipped,
    };
}
