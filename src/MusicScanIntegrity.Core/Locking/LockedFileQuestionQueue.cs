using MusicScanIntegrity.Core.Settings;

namespace MusicScanIntegrity.Core.Locking;

/// <summary>
/// Queues locked-file questions and asks them one at a time.
/// </summary>
/// <remarks>
/// The scan runs in parallel, so several files can be locked at once, and a
/// stack of windows leaves the user unable to tell which belongs to which file.
/// The "apply to all" flag is handled here too: after it, the rest are decided
/// without asking.
/// </remarks>
public sealed class LockedFileQuestionQueue(ILockedFileDecisionProvider inner) : IDisposable
{
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);
    private readonly Lock _stateLock = new();

    private LockedFileAction? _appliedToAll;
    private int _waiting;
    private bool _stopRequested;

    /// <summary>The user asked to stop the scan from the prompt.</summary>
    public bool StopRequested
    {
        get
        {
            lock (_stateLock)
            {
                return _stopRequested;
            }
        }
    }

    /// <summary>How many questions are currently waiting.</summary>
    public int Waiting => Volatile.Read(ref _waiting);

    /// <summary>
    /// Asks the user, or returns the earlier "apply to all" answer.
    /// </summary>
    public async Task<LockedFileDecision> AskAsync(
        string filePath,
        long sizeBytes,
        LockOwnerResult owner,
        CancellationToken cancellationToken)
    {
        lock (_stateLock)
        {
            if (_stopRequested)
            {
                return new LockedFileDecision(LockedFileAction.Skip, StopScan: true);
            }

            if (_appliedToAll is { } applied)
            {
                return new LockedFileDecision(applied, ApplyToAll: true);
            }
        }

        Interlocked.Increment(ref _waiting);
        try
        {
            await _oneAtATime.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Interlocked.Decrement(ref _waiting);
            throw;
        }

        try
        {
            // While this file waited the user may have answered "apply to
            // all" or stopped the scan; check again.
            lock (_stateLock)
            {
                if (_stopRequested)
                {
                    return new LockedFileDecision(LockedFileAction.Skip, StopScan: true);
                }

                if (_appliedToAll is { } applied)
                {
                    return new LockedFileDecision(applied, ApplyToAll: true);
                }
            }

            // This question is not counted among those still queued.
            int queuedAfterThis = Math.Max(0, Waiting - 1);
            LockedFileQuestion question = new(filePath, sizeBytes, owner, queuedAfterThis);
            LockedFileDecision decision = await inner.AskAsync(question, cancellationToken).ConfigureAwait(false);

            lock (_stateLock)
            {
                if (decision.StopScan)
                {
                    _stopRequested = true;
                }
                else if (decision.ApplyToAll)
                {
                    _appliedToAll = decision.Action;
                }
            }

            return decision;
        }
        finally
        {
            Interlocked.Decrement(ref _waiting);
            _oneAtATime.Release();
        }
    }

    /// <summary>Resets state before a new scan.</summary>
    public void Reset()
    {
        lock (_stateLock)
        {
            _appliedToAll = null;
            _stopRequested = false;
        }
    }

    /// <inheritdoc />
    public void Dispose() => _oneAtATime.Dispose();
}
