using MusicScanIntegrity.Core.Models;
using MusicScanIntegrity.Core.Resources;

namespace MusicScanIntegrity.Core.Audio;

/// <summary>Reads the main tags of a file.</summary>
public interface IMetadataReader
{
    /// <summary>
    /// Reads title, artist and album. Returns <see langword="null"/> when tags
    /// could not be read at all — still a metadata problem, not damaged audio.
    /// </summary>
    /// <param name="filePath">Path to the file.</param>
    /// <param name="error">Technical cause when reading failed.</param>
    TrackMetadata? Read(string filePath, out string? error);
}

/// <summary>Tag reading through TagLib#.</summary>
/// <remarks>
/// Missing tags are deliberately kept apart from damaged audio: a great deal
/// of perfectly good music carries no tags at all.
/// </remarks>
public sealed class TagLibMetadataReader : IMetadataReader
{
    /// <inheritdoc />
    public TrackMetadata? Read(string filePath, out string? error)
    {
        error = null;

        try
        {
            using TagLib.File file = TagLib.File.Create(filePath);
            TagLib.Tag tag = file.Tag;

            return new TrackMetadata(
                Normalize(tag.Title),
                Normalize(tag.FirstPerformer ?? tag.FirstAlbumArtist),
                Normalize(tag.Album),
                file.Properties?.Duration.TotalSeconds,
                (int)tag.Track,
                tag.Pictures.Length > 0);
        }
        catch (TagLib.UnsupportedFormatException ex)
        {
            // TagLib cannot read tags for this container; the file is fine.
            error = Common.Format.Text(Strings.Tags_UnsupportedContainer, ex.Message);
            return null;
        }
        catch (TagLib.CorruptFileException ex)
        {
            error = Common.Format.Text(Strings.Tags_Corrupt, ex.Message);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = Common.Format.Text(Strings.Tags_Unreadable, ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            error = $"{ex.GetType().Name} · {ex.Message}";
            return null;
        }
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
