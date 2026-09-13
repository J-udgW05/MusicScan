namespace MusicScanIntegrity.Core.Models;

/// <summary>
/// Итог быстрого обхода папки: что нашлось, сколько и где не хватило прав.
/// Нужен, чтобы сразу предложить пользователю начать проверку
/// (02_ARCHITECTURE.md, раздел 9) — без декодирования файлов.
/// </summary>
public sealed class DiscoveryResult
{
    /// <summary>Аудиофайлы и образы дисков, которые будут декодироваться.</summary>
    public required IReadOnlyList<ScanItem> AudioItems { get; init; }

    /// <summary>Найденные плейлисты.</summary>
    public required IReadOnlyList<ScanItem> Playlists { get; init; }

    /// <summary>Сколько папок обошли.</summary>
    public required int FolderCount { get; init; }

    /// <summary>Папки, в которые не пустила операционная система, — показываются честно.</summary>
    public required IReadOnlyList<InaccessibleFolder> InaccessibleFolders { get; init; }

    /// <summary>Корневая папка обхода.</summary>
    public required string RootPath { get; init; }

    /// <summary>Сколько времени занял обход.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Всего объектов к проверке.</summary>
    public int TotalCount => AudioItems.Count + Playlists.Count;

    /// <summary>Пустой результат — удобно как значение по умолчанию.</summary>
    public static DiscoveryResult Empty(string rootPath) => new()
    {
        AudioItems = [],
        Playlists = [],
        FolderCount = 0,
        InaccessibleFolders = [],
        RootPath = rootPath,
    };
}

/// <summary>Папка, которую не удалось прочитать, и техническая причина.</summary>
/// <param name="Path">Путь к папке.</param>
/// <param name="Reason">Причина в человеческом виде.</param>
/// <param name="TechnicalDetail">Техническая причина (тип исключения, код Windows).</param>
public sealed record InaccessibleFolder(string Path, string Reason, string? TechnicalDetail);
