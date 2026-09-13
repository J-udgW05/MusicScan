using MusicScanIntegrity.Core.Integrity;

namespace MusicScanIntegrity.Core.Models;

/// <summary>Результат проверки одного файла.</summary>
public sealed class FileCheckResult
{
    /// <summary>Полный путь к файлу.</summary>
    public required string FullPath { get; init; }

    /// <summary>Имя файла без папки.</summary>
    public required string FileName { get; init; }

    /// <summary>Папка, в которой лежит файл.</summary>
    public required string DirectoryPath { get; init; }

    /// <summary>Размер в байтах; -1, если узнать не удалось.</summary>
    public required long SizeBytes { get; init; }

    /// <summary>Итоговый статус — самое серьёзное из замечаний.</summary>
    public required CheckStatus Status { get; init; }

    /// <summary>Все замечания по файлу; пустой список означает «в порядке».</summary>
    public required IReadOnlyList<CheckIssue> Issues { get; init; }

    /// <summary>Сколько заняла проверка именно этого файла.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Формат по расширению («FLAC», «MP3»). Если содержимое не совпало с расширением,
    /// сюда попадает вид «FLAC?» — как в макете результатов.
    /// </summary>
    public required string Format { get; init; }

    /// <summary>Тип объекта (аудио, плейлист, образ диска).</summary>
    public ScanItemKind Kind { get; init; } = ScanItemKind.Audio;

    /// <summary>Прочитанные теги — заполняются, только если включена проверка метаданных.</summary>
    public TrackMetadata? Metadata { get; init; }

    /// <summary>
    /// Длительность по данным заголовка; 0 — неизвестна.
    /// </summary>
    /// <remarks>
    /// Нужна не для показа, а для сверки: cue-лист размечает дорожки временем
    /// от начала файла, и метка за пределом длительности означает, что cue и
    /// файл — из разных изданий.
    /// </remarks>
    public double DurationSeconds { get; init; }

    /// <summary>
    /// Файл не изменился с прошлой проверки, и глубокие проверки пропущены.
    /// </summary>
    public bool Unchanged { get; init; }

    /// <summary>
    /// Чем закончилась проверка файла его собственными средствами.
    /// </summary>
    /// <remarks>
    /// Хранится отдельно от замечаний: для исправного файла замечаний нет, но
    /// разница между «суммы сошлись» и «сумм в формате нет» важна и должна быть
    /// видна в подробностях.
    /// </remarks>
    public ContainerValidation? Integrity { get; init; }

    /// <summary>Подпись статуса для таблицы: уточняющая, если замечание одно.</summary>
    public string StatusLabel
    {
        get
        {
            if (Issues.Count == 0)
            {
                return Status.DisplayName();
            }

            // Показываем самое серьёзное замечание; при равенстве — первое по порядку.
            CheckIssue leading = Issues[0];
            foreach (CheckIssue issue in Issues)
            {
                if ((int)issue.Severity > (int)leading.Severity)
                {
                    leading = issue;
                }
            }

            return leading.ShortLabel;
        }
    }

    /// <summary>Человеческое описание для колонки «Описание» и панели подробностей.</summary>
    public string Description => Issues.Count == 0
        ? IntegrityNote
        : string.Join(" ", Issues.Select(i => i.Message));

    /// <summary>Что именно удалось подтвердить у исправного файла.</summary>
    private string IntegrityNote => Unchanged
        ? "Файл не изменился с прошлой проверки — проверен по отпечатку содержимого."
        : Integrity?.Verdict switch
    {
        ContainerVerdict.Verified =>
            $"Файл прочитан, контрольные суммы формата сошлись (проверено единиц: {Integrity.UnitsChecked}).",
        ContainerVerdict.StructureOnly =>
            "Файл прочитан, структура цела. Контрольных сумм в этом формате нет — проверены объявленные длины.",
        _ => "Файл прочитан и декодирован без ошибок.",
    };

    /// <summary>Технические причины всех замечаний — вторая строка в подробностях.</summary>
    public string? TechnicalDetail
    {
        get
        {
            string[] details = [.. Issues.Select(i => i.TechnicalDetail).Where(d => !string.IsNullOrWhiteSpace(d))!];
            return details.Length == 0 ? null : string.Join("; ", details);
        }
    }

    /// <summary>Собирает результат, выводя итоговый статус из списка замечаний.</summary>
    public static FileCheckResult From(
        ScanItem item,
        IReadOnlyList<CheckIssue> issues,
        TimeSpan duration,
        string format,
        TrackMetadata? metadata = null,
        long? actualSize = null,
        ContainerValidation? integrity = null,
        double durationSeconds = 0)
    {
        CheckStatus status = CheckStatus.Ok;
        foreach (CheckIssue issue in issues)
        {
            status = status.Combine(issue.Severity);
        }

        return new FileCheckResult
        {
            FullPath = item.FullPath,
            FileName = Path.GetFileName(item.FullPath),
            DirectoryPath = Path.GetDirectoryName(item.FullPath) ?? string.Empty,
            SizeBytes = actualSize ?? item.SizeBytes,
            Status = status,
            Issues = issues,
            Duration = duration,
            Format = format,
            Kind = item.Kind,
            Metadata = metadata,
            Integrity = integrity,
            DurationSeconds = durationSeconds,
        };
    }
}

/// <summary>Основные теги трека — то, что проверяется при включённой проверке метаданных.</summary>
/// <param name="Title">Название.</param>
/// <param name="Artist">Исполнитель.</param>
/// <param name="Album">Альбом.</param>
/// <param name="DurationSeconds">Длительность по данным тегов, если известна.</param>
/// <param name="TrackNumber">Номер дорожки; 0 — не указан.</param>
/// <param name="HasCover">В файле есть обложка.</param>
public sealed record TrackMetadata(
    string? Title,
    string? Artist,
    string? Album,
    double? DurationSeconds,
    int TrackNumber = 0,
    bool HasCover = false)
{
    /// <summary>Все три основных тега заполнены.</summary>
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(Title) &&
        !string.IsNullOrWhiteSpace(Artist) &&
        !string.IsNullOrWhiteSpace(Album);

    /// <summary>Перечисляет отсутствующие теги для человеческого сообщения.</summary>
    public IReadOnlyList<string> MissingFields
    {
        get
        {
            List<string> missing = [];
            if (string.IsNullOrWhiteSpace(Title)) missing.Add("название");
            if (string.IsNullOrWhiteSpace(Artist)) missing.Add("исполнитель");
            if (string.IsNullOrWhiteSpace(Album)) missing.Add("альбом");
            return missing;
        }
    }
}
