using MusicScanIntegrity.Core.Resources;

namespace MusicScanIntegrity.Core.Models;

/// <summary>What a collection-level finding is about.</summary>
public enum CollectionFindingKind
{
    /// <summary>Track numbering in the folder has gaps.</summary>
    MissingTracks,

    /// <summary>One folder mixes several audio formats.</summary>
    MixedFormats,

    /// <summary>No file in the folder carries cover art.</summary>
    NoCover,

    /// <summary>One folder mixes several album tag values.</summary>
    MixedAlbums,

    /// <summary>The same track exists in more than one place.</summary>
    Duplicate,
}

/// <summary>
/// A finding about a folder or the collection as a whole rather than a file.
/// </summary>
/// <remarks>
/// Kept separate from <see cref="CheckIssue" /> on the merits: a gap in track
/// numbering is a property of the album, not of any single file in it, and
/// pinning it to an arbitrary file would be untrue.
/// </remarks>
public sealed record CollectionFinding(
    CollectionFindingKind Kind,
    string Path,
    string Message,
    string? Detail = null)
{
    /// <summary>Short caption for the kind of finding.</summary>
    public string KindLabel => Kind switch
    {
        CollectionFindingKind.MissingTracks => Strings.Finding_MissingTracks,
        CollectionFindingKind.MixedFormats => Strings.Finding_MixedFormats,
        CollectionFindingKind.NoCover => Strings.Finding_NoCover,
        CollectionFindingKind.MixedAlbums => Strings.Finding_MixedAlbums,
        CollectionFindingKind.Duplicate => Strings.Finding_Duplicate,
        _ => Strings.Finding_Other,
    };
}
