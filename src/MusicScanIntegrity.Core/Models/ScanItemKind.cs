namespace MusicScanIntegrity.Core.Models;

/// <summary>What the folder walk turned up.</summary>
public enum ScanItemKind
{
    /// <summary>Plain audio file, to be decoded.</summary>
    Audio,

    /// <summary>Playlist; only the paths inside it are checked.</summary>
    Playlist,

    /// <summary>Disc image (ISO/SACD); experimental support.</summary>
    DiscImage,
}
