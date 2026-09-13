namespace MusicScanIntegrity.Core.Playlists;

/// <summary>
/// PLS — INI-подобный формат с секцией [playlist] и ключами вида «File1=путь».
/// Интересуют только ключи FileN; Title и Length описывают трек, а не путь.
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

        // Сохраняем порядок из файла: File1, File2, … могут идти вперемешку с Title.
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

            // «File12» → 12; если номера нет, ставим запись в конец в порядке появления.
            int index = int.TryParse(key.AsSpan(4), out int parsed) ? parsed : fallbackIndex++;
            entries.Add((index, value));
        }

        return [.. entries.OrderBy(e => e.Index).Select(e => e.Path)];
    }
}
