using MusicScanIntegrity.Core.Integrity;
using MusicScanIntegrity.Core.Resources;

namespace MusicScanIntegrity.Core.Models;

/// <summary>Result of checking one file.</summary>
public sealed class FileCheckResult
{
    /// <summary>Full path to the file.</summary>
    public required string FullPath { get; init; }

    /// <summary>File name without the folder.</summary>
    public required string FileName { get; init; }

    /// <summary>Folder the file sits in.</summary>
    public required string DirectoryPath { get; init; }

    /// <summary>Size in bytes; -1 when it could not be read.</summary>
    public required long SizeBytes { get; init; }

    /// <summary>Final status: the most serious of the findings.</summary>
    public required CheckStatus Status { get; init; }

    /// <summary>All findings; an empty list means the file is fine.</summary>
    public required IReadOnlyList<CheckIssue> Issues { get; init; }

    /// <summary>How long checking this file took.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Format from the extension ("FLAC", "MP3"). When the contents disagree
    /// with the extension it reads "FLAC?".
    /// </summary>
    public required string Format { get; init; }

    /// <summary>Item kind: audio, playlist or disc image.</summary>
    public ScanItemKind Kind { get; init; } = ScanItemKind.Audio;

    /// <summary>Tags read from the file; filled only when tag checking is on.</summary>
    public TrackMetadata? Metadata { get; init; }

    /// <summary>Duration from the header; 0 when unknown.</summary>
    /// <remarks>
    /// Not for display but for cross-checking: a cue sheet marks tracks by
    /// offset from the start, so a mark past the duration means the cue and
    /// the file come from different releases.
    /// </remarks>
    public double DurationSeconds { get; init; }

    /// <summary>Unchanged since the last scan, so deep checks were skipped.</summary>
    public bool Unchanged { get; init; }

    /// <summary>Outcome of validating the file against its own format.</summary>
    /// <remarks>
    /// Kept apart from the findings: a healthy file has none, yet the
    /// difference between "checksums matched" and "this format has no
    /// checksums" matters and belongs in the details pane.
    /// </remarks>
    public ContainerValidation? Integrity { get; init; }

    /// <summary>Status caption for the table; specific when there is one finding.</summary>
    public string StatusLabel
    {
        get
        {
            if (Issues.Count == 0)
            {
                return Status.DisplayName();
            }

            // Most serious finding wins; ties go to the first one.
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

    /// <summary>Human-readable text for the description column and the details pane.</summary>
    public string Description => Issues.Count == 0
        ? IntegrityNote
        : string.Join(" ", Issues.Select(i => i.Message));

    /// <summary>What exactly was confirmed about a healthy file.</summary>
    private string IntegrityNote => Unchanged
        ? Strings.Result_Unchanged
        : Integrity?.Verdict switch
    {
        ContainerVerdict.Verified =>
            Common.Format.Text(Strings.Result_Verified, Common.Format.Number(Integrity.UnitsChecked)),
        ContainerVerdict.StructureOnly =>
            Strings.Result_StructureOnly,
        _ => Strings.Result_Decoded,
    };

    /// <summary>Technical causes of every finding; the second line in the details.</summary>
    public string? TechnicalDetail
    {
        get
        {
            string[] details = [.. Issues.Select(i => i.TechnicalDetail).Where(d => !string.IsNullOrWhiteSpace(d))!];
            return details.Length == 0 ? null : string.Join("; ", details);
        }
    }

    /// <summary>Builds the result, deriving the status from the findings.</summary>
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

/// <summary>The track tags that tag checking looks at.</summary>
/// <param name="TrackNumber">Track number; 0 when absent.</param>
public sealed record TrackMetadata(
    string? Title,
    string? Artist,
    string? Album,
    double? DurationSeconds,
    int TrackNumber = 0,
    bool HasCover = false)
{
    /// <summary>All three main tags are present.</summary>
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(Title) &&
        !string.IsNullOrWhiteSpace(Artist) &&
        !string.IsNullOrWhiteSpace(Album);

    /// <summary>Lists the missing tags for the user-facing message.</summary>
    public IReadOnlyList<string> MissingFields
    {
        get
        {
            List<string> missing = [];
            if (string.IsNullOrWhiteSpace(Title)) missing.Add(Strings.Tag_Title);
            if (string.IsNullOrWhiteSpace(Artist)) missing.Add(Strings.Tag_Artist);
            if (string.IsNullOrWhiteSpace(Album)) missing.Add(Strings.Tag_Album);
            return missing;
        }
    }
}
