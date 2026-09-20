using MusicScanIntegrity.Core.Models;

namespace MusicScanIntegrity.Core.Scanning;

/// <summary>Scan state.</summary>
public enum ScanState
{
    /// <summary>Never started.</summary>
    Idle,

    /// <summary>Walking the folder.</summary>
    Discovering,

    /// <summary>Checking files.</summary>
    Running,

    /// <summary>Paused: files in flight finish, no new ones are taken.</summary>
    Paused,

    /// <summary>Finished.</summary>
    Completed,

    /// <summary>Stopped by the user.</summary>
    Stopped,

    /// <summary>Aborted by a critical decoder failure.</summary>
    Failed,
}

/// <summary>Snapshot of scan progress for the UI.</summary>
/// <param name="CurrentFile">File being checked right now.</param>
/// <param name="Parallelism">How many files are checked at once.</param>
/// <param name="PendingLockedQuestions">Locked-file questions awaiting an answer.</param>
public readonly record struct ScanProgress(
    ScanState State,
    ScanCounters Counters,
    string? CurrentFile,
    TimeSpan Elapsed,
    int Parallelism,
    int PendingLockedQuestions);

/// <summary>
/// A warning surfaced while the scan runs rather than only at the end.
/// </summary>
/// <param name="Title">Human wording.</param>
public sealed record LiveWarning(string Title, string Path, IssueCode Code);
