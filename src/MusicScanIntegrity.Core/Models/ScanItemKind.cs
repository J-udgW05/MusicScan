namespace MusicScanIntegrity.Core.Models;

/// <summary>Что именно нашёл обход папки.</summary>
public enum ScanItemKind
{
    /// <summary>Обычный аудиофайл — его нужно декодировать.</summary>
    Audio,

    /// <summary>Плейлист — у него проверяются только пути внутри.</summary>
    Playlist,

    /// <summary>Образ диска (ISO/SACD) — экспериментальная поддержка.</summary>
    DiscImage,
}
