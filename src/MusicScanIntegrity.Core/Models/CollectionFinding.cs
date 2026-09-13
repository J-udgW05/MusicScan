namespace MusicScanIntegrity.Core.Models;

/// <summary>О чём говорит замечание по коллекции.</summary>
public enum CollectionFindingKind
{
    /// <summary>В нумерации дорожек папки есть пропуски.</summary>
    MissingTracks,

    /// <summary>В одной папке файлы разных форматов.</summary>
    MixedFormats,

    /// <summary>Ни у одного файла папки нет обложки.</summary>
    NoCover,

    /// <summary>В одной папке разные значения тега «альбом».</summary>
    MixedAlbums,

    /// <summary>Один и тот же трек лежит в нескольких местах.</summary>
    Duplicate,
}

/// <summary>
/// Замечание, которое относится не к файлу, а к папке или к коллекции целиком.
/// </summary>
/// <remarks>
/// Отдельный тип, а не <see cref="CheckIssue" />, по существу дела: пропуск в
/// нумерации — свойство альбома, а не какого-то одного файла в нём, и вешать
/// такое замечание на случайно выбранный файл было бы неправдой.
/// </remarks>
/// <param name="Kind">О чём речь.</param>
/// <param name="Path">Папка или файл, к которому относится замечание.</param>
/// <param name="Message">Формулировка для человека.</param>
/// <param name="Detail">Подробности: чего именно не хватает, где лежат копии.</param>
public sealed record CollectionFinding(
    CollectionFindingKind Kind,
    string Path,
    string Message,
    string? Detail = null)
{
    /// <summary>Короткая подпись вида замечания.</summary>
    public string KindLabel => Kind switch
    {
        CollectionFindingKind.MissingTracks => "Пропуски в нумерации",
        CollectionFindingKind.MixedFormats => "Разные форматы",
        CollectionFindingKind.NoCover => "Нет обложки",
        CollectionFindingKind.MixedAlbums => "Разные альбомы в папке",
        CollectionFindingKind.Duplicate => "Дубликат",
        _ => "Замечание",
    };
}
