namespace MusicScanIntegrity.Core.Models;

/// <summary>
/// Outcome of the fast folder walk: what was found, how much of it, and where
/// access was denied. Produced without decoding anything so the app can offer
/// to start the scan straight away.
/// </summary>
public sealed class DiscoveryResult
{
    /// <summary>Audio files and disc images that will be decoded.</summary>
    public required IReadOnlyList<ScanItem> AudioItems { get; init; }

    /// <summary>Playlists found along the way.</summary>
    public required IReadOnlyList<ScanItem> Playlists { get; init; }

    /// <summary>How many folders were walked.</summary>
    public required int FolderCount { get; init; }

    /// <summary>Folders the OS refused; reported rather than silently skipped.</summary>
    public required IReadOnlyList<InaccessibleFolder> InaccessibleFolders { get; init; }

    /// <summary>Root of the walk.</summary>
    public required string RootPath { get; init; }

    /// <summary>How long the walk took.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Total items queued for checking.</summary>
    public int TotalCount => AudioItems.Count + Playlists.Count;

    /// <summary>Empty result, handy as a default value.</summary>
    public static DiscoveryResult Empty(string rootPath) => new()
    {
        AudioItems = [],
        Playlists = [],
        FolderCount = 0,
        InaccessibleFolders = [],
        RootPath = rootPath,
    };
}

/// <summary>A folder that could not be read, with the reason.</summary>
/// <param name="Reason">Human-readable cause.</param>
/// <param name="TechnicalDetail">Exception type or Windows error code.</param>
public sealed record InaccessibleFolder(string Path, string Reason, string? TechnicalDetail);
