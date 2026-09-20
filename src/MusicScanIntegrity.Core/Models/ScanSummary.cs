using MusicScanIntegrity.Core.Resources;

namespace MusicScanIntegrity.Core.Models;

/// <summary>Summary of a finished or stopped scan; the basis of the report tab.</summary>
public sealed class ScanSummary
{
    /// <summary>Root folder of the scan.</summary>
    public required string RootPath { get; init; }

    /// <summary>Final tallies.</summary>
    public required ScanCounters Counters { get; init; }

    /// <summary>When the scan started.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>How long the scan took.</summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>How many threads were actually used.</summary>
    public required int Parallelism { get; init; }

    /// <summary>How deeply files were read.</summary>
    /// <remarks>
    /// Goes into the report: without it a quick scan and a full one produce
    /// identical-looking reports that stand for very different claims.
    /// </remarks>
    public Settings.CheckDepth Depth { get; init; } = Settings.CheckDepth.Sampled;

    /// <summary>Whether container checksums were verified.</summary>
    public bool ContainerIntegrityChecked { get; init; } = true;

    /// <summary>One-line description of how the scan was performed.</summary>
    public string DepthLabel
    {
        get
        {
            string depth = Depth switch
            {
                Settings.CheckDepth.Quick => Strings.Depth_Quick,
                Settings.CheckDepth.Full => Strings.Depth_Full,
                _ => Strings.Depth_Sampled,
            };

            return ContainerIntegrityChecked
                ? Common.Format.Text(Strings.Depth_ChecksumsVerified, depth)
                : Common.Format.Text(Strings.Depth_ChecksumsNotVerified, depth);
        }
    }

    /// <summary>The user interrupted the scan.</summary>
    public bool WasStopped { get; init; }

    /// <summary>Scan aborted by a critical failure; text for the user.</summary>
    public string? CriticalFailure { get; init; }

    /// <summary>Per-folder breakdown shown on the report tab.</summary>
    public IReadOnlyList<FolderStat> Folders { get; init; } = [];

    /// <summary>How many playlists were checked.</summary>
    public int PlaylistCount { get; init; }

    /// <summary>How many paths inside playlists were missing.</summary>
    public int PlaylistMissingLinks { get; init; }

    /// <summary>Folders the OS refused during the walk.</summary>
    public IReadOnlyList<InaccessibleFolder> InaccessibleFolders { get; init; } = [];
}

/// <summary>Per-folder statistics.</summary>
/// <param name="BadCount">How many of the checked files are corrupted.</param>
public sealed record FolderStat(string Path, int FileCount, int BadCount);
