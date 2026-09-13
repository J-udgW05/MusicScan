namespace MusicScanIntegrity.Core.Playlists;

/// <summary>
/// Разбор одного формата плейлиста в список путей.
/// Форматы отличаются устройством, результат один — список путей
/// (02_ARCHITECTURE.md, раздел 6).
/// </summary>
public interface IPlaylistParser
{
    /// <summary>Расширения, которые понимает этот разборщик (в виде «.m3u»).</summary>
    IReadOnlySet<string> Extensions { get; }

    /// <summary>
    /// Извлекает пути к аудиофайлам из содержимого плейлиста.
    /// Пути возвращаются ровно так, как записаны в файле — без разворачивания.
    /// </summary>
    /// <param name="content">Текст плейлиста.</param>
    IReadOnlyList<string> Parse(string content);
}
