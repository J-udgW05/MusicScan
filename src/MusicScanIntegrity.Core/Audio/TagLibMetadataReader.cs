using MusicScanIntegrity.Core.Models;

namespace MusicScanIntegrity.Core.Audio;

/// <summary>Чтение основных тегов файла.</summary>
public interface IMetadataReader
{
    /// <summary>
    /// Читает название, исполнителя и альбом.
    /// Возвращает <see langword="null"/>, если теги прочитать не удалось вообще, —
    /// это тоже «проблемы с метаданными», а не повреждение аудио.
    /// </summary>
    /// <param name="filePath">Путь к файлу.</param>
    /// <param name="error">Техническая причина, если чтение не удалось.</param>
    TrackMetadata? Read(string filePath, out string? error);
}

/// <summary>Чтение тегов через TagLib#.</summary>
/// <remarks>
/// Отсутствие тегов сознательно не смешивается с повреждением аудио: огромное
/// количество нормальной музыки хранится вообще без тегов
/// (03_IMPLEMENTATION_GUIDE.md, раздел 2).
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
            // Теги для этого контейнера TagLib читать не умеет — это не поломка файла.
            error = $"TagLib не поддерживает контейнер: {ex.Message}";
            return null;
        }
        catch (TagLib.CorruptFileException ex)
        {
            error = $"Теги повреждены: {ex.Message}";
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = $"Файл недоступен для чтения тегов: {ex.Message}";
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
