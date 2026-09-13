namespace MusicScanIntegrity.Core.Playlists;

/// <summary>
/// CUE — текстовые директивы. Путь к аудио задаёт строка
/// <c>FILE "имя.flac" WAVE</c>; дорожки (TRACK/INDEX) описывают позиции
/// внутри того же файла и отдельными путями не являются.
/// </summary>
public sealed class CuePlaylistParser : IPlaylistParser
{
    /// <inheritdoc />
    public IReadOnlySet<string> Extensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".cue" };

    /// <inheritdoc />
    public IReadOnlyList<string> Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        List<string> paths = [];
        foreach (string rawLine in content.Split('\n'))
        {
            string line = rawLine.Trim().Trim('﻿');
            if (!line.StartsWith("FILE", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? path = ExtractFilePath(line);
            if (!string.IsNullOrWhiteSpace(path))
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    /// <summary>
    /// Достаёт метки дорожек: номер и время начала в секундах.
    /// </summary>
    /// <param name="content">Текст cue-листа.</param>
    /// <returns>Метки в порядке появления.</returns>
    /// <remarks>
    /// Время записано как «минуты:секунды:кадры», где кадр — одна
    /// семьдесятпятая секунды: так размечают компакт-диски.
    /// </remarks>
    public static IReadOnlyList<CueMark> ParseMarks(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        List<CueMark> marks = [];
        int track = 0;

        foreach (string rawLine in content.Split('\n'))
        {
            string line = rawLine.Trim().Trim('\uFEFF');

            if (line.StartsWith("TRACK", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && int.TryParse(parts[1], out int number))
                {
                    track = number;
                }

                continue;
            }

            if (!line.StartsWith("INDEX", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string[] fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length >= 3 && TryParseTime(fields[2], out double seconds))
            {
                marks.Add(new CueMark(track, seconds));
            }
        }

        return marks;
    }

    /// <summary>Разбирает время вида «12:34:56».</summary>
    private static bool TryParseTime(string value, out double seconds)
    {
        seconds = 0;
        string[] parts = value.Split(':');

        if (parts.Length != 3
            || !int.TryParse(parts[0], out int minutes)
            || !int.TryParse(parts[1], out int wholeSeconds)
            || !int.TryParse(parts[2], out int frames))
        {
            return false;
        }

        seconds = (minutes * 60) + wholeSeconds + (frames / 75.0);
        return true;
    }

    /// <summary>
    /// Достаёт имя файла из строки FILE. Имя обычно в кавычках, но встречаются
    /// файлы без кавычек — тогда путём считается всё до последнего слова-типа
    /// (WAVE, MP3, BINARY, AIFF, MOTOROLA).
    /// </summary>
    private static string? ExtractFilePath(string line)
    {
        int firstQuote = line.IndexOf('"');
        if (firstQuote >= 0)
        {
            int lastQuote = line.LastIndexOf('"');
            return lastQuote > firstQuote ? line[(firstQuote + 1)..lastQuote] : null;
        }

        string rest = line[4..].Trim();
        if (rest.Length == 0)
        {
            return null;
        }

        int lastSpace = rest.LastIndexOf(' ');
        return lastSpace > 0 ? rest[..lastSpace].Trim() : rest;
    }
}

/// <summary>Метка дорожки внутри cue-листа.</summary>
/// <param name="Track">Номер дорожки.</param>
/// <param name="Seconds">Время начала от начала файла.</param>
public readonly record struct CueMark(int Track, double Seconds);
