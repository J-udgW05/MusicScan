namespace MusicScanIntegrity.Core.Models;

/// <summary>
/// Final status of one checked file. There are exactly four.
/// </summary>
/// <remarks>
/// There is deliberately no separate "error" status. A missing file, a denied
/// read and a decoder failure all land in <see cref="Corrupted"/>; the actual
/// cause is carried by <see cref="CheckIssue"/> and always shown, so that
/// "file not found" does not read as "broken audio". Adding a fifth value
/// would split the four status colours the whole UI is built on.
/// </remarks>
public enum CheckStatus
{
    /// <summary>Plays back, nothing to report.</summary>
    Ok = 0,

    /// <summary>Plays back, but something is off: tags, timeout, extension, lock.</summary>
    Warning = 1,

    /// <summary>Could not be checked: undecodable, missing or inaccessible.</summary>
    Corrupted = 2,

    /// <summary>Not checked, by the user's decision.</summary>
    Skipped = 3,
}
