namespace MusicScanIntegrity.Core.Models;

/// <summary>
/// Running scan tallies shown in the scan tab's side column and the status bar.
/// </summary>
/// <param name="Checked">Files that reached any final status.</param>
public readonly record struct ScanCounters(
    int Total,
    int Checked,
    int Ok,
    int Corrupted,
    int Warnings,
    int Skipped)
{
    /// <summary>Completed share, 0 to 1.</summary>
    public double Progress => Total <= 0 ? 0 : Math.Clamp((double)Checked / Total, 0, 1);

    /// <summary>Folds one more result into the tallies.</summary>
    public ScanCounters Add(CheckStatus status) => status switch
    {
        CheckStatus.Ok => this with { Checked = Checked + 1, Ok = Ok + 1 },
        CheckStatus.Warning => this with { Checked = Checked + 1, Warnings = Warnings + 1 },
        CheckStatus.Corrupted => this with { Checked = Checked + 1, Corrupted = Corrupted + 1 },
        CheckStatus.Skipped => this with { Checked = Checked + 1, Skipped = Skipped + 1 },
        _ => this,
    };
}
