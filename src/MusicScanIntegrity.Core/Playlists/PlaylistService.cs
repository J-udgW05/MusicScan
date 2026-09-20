using System.Text;
using MusicScanIntegrity.Core.Models;
using MusicScanIntegrity.Core.Resources;

namespace MusicScanIntegrity.Core.Playlists;

/// <summary>Checks playlists: do the paths they reference exist.</summary>
public interface IPlaylistService
{
    /// <summary>
    /// Checks one playlist. Nothing is decoded; only path existence is
    /// verified.
    /// </summary>
    /// <param name="playlistPath">Path to the playlist file.</param>
    /// <param name="knownStatuses">
    /// Statuses from the main scan, so the same file cannot end up with two
    /// different results.
    /// </param>
    /// <param name="knownDurations">
    /// Durations from the main scan, used to validate cue sheet track marks.
    /// </param>
    Task<PlaylistCheckResult> CheckAsync(
        string playlistPath,
        IReadOnlyDictionary<string, CheckStatus>? knownStatuses = null,
        IReadOnlyDictionary<string, double>? knownDurations = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IPlaylistService" />
public sealed class PlaylistService : IPlaylistService
{
    private readonly Dictionary<string, IPlaylistParser> _parsers;

    /// <summary>Creates the service with the standard set of parsers.</summary>
    public PlaylistService(IEnumerable<IPlaylistParser>? parsers = null)
    {
        _parsers = new Dictionary<string, IPlaylistParser>(StringComparer.OrdinalIgnoreCase);

        // An empty set counts as "not supplied": the DI container passes an
        // empty collection rather than the default, which would leave the
        // service with no parsers at all.
        IReadOnlyList<IPlaylistParser> all = parsers?.ToArray() is { Length: > 0 } provided
            ? provided
            : [new M3uPlaylistParser(), new PlsPlaylistParser(), new CuePlaylistParser()];

        foreach (IPlaylistParser parser in all)
        {
            foreach (string extension in parser.Extensions)
            {
                _parsers[extension] = parser;
            }
        }
    }

    /// <inheritdoc />
    public async Task<PlaylistCheckResult> CheckAsync(
        string playlistPath,
        IReadOnlyDictionary<string, CheckStatus>? knownStatuses = null,
        IReadOnlyDictionary<string, double>? knownDurations = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playlistPath);

        string extension = Path.GetExtension(playlistPath);
        if (!_parsers.TryGetValue(extension, out IPlaylistParser? parser))
        {
            return new PlaylistCheckResult
            {
                FullPath = playlistPath,
                Entries = [],
                ParseIssue = new CheckIssue(
                    IssueCode.PlaylistUnreadable,
                    Strings.Playlist_Unsupported,
                    Common.Format.Text(Strings.Playlist_Unsupported_Detail, extension)),
            };
        }

        string content;
        try
        {
            content = await ReadTextAsync(playlistPath, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new PlaylistCheckResult
            {
                FullPath = playlistPath,
                Entries = [],
                ParseIssue = new CheckIssue(
                    IssueCode.PlaylistUnreadable,
                    Strings.Playlist_Unreadable,
                    $"{ex.GetType().Name} · {ex.Message}"),
            };
        }

        string baseFolder = Path.GetDirectoryName(Path.GetFullPath(playlistPath)) ?? string.Empty;
        List<PlaylistEntry> entries = [];

        foreach (string rawPath in parser.Parse(content))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? resolved = ResolvePath(rawPath, baseFolder);
            bool exists = resolved is not null && File.Exists(resolved);

            CheckStatus? known = null;
            if (exists && knownStatuses is not null && resolved is not null &&
                knownStatuses.TryGetValue(resolved, out CheckStatus status))
            {
                known = status;
            }

            entries.Add(new PlaylistEntry(rawPath, resolved, exists, known));
        }

        return new PlaylistCheckResult
        {
            FullPath = playlistPath,
            Entries = entries,
            ContentIssue = CheckCueMarks(playlistPath, content, entries, knownDurations),
        };
    }

    /// <summary>
    /// Checks cue sheet track marks against the file duration.
    /// </summary>
    /// <remarks>
    /// A mark past the end means the cue and the audio come from different
    /// releases: the sheet describes one file while another sits next to it.
    /// A player would silently list tracks that do not exist.
    /// </remarks>
    private static CheckIssue? CheckCueMarks(
        string playlistPath,
        string content,
        IReadOnlyList<PlaylistEntry> entries,
        IReadOnlyDictionary<string, double>? knownDurations)
    {
        if (knownDurations is null
            || !string.Equals(Path.GetExtension(playlistPath), ".cue", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        PlaylistEntry? target = entries.FirstOrDefault(e => e.Exists && e.ResolvedPath is not null);

        if (target?.ResolvedPath is not { } audioPath
            || !knownDurations.TryGetValue(audioPath, out double duration)
            || duration <= 0)
        {
            return null;
        }

        IReadOnlyList<CueMark> marks = CuePlaylistParser.ParseMarks(content);

        if (marks.Count == 0)
        {
            return null;
        }

        CueMark last = marks[^1];

        // One second of slack: the last track sometimes starts right at the
        // end and the header duration is rounded.
        if (last.Seconds <= duration + 1)
        {
            return null;
        }

        return new CheckIssue(
            IssueCode.CueMarksBeyondFile,
            Common.Format.Text(Strings.Playlist_CueBeyondFile, last.Track),
            Common.Format.Text(Strings.Playlist_CueBeyondFile_Detail, last.Seconds, duration));
    }

    /// <summary>
    /// Resolves a playlist path. Relative paths are taken from the playlist's
    /// own folder, not the process working directory.
    /// </summary>
    internal static string? ResolvePath(string rawPath, string baseFolder)
    {
        string trimmed = rawPath.Trim().Trim('"');
        if (trimmed.Length == 0)
        {
            return null;
        }

        // Network stream URLs are not files on disk; nothing to check.
        if (trimmed.Contains("://", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            // Playlists from other systems use "/" instead of "\".
            string normalized = trimmed.Replace('/', Path.DirectorySeparatorChar);

            return Path.IsPathRooted(normalized)
                ? Path.GetFullPath(normalized)
                : Path.GetFullPath(Path.Combine(baseFolder, normalized));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Not path-like at all; treated as a missing entry.
            return null;
        }
    }

    /// <summary>
    /// Reads a playlist with encoding detection: .m3u8 and files with a BOM
    /// are UTF-8; older .m3u and .pls without one are usually system ANSI.
    /// </summary>
    private static async Task<string> ReadTextAsync(string path, CancellationToken cancellationToken)
    {
        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        string extension = Path.GetExtension(path);
        if (extension.Equals(".m3u8", StringComparison.OrdinalIgnoreCase))
        {
            return Encoding.UTF8.GetString(bytes);
        }

        // Try strict UTF-8 first: if the bytes decode, it almost certainly is.
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return SystemAnsiEncoding.GetString(bytes);
        }
    }

    private static Encoding SystemAnsiEncoding { get; } = GetSystemAnsiEncoding();

    private static Encoding GetSystemAnsiEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(0);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return Encoding.Latin1;
        }
    }
}
