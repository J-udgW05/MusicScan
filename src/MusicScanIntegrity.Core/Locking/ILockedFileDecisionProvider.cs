using MusicScanIntegrity.Core.Settings;

namespace MusicScanIntegrity.Core.Locking;

/// <summary>A question to the user about a locked file.</summary>
/// <param name="SizeBytes">File size, so the copy prompt can state how much will be copied.</param>
/// <param name="Owner">Who holds the file, or an honest "could not determine".</param>
/// <param name="QueuedAfterThis">How many more questions are waiting.</param>
public sealed record LockedFileQuestion(
    string FilePath,
    long SizeBytes,
    LockOwnerResult Owner,
    int QueuedAfterThis);

/// <summary>The user's answer.</summary>
/// <param name="ApplyToAll">Apply the same action to every other locked file.</param>
/// <param name="StopScan">The user asked to stop the whole scan.</param>
public sealed record LockedFileDecision(LockedFileAction Action, bool ApplyToAll = false, bool StopScan = false)
{
    /// <summary>Default answer when there is nobody to ask, for example in tests.</summary>
    public static LockedFileDecision Skip { get; } = new(LockedFileAction.Skip);
}

/// <summary>
/// Asks the user about a locked file. Implementations must present questions
/// one at a time rather than opening a stack of windows.
/// </summary>
public interface ILockedFileDecisionProvider
{
    /// <summary>Asks and waits for the answer.</summary>
    Task<LockedFileDecision> AskAsync(LockedFileQuestion question, CancellationToken cancellationToken);
}

/// <summary>
/// Stub for when there is nobody to ask (tests, batch mode): the file is
/// simply skipped.
/// </summary>
public sealed class AlwaysSkipDecisionProvider : ILockedFileDecisionProvider
{
    /// <inheritdoc />
    public Task<LockedFileDecision> AskAsync(LockedFileQuestion question, CancellationToken cancellationToken) =>
        Task.FromResult(LockedFileDecision.Skip);
}
