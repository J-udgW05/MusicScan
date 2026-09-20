using System.Globalization;
using MusicScanIntegrity.Core.Models;
using MusicScanIntegrity.Core.Resources;

namespace MusicScanIntegrity.Core.Analysis;

/// <summary>
/// Inspects the collection as a whole: albums per folder and duplicates across the scan.
/// </summary>
/// <remarks>
/// Works from finished results and reads no file twice. The findings concern a
/// folder or a pair of files rather than a single file, which is why they are
/// kept apart from the results list.
/// </remarks>
public static class CollectionInspector
{
    /// <summary>Below this many files a folder is not treated as an album.</summary>
    private const int MinAlbumFiles = 3;

    /// <summary>
    /// Share of files that must carry a track number before gaps are judged.
    /// </summary>
    private const double NumberedShare = 0.7;

    /// <summary>How many missing numbers to list in the details.</summary>
    private const int MaxListed = 12;

    /// <summary>
    /// Largest value still treated as a track number.
    /// </summary>
    /// <remarks>
    /// A CD holds at most 99 tracks and the longest compilations run to
    /// hundreds. A number beyond this bound means a broken tag, not an album
    /// with a million tracks. Honouring it would allocate a million integers
    /// and emit a million "missing track" findings.
    /// </remarks>
    private const int MaxTrackNumber = 999;

    /// <summary>Inspects the scan results.</summary>
    /// <param name="results">Per-file results.</param>
    /// <param name="findDuplicates">Look for duplicate tracks.</param>
    /// <returns>Findings about folders and duplicates.</returns>
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

    /// <summary>Looks for gaps in track numbering.</summary>
    private static void CheckTrackNumbers(string folder, IReadOnlyList<FileCheckResult> files, List<CollectionFinding> findings)
    {
        int[] numbers = [.. files
            .Select(f => f.Metadata?.TrackNumber ?? 0)
            .Where(n => n > 0)
            .Distinct()
            .Order()];

        // Gaps can only be judged when almost everything is numbered;
        // otherwise an unnumbered folder looks like one big gap.
        if (numbers.Length < MinAlbumFiles || (double)numbers.Length / files.Count < NumberedShare)
        {
            return;
        }

        // A number outside sane bounds is a broken tag, not the album's end.
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
            listed += Strings.Collection_AndMore;
        }

        findings.Add(new CollectionFinding(
            CollectionFindingKind.MissingTracks,
            folder,
            Common.Format.Text(Strings.Collection_MissingTracks, listed),
            Common.Format.Text(Strings.Collection_MissingTracks_Detail, numbers.Length, numbers[^1])));
    }

    /// <summary>Notices mixed formats inside one folder.</summary>
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
            Common.Format.Text(Strings.Collection_MixedFormats, string.Join(", ", formats)),
            Strings.Collection_MixedFormats_Detail));
    }

    /// <summary>Notices differing album tags inside one folder.</summary>
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
            Common.Format.Text(Strings.Collection_MixedAlbums, string.Join(", ", albums.Take(3))),
            albums.Length > 3 ? Common.Format.Text(Strings.Collection_MixedAlbums_Detail, albums.Length) : null));
    }

    /// <summary>Notices a folder without any cover art.</summary>
    private static void CheckCover(string folder, IReadOnlyList<FileCheckResult> files, List<CollectionFinding> findings)
    {
        // Cover art is only worth reporting when tags were read at all.
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
            Strings.Collection_NoCover,
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
            // Unreadable folder: assume cover art exists. A false finding is
            // worse than a missed one.
            return true;
        }

        return false;
    }

    /// <summary>
    /// Looks for the same track in more than one place.
    /// </summary>
    /// <remarks>
    /// Artist, title and duration must match to the second. Tags alone are not
    /// enough: live and studio versions share a title but are different
    /// recordings.
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
                Common.Format.Count(elsewhere, "Plural_DuplicateElsewhere", first.Metadata!.Artist, first.Metadata.Title),
                string.Join("; ", copies.Skip(1).Take(MaxListed).Select(c => c.FullPath))));
        }
    }
}
