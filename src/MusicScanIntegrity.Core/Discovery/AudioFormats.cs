using MusicScanIntegrity.Core.Models;
using MusicScanIntegrity.Core.Settings;

namespace MusicScanIntegrity.Core.Discovery;

/// <summary>
/// Which extensions count as audio, playlists and disc images.
/// </summary>
public static class AudioFormats
{
    /// <summary>Ordinary audio formats.</summary>
    public static readonly IReadOnlySet<string> Audio = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".wav", ".aiff", ".aif", ".aifc",
        ".flac", ".alac", ".m4a", ".ape", ".wv",
        ".mp3", ".aac", ".ogg", ".oga", ".opus", ".wma",
    };

    /// <summary>Tracker and sequencer formats.</summary>
    public static readonly IReadOnlySet<string> Tracker = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mid", ".midi", ".mod", ".xm", ".it", ".s3m", ".mtm", ".umx",
    };

    /// <summary>DSD formats.</summary>
    public static readonly IReadOnlySet<string> Dsd = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".dsf", ".dff",
    };

    /// <summary>Playlists.</summary>
    public static readonly IReadOnlySet<string> Playlists = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".m3u", ".m3u8", ".pls", ".cue",
    };

    /// <summary>
    /// Disc images. Kept apart from audio because an ordinary .iso is a disc
    /// image, not music.
    /// </summary>
    public static readonly IReadOnlySet<string> DiscImages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".iso",
    };

    /// <summary>Every audio extension supported out of the box.</summary>
    public static IReadOnlySet<string> AllAudio { get; } =
        new HashSet<string>(Audio.Concat(Tracker).Concat(Dsd), StringComparer.OrdinalIgnoreCase);

    /// <summary>Display name for an extension: ".flac" becomes "FLAC".</summary>
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
    /// Builds the extension set for the current settings: the base list minus
    /// the disabled ones, plus user extensions, plus ISO when the experimental
    /// support is on.
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

        // Playlists are walked only when the user enabled checking them.
        HashSet<string> playlists = settings.CheckPlaylists
            ? new HashSet<string>(Playlists, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return new FormatSelection(audio, playlists, discImages);
    }
}

/// <summary>The extension set that applies to a particular configuration.</summary>
public sealed record FormatSelection(
    IReadOnlySet<string> Audio,
    IReadOnlySet<string> Playlists,
    IReadOnlySet<string> DiscImages)
{
    /// <summary>Classifies a file by its extension.</summary>
    /// <returns><see langword="null"/> when the file fits no category.</returns>
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
