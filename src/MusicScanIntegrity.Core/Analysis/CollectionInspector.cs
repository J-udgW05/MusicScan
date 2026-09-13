using System.Globalization;
using MusicScanIntegrity.Core.Models;

namespace MusicScanIntegrity.Core.Analysis;

/// <summary>
/// Разбирает коллекцию целиком: альбомы по папкам и повторы по всей проверке.
/// </summary>
/// <remarks>
/// Работает по уже готовым результатам, второй раз файлы не читает. Замечания
/// получаются не о файле, а о папке или о паре файлов, поэтому и хранятся
/// отдельно от списка результатов.
/// </remarks>
public static class CollectionInspector
{
    /// <summary>Меньше этого числа файлов папка альбомом не считается.</summary>
    private const int MinAlbumFiles = 3;

    /// <summary>
    /// Какая доля файлов должна иметь номер дорожки, чтобы судить о пропусках.
    /// </summary>
    private const double NumberedShare = 0.7;

    /// <summary>Сколько пропущенных номеров показывать в подробностях.</summary>
    private const int MaxListed = 12;

    /// <summary>
    /// Наибольший номер дорожки, который ещё считается номером.
    /// </summary>
    /// <remarks>
    /// На компакт-диске дорожек не больше 99, в самых длинных сборниках — сотни.
    /// Номер за этой границей означает не альбом на миллион дорожек, а
    /// испорченный тег: программа для того и написана, чтобы такие встречать.
    /// Считать по нему пропуски нельзя — получился бы список из миллиона
    /// «недостающих» дорожек, а до этого миллион чисел в памяти.
    /// </remarks>
    private const int MaxTrackNumber = 999;

    /// <summary>Разбирает результаты проверки.</summary>
    /// <param name="results">Результаты по файлам.</param>
    /// <param name="findDuplicates">Искать повторяющиеся треки.</param>
    /// <returns>Замечания по папкам и повторам.</returns>
    public static IReadOnlyList<CollectionFinding> Inspect(
        IReadOnlyList<FileCheckResult> results,
        bool findDuplicates = true)
    {
        ArgumentNullException.ThrowIfNull(results);

        List<CollectionFinding> findings = [];

        foreach (IGrouping<string, FileCheckResult> folder in results
            .Where(r => r.Kind == ScanItemKind.Audio)
            .GroupBy(r => r.DirectoryPath, StringComparer.OrdinalIgnoreCase))
        {
            InspectFolder(folder.Key, [.. folder], findings);
        }

        if (findDuplicates)
        {
            FindDuplicates(results, findings);
        }

        return findings;
    }

    private static void InspectFolder(string folder, IReadOnlyList<FileCheckResult> files, List<CollectionFinding> findings)
    {
        if (files.Count < MinAlbumFiles)
        {
            return;
        }

        CheckTrackNumbers(folder, files, findings);
        CheckFormats(folder, files, findings);
        CheckAlbumTags(folder, files, findings);
        CheckCover(folder, files, findings);
    }

    /// <summary>Ищет пропуски в нумерации дорожек.</summary>
    private static void CheckTrackNumbers(string folder, IReadOnlyList<FileCheckResult> files, List<CollectionFinding> findings)
    {
        int[] numbers = [.. files
            .Select(f => f.Metadata?.TrackNumber ?? 0)
            .Where(n => n > 0)
            .Distinct()
            .Order()];

        // Судить о пропусках можно, только когда пронумеровано почти всё:
        // иначе «пропуском» окажется папка, где номера просто не проставлены.
        if (numbers.Length < MinAlbumFiles || (double)numbers.Length / files.Count < NumberedShare)
        {
            return;
        }

        // Номер вне разумных пределов — это сломанный тег, а не край альбома.
        if (numbers[^1] > MaxTrackNumber)
        {
            return;
        }

        int[] missing = [.. Enumerable.Range(1, numbers[^1]).Except(numbers)];

        if (missing.Length == 0)
        {
            return;
        }

        string listed = string.Join(", ", missing.Take(MaxListed));
        if (missing.Length > MaxListed)
        {
            listed += " и другие";
        }

        findings.Add(new CollectionFinding(
            CollectionFindingKind.MissingTracks,
            folder,
            $"В альбоме не хватает дорожек: {listed}.",
            $"Найдено {numbers.Length} из {numbers[^1]} по нумерации в тегах"));
    }

    /// <summary>Замечает разнобой форматов внутри одной папки.</summary>
    private static void CheckFormats(string folder, IReadOnlyList<FileCheckResult> files, List<CollectionFinding> findings)
    {
        string[] formats = [.. files
            .Select(f => f.Format.TrimEnd('?'))
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)];

        if (formats.Length < 2)
        {
            return;
        }

        findings.Add(new CollectionFinding(
            CollectionFindingKind.MixedFormats,
            folder,
            $"В одной папке файлы разных форматов: {string.Join(", ", formats)}.",
            "Обычно так выходит, когда часть альбома докачали в другом качестве"));
    }

    /// <summary>Замечает разные значения тега «альбом» в одной папке.</summary>
    private static void CheckAlbumTags(string folder, IReadOnlyList<FileCheckResult> files, List<CollectionFinding> findings)
    {
        string[] albums = [.. files
            .Select(f => f.Metadata?.Album)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)!];

        if (albums.Length < 2)
        {
            return;
        }

        findings.Add(new CollectionFinding(
            CollectionFindingKind.MixedAlbums,
            folder,
            $"В папке лежат треки разных альбомов: {string.Join(", ", albums.Take(3))}.",
            albums.Length > 3 ? $"Всего разных значений тега: {albums.Length}" : null));
    }

    /// <summary>Замечает папку без единой обложки.</summary>
    private static void CheckCover(string folder, IReadOnlyList<FileCheckResult> files, List<CollectionFinding> findings)
    {
        // Говорить об обложке имеет смысл, только если теги вообще читались.
        if (!files.Any(f => f.Metadata is not null) || files.Any(f => f.Metadata?.HasCover == true))
        {
            return;
        }

        if (HasCoverFile(folder))
        {
            return;
        }

        findings.Add(new CollectionFinding(
            CollectionFindingKind.NoCover,
            folder,
            "У альбома нет обложки: ни в тегах, ни отдельным файлом.",
            null));
    }

    private static bool HasCoverFile(string folder)
    {
        try
        {
            foreach (string path in Directory.EnumerateFiles(folder))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                string extension = Path.GetExtension(path);

                bool looksLikeImage = extension is ".jpg" or ".jpeg" or ".png" or ".webp" or ".bmp";
                bool looksLikeCover = name.Contains("cover", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("front", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("folder", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("обложка", StringComparison.OrdinalIgnoreCase);

                if (looksLikeImage && looksLikeCover)
                {
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Папку не прочитать — считаем, что обложка есть: лишнее замечание
            // хуже пропущенного.
            return true;
        }

        return false;
    }

    /// <summary>
    /// Ищет один и тот же трек в разных местах.
    /// </summary>
    /// <remarks>
    /// Совпадать должны исполнитель, название и длительность с точностью до
    /// секунды. По одним тегам сравнивать нельзя: у концертных и студийных
    /// версий названия совпадают, а треки разные.
    /// </remarks>
    private static void FindDuplicates(IReadOnlyList<FileCheckResult> results, List<CollectionFinding> findings)
    {
        var groups = results
            .Where(r => r.Kind == ScanItemKind.Audio
                && r.Metadata is { Title: not null, Artist: not null }
                && r.DurationSeconds > 1)
            .GroupBy(r => string.Create(
                CultureInfo.InvariantCulture,
                $"{r.Metadata!.Artist!.Trim().ToLowerInvariant()}|{r.Metadata.Title!.Trim().ToLowerInvariant()}|{Math.Round(r.DurationSeconds)}"))
            .Where(g => g.Count() > 1);

        foreach (var group in groups)
        {
            FileCheckResult[] copies = [.. group.OrderBy(r => r.FullPath, StringComparer.OrdinalIgnoreCase)];
            FileCheckResult first = copies[0];

            int elsewhere = copies.Length - 1;

            findings.Add(new CollectionFinding(
                CollectionFindingKind.Duplicate,
                first.FullPath,
                $"Тот же трек лежит ещё в {elsewhere} {Common.Format.Plural(elsewhere, "месте", "местах", "местах")}: "
                    + $"{first.Metadata!.Artist} — {first.Metadata.Title}.",
                string.Join("; ", copies.Skip(1).Take(MaxListed).Select(c => c.FullPath))));
        }
    }
}
