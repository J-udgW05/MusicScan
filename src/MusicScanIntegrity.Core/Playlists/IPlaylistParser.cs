namespace MusicScanIntegrity.Core.Playlists;

/// <summary>
/// Parses one playlist format into a list of paths. The formats differ in
/// structure but produce the same result.
/// </summary>
public interface IPlaylistParser
{
    /// <summary>Extensions this parser handles, in the ".m3u" form.</summary>
    IReadOnlySet<string> Extensions { get; }

    /// <summary>
    /// Extracts audio paths from the playlist text, exactly as written, with
    /// no resolution applied.
    /// </summary>
    /// <param name="content">Playlist text.</param>
    IReadOnlyList<string> Parse(string content);
}
