namespace MusicScanIntegrity.Core.Playlists;

/// <summary>
/// PLS is an INI-like format with a [playlist] section and "File1=path" keys.
/// Only the FileN keys matter; Title and Length describe the track.
/// </summary>
public sealed class PlsPlaylistParser : IPlaylistParser
{
    /// <inheritdoc />
    public IReadOnlySet<string> Extensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pls" };

    /// <inheritdoc />
    public IReadOnlyList<string> Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        // Preserve file order: File1, File2, … may be interleaved with Title.
        List<(int Index, string Path)> entries = [];
        int fallbackIndex = int.MaxValue / 2;

        foreach (string rawLine in content.Split('\n'))
        {
            string line = rawLine.Trim().Trim('﻿');
            if (line.Length == 0 || line.StartsWith('[') || line.StartsWith(';'))
            {
                continue;
            }

            int separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            string key = line[..separator].Trim();
            if (!key.StartsWith("File", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string value = line[(separator + 1)..].Trim();
            if (value.Length == 0)
            {
                continue;
            }

            // "File12" → 12; entries without a number go last, in order.
            int index = int.TryParse(key.AsSpan(4), out int parsed) ? parsed : fallbackIndex++;
            entries.Add((index, value));
        }

        return [.. entries.OrderBy(e => e.Index).Select(e => e.Path)];
    }
}
