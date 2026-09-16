using MusicScanIntegrity.Core.Resources;

namespace MusicScanIntegrity.Core.Models;

/// <summary>Status captions and glyphs, shared by the UI and the reports.</summary>
public static class CheckStatusExtensions
{
    /// <summary>Status name as the user sees it.</summary>
    public static string DisplayName(this CheckStatus status) => status switch
    {
        CheckStatus.Ok => Strings.Status_Ok,
        CheckStatus.Warning => Strings.Status_Warning,
        CheckStatus.Corrupted => Strings.Status_Corrupted,
        CheckStatus.Skipped => Strings.Status_Skipped,
        _ => Strings.Status_Unknown,
    };

    /// <summary>
    /// Textual status mark. Status is never conveyed by colour alone — a glyph
    /// and a word always sit next to it.
    /// </summary>
    public static string Glyph(this CheckStatus status) => status switch
    {
        CheckStatus.Ok => "✓",
        CheckStatus.Warning => "!",
        CheckStatus.Corrupted => "✕",
        CheckStatus.Skipped => "–",
        _ => "?",
    };

    /// <summary>Icon key for this status.</summary>
    public static string IconKey(this CheckStatus status) => status switch
    {
        CheckStatus.Ok => "status-ok",
        CheckStatus.Warning => "status-warning",
        CheckStatus.Corrupted => "status-broken",
        CheckStatus.Skipped => "status-skipped",
        _ => "info",
    };

    /// <summary>
    /// Bare status mark, without the ring around the tick and cross. Used where
    /// the glyph is small (13–14 px) and sits inside a coloured square, where
    /// the ring only blurs it.
    /// </summary>
    public static string MarkKey(this CheckStatus status) => status switch
    {
        CheckStatus.Ok => "mark-ok",
        CheckStatus.Warning => "mark-warning",
        CheckStatus.Corrupted => "mark-broken",
        CheckStatus.Skipped => "mark-skipped",
        _ => "info",
    };

    /// <summary>
    /// The more serious of two statuses. Skipped is terminal: the file was not
    /// checked at all, so it never merges with scan outcomes.
    /// </summary>
    public static CheckStatus Combine(this CheckStatus first, CheckStatus second)
    {
        if (first == CheckStatus.Skipped || second == CheckStatus.Skipped)
        {
            return CheckStatus.Skipped;
        }

        return (CheckStatus)Math.Max((int)first, (int)second);
    }
}
