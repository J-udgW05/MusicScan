namespace MusicScanIntegrity.Core.Models;

/// <summary>
/// The concrete reason a file ended up with its status.
/// </summary>
public enum IssueCode
{
    /// <summary>Nothing to report.</summary>
    None = 0,

    /// <summary>File disappeared between the walk and the check.</summary>
    FileNotFound,

    /// <summary>File exists, but the OS refused to read it.</summary>
    AccessDenied,

    /// <summary>Locked by another process; the user chose to skip it.</summary>
    LockedSkippedByUser,

    /// <summary>Locked by another process; the wait timed out.</summary>
    LockedWaitTimeout,

    /// <summary>Was locked; checked through a temporary copy.</summary>
    LockedCheckedViaCopy,

    /// <summary>Could not make a temporary copy of the locked file.</summary>
    LockedCopyFailed,

    /// <summary>Password- or DRM-protected; happens with some WMA and AAC.</summary>
    PasswordProtected,

    /// <summary>Decoding could not be started at all.</summary>
    DecodeStartFailed,

    /// <summary>Decoding started, but reading the audio data failed.</summary>
    AudioReadFailed,

    /// <summary>Empty, or shorter than valid audio could possibly be.</summary>
    EmptyFile,

    /// <summary>Checking this one file exceeded the allowed timeout.</summary>
    CheckTimeout,

    /// <summary>Extension does not match the contents, but it is still audio.</summary>
    ExtensionMismatch,

    /// <summary>Tag checking is on and tags are missing or unreadable.</summary>
    MetadataProblem,

    /// <summary>Larger than the configured large-file threshold.</summary>
    LargeFile,

    /// <summary>A file referenced by a playlist is missing from disk.</summary>
    PlaylistTargetMissing,

    /// <summary>Cue-sheet track marks fall past the end of the file.</summary>
    CueMarksBeyondFile,

    /// <summary>The playlist file itself could not be parsed.</summary>
    PlaylistUnreadable,

    /// <summary>A checksum inside the format did not match; the file is damaged.</summary>
    ChecksumMismatch,

    /// <summary>Structure is broken: frames, pages or blocks do not line up.</summary>
    ContainerDamaged,

    /// <summary>Truncated: less data than its own header promises.</summary>
    Truncated,

    /// <summary>Decodes, but carries no sound at all.</summary>
    DigitalSilence,

    /// <summary>A silent gap inside an otherwise audible track; looks like lost data.</summary>
    AudioDropout,

    /// <summary>A noticeable share of samples hits the top of the scale.</summary>
    Clipping,

    /// <summary>The recording carries a DC component; a sign of poor digitisation.</summary>
    DcOffset,

    /// <summary>Contents changed while size and date stayed the same.</summary>
    SilentCorruption,

    /// <summary>Tag text was written in the wrong encoding; reads as mojibake.</summary>
    BrokenTagText,

    /// <summary>Looks re-encoded from a lossy source.</summary>
    TranscodeSuspected,

    /// <summary>Unexpected error while checking this particular file.</summary>
    UnexpectedError,
}
