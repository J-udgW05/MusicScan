namespace MusicScanIntegrity.Core.Scanning;

/// <summary>
/// A pause that does not abort checks already in flight.
/// </summary>
/// <remarks>
/// Files being checked run to a result; no new ones are taken from the queue
/// until the scan is resumed.
/// </remarks>
public sealed class PauseTokenSource : IDisposable
{
    // A set event means "carry on"; a reset one means "paused".
    private readonly ManualResetEventSlim _gate = new(initialState: true);

    /// <summary>The scan is currently paused.</summary>
    public bool IsPaused => !_gate.IsSet;

    /// <summary>Pauses.</summary>
    public void Pause() => _gate.Reset();

    /// <summary>Resumes.</summary>
    public void Resume() => _gate.Set();

    /// <summary>
    /// Waits for the pause to lift; called before taking the next file.
    /// </summary>
    public async Task WaitWhilePausedAsync(CancellationToken cancellationToken)
    {
        if (_gate.IsSet)
        {
            return;
        }

        // Wait on the thread pool so a scan thread is not blocked.
        await Task.Run(() => _gate.Wait(cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();
}
