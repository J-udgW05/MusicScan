namespace MusicScanIntegrity.Core.Models;

/// <summary>
/// Result of checking one playlist. Only the existence of the referenced paths
/// is verified — decoding is already covered by the ordinary scan.
/// </summary>
public sealed class PlaylistCheckResult
{
    /// <summary>Path to the playlist file.</summary>
    public required string FullPath { get; init; }

    /// <summary>File name of the playlist.</summary>
    public string FileName => Path.GetFileName(FullPath);

    /// <summary>Every entry in the playlist.</summary>
    public required IReadOnlyList<PlaylistEntry> Entries { get; init; }

    /// <summary>The playlist file itself could not be parsed.</summary>
    public CheckIssue? ParseIssue { get; init; }

    /// <summary>Issue with the contents, e.g. cue marks past the end of the file.</summary>
    public CheckIssue? ContentIssue { get; init; }

    /// <summary>How many referenced paths are missing from disk.</summary>
    public int MissingCount => Entries.Count(e => !e.Exists);

    /// <summary>Final status of the playlist.</summary>
    public CheckStatus Status =>
        ParseIssue is not null ? ParseIssue.Severity
        : ContentIssue is not null ? ContentIssue.Severity
        : MissingCount > 0 ? CheckStatus.Warning
        : CheckStatus.Ok;
}

/// <summary>One entry inside a playlist.</summary>
/// <param name="RawPath">The path exactly as written in the playlist.</param>
/// <param name="ResolvedPath">Absolute path; relative ones resolve against the playlist folder.</param>
/// <param name="KnownStatus">
/// Status from the main scan when this file was already checked, so the same
/// file cannot end up with two different results.
/// </param>
public sealed record PlaylistEntry(string RawPath, string? ResolvedPath, bool Exists, CheckStatus? KnownStatus = null);
