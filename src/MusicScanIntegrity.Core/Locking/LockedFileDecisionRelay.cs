namespace MusicScanIntegrity.Core.Locking;

/// <summary>
/// Relay between the scan engine and whoever actually asks the user.
/// </summary>
/// <remarks>
/// The engine is constructed before the main window, but the window is what
/// asks — a direct dependency would be a cycle in the container. The relay
/// breaks it: the engine takes the relay, the window installs itself as
/// <see cref="Target"/> at startup. With no target (batch mode, tests) locked
/// files are skipped.
/// </remarks>
public sealed class LockedFileDecisionRelay : ILockedFileDecisionProvider
{
    /// <summary>Who answers questions; <see langword="null"/> means nobody.</summary>
    public ILockedFileDecisionProvider? Target { get; set; }

    /// <inheritdoc />
    public Task<LockedFileDecision> AskAsync(LockedFileQuestion question, CancellationToken cancellationToken) =>
        Target is { } target
            ? target.AskAsync(question, cancellationToken)
            : Task.FromResult(LockedFileDecision.Skip);
}
