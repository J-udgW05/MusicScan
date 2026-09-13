namespace MusicScanIntegrity.Core.Playlists;

/// <summary>
/// M3U и M3U8 — простой построчный список путей.
/// Строки, начинающиеся с «#», — служебные директивы (#EXTM3U, #EXTINF) и не пути.
/// </summary>
public sealed class M3uPlaylistParser : IPlaylistParser
{
    /// <inheritdoc />
    public IReadOnlySet<string> Extensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".m3u", ".m3u8" };

    /// <inheritdoc />
    public IReadOnlyList<string> Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        List<string> paths = [];
        foreach (string rawLine in content.Split('\n'))
        {
            string line = rawLine.Trim().Trim('﻿');
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            paths.Add(line);
        }

        return paths;
    }
}
