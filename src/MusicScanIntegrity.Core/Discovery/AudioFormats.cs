using MusicScanIntegrity.Core.Models;
using MusicScanIntegrity.Core.Settings;

namespace MusicScanIntegrity.Core.Discovery;

/// <summary>
/// Какие расширения программа считает аудио, плейлистами и образами дисков.
/// Список зафиксирован в 01_SPECIFICATION.md, раздел 4.
/// </summary>
public static class AudioFormats
{
    /// <summary>Обычные аудиоформаты.</summary>
    public static readonly IReadOnlySet<string> Audio = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".wav", ".aiff", ".aif", ".aifc",
        ".flac", ".alac", ".m4a", ".ape", ".wv",
        ".mp3", ".aac", ".ogg", ".oga", ".opus", ".wma",
    };

    /// <summary>Трекерные и секвенсорные форматы.</summary>
    public static readonly IReadOnlySet<string> Tracker = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mid", ".midi", ".mod", ".xm", ".it", ".s3m", ".mtm", ".umx",
    };

    /// <summary>Форматы DSD.</summary>
    public static readonly IReadOnlySet<string> Dsd = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".dsf", ".dff",
    };

    /// <summary>Плейлисты.</summary>
    public static readonly IReadOnlySet<string> Playlists = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".m3u", ".m3u8", ".pls", ".cue",
    };

    /// <summary>
    /// Образы дисков. Отдельно от аудио: обычный .iso — это образ диска,
    /// а не музыка (03_IMPLEMENTATION_GUIDE.md, раздел 2).
    /// </summary>
    public static readonly IReadOnlySet<string> DiscImages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".iso",
    };

    /// <summary>Все аудиорасширения, поддерживаемые из коробки.</summary>
    public static IReadOnlySet<string> AllAudio { get; } =
        new HashSet<string>(Audio.Concat(Tracker).Concat(Dsd), StringComparer.OrdinalIgnoreCase);

    /// <summary>Человеческое название формата по расширению: «.flac» → «FLAC».</summary>
    public static string DisplayName(string extension)
    {
        string ext = extension.TrimStart('.').ToUpperInvariant();
        return ext switch
        {
            "MID" or "MIDI" => "MIDI",
            "AIF" or "AIFC" => "AIFF",
            "OGA" => "OGG",
            "M4A" => "M4A",
            "" => "—",
            _ => ext,
        };
    }

    /// <summary>
    /// Строит набор расширений, актуальный для текущих настроек:
    /// базовый список минус выключенные плюс пользовательские, плюс ISO,
    /// если включена экспериментальная поддержка.
    /// </summary>
    public static FormatSelection ForSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        HashSet<string> audio = new(AllAudio, StringComparer.OrdinalIgnoreCase);
        foreach (string custom in settings.CustomExtensions)
        {
            audio.Add(custom);
        }

        foreach (string disabled in settings.DisabledExtensions)
        {
            audio.Remove(disabled);
        }

        HashSet<string> discImages = settings.EnableIsoSacd
            ? new HashSet<string>(DiscImages, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Плейлисты обходим только если пользователь включил их проверку.
        HashSet<string> playlists = settings.CheckPlaylists
            ? new HashSet<string>(Playlists, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return new FormatSelection(audio, playlists, discImages);
    }
}

/// <summary>Набор расширений, актуальный для конкретных настроек.</summary>
/// <param name="Audio">Аудиорасширения.</param>
/// <param name="Playlists">Расширения плейлистов.</param>
/// <param name="DiscImages">Расширения образов дисков.</param>
public sealed record FormatSelection(
    IReadOnlySet<string> Audio,
    IReadOnlySet<string> Playlists,
    IReadOnlySet<string> DiscImages)
{
    /// <summary>Определяет, что это за файл, по его расширению.</summary>
    /// <returns><see langword="null"/>, если файл не подходит ни под одну категорию.</returns>
    public ScanItemKind? Classify(string path)
    {
        string extension = Path.GetExtension(path);
        if (extension.Length == 0)
        {
            return null;
        }

        if (Audio.Contains(extension))
        {
            return ScanItemKind.Audio;
        }

        if (Playlists.Contains(extension))
        {
            return ScanItemKind.Playlist;
        }

        if (DiscImages.Contains(extension))
        {
            return ScanItemKind.DiscImage;
        }

        return null;
    }
}
