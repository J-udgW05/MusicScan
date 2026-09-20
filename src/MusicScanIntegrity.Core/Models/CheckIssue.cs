using MusicScanIntegrity.Core.Resources;

namespace MusicScanIntegrity.Core.Models;

/// <summary>
/// One finding about a file: a code, a human wording and a technical cause.
/// </summary>
/// <remarks>
/// The split is deliberate — the main text must never read "Error 0x…"; the
/// technical cause goes on a second line.
/// </remarks>
public sealed record CheckIssue(IssueCode Code, string Message, string? TechnicalDetail = null)
{
    /// <summary>Status this finding gives the file.</summary>
    public CheckStatus Severity => Code switch
    {
        IssueCode.None => CheckStatus.Ok,

        IssueCode.FileNotFound
            or IssueCode.AccessDenied
            or IssueCode.DecodeStartFailed
            or IssueCode.AudioReadFailed
            or IssueCode.EmptyFile
            or IssueCode.ChecksumMismatch
            or IssueCode.ContainerDamaged
            or IssueCode.Truncated
            or IssueCode.SilentCorruption
            or IssueCode.UnexpectedError => CheckStatus.Corrupted,

        IssueCode.LockedSkippedByUser => CheckStatus.Skipped,

        // Everything else is soft: the file reads, but something is off.
        _ => CheckStatus.Warning,
    };

    /// <summary>Short caption for the status column of the results table.</summary>
    public string ShortLabel => Code switch
    {
        IssueCode.MetadataProblem => Strings.Issue_Short_MetadataProblem,
        IssueCode.CheckTimeout => Strings.Issue_Short_CheckTimeout,
        IssueCode.ExtensionMismatch => Strings.Issue_Short_ExtensionMismatch,
        IssueCode.PasswordProtected => Strings.Issue_Short_PasswordProtected,
        IssueCode.LockedWaitTimeout => Strings.Issue_Short_LockedWaitTimeout,
        IssueCode.LockedCheckedViaCopy => Strings.Issue_Short_LockedCheckedViaCopy,
        IssueCode.LargeFile => Strings.Issue_Short_LargeFile,
        IssueCode.ChecksumMismatch => Strings.Issue_Short_ChecksumMismatch,
        IssueCode.ContainerDamaged => Strings.Issue_Short_ContainerDamaged,
        IssueCode.Truncated => Strings.Issue_Short_Truncated,
        IssueCode.DigitalSilence => Strings.Issue_Short_DigitalSilence,
        IssueCode.AudioDropout => Strings.Issue_Short_AudioDropout,
        IssueCode.Clipping => Strings.Issue_Short_Clipping,
        IssueCode.DcOffset => Strings.Issue_Short_DcOffset,
        IssueCode.TranscodeSuspected => Strings.Issue_Short_TranscodeSuspected,
        IssueCode.BrokenTagText => Strings.Issue_Short_BrokenTagText,
        IssueCode.SilentCorruption => Strings.Issue_Short_SilentCorruption,
        IssueCode.CueMarksBeyondFile => Strings.Issue_Short_CueMarksBeyondFile,
        _ => Severity.DisplayName(),
    };
}
