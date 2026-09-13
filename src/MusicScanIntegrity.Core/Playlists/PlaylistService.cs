using System.Text;
using MusicScanIntegrity.Core.Models;

namespace MusicScanIntegrity.Core.Playlists;

/// <summary>Проверка плейлистов: существуют ли пути, на которые они ссылаются.</summary>
public interface IPlaylistService
{
    /// <summary>
    /// Проверяет один плейлист. Файлы не декодируются — только проверяется
    /// существование путей (02_ARCHITECTURE.md, раздел 6).
    /// </summary>
    /// <param name="playlistPath">Путь к файлу плейлиста.</param>
    /// <param name="knownStatuses">
    /// Статусы файлов из основного сканирования — чтобы один и тот же файл
    /// не получил два разных результата.
    /// </param>
    /// <param name="knownDurations">
    /// Длительности файлов из основного сканирования: по ним проверяются метки
    /// дорожек в cue-листах.
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

    /// <summary>Создаёт службу со стандартным набором разборщиков.</summary>
    public PlaylistService(IEnumerable<IPlaylistParser>? parsers = null)
    {
        _parsers = new Dictionary<string, IPlaylistParser>(StringComparer.OrdinalIgnoreCase);

        // Пустой набор считается «не задан»: контейнер внедрения зависимостей
        // подставляет пустую коллекцию вместо значения по умолчанию, и без этой
        // проверки служба осталась бы вообще без разборщиков.
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
                    "Формат плейлиста не поддерживается.",
                    $"Расширение {extension}"),
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
                    "Файл плейлиста не удалось прочитать.",
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
    /// Сверяет метки дорожек cue-листа с длительностью файла.
    /// </summary>
    /// <remarks>
    /// Метка за пределом файла означает, что cue и аудио — из разных изданий:
    /// диск размечен на один файл, а рядом лежит другой. Проигрыватель в таком
    /// случае молча покажет дорожки, которых нет.
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

        // Допуск в секунду: последняя дорожка иногда начинается вплотную к концу,
        // а длительность из заголовка округлена.
        if (last.Seconds <= duration + 1)
        {
            return null;
        }

        return new CheckIssue(
            IssueCode.CueMarksBeyondFile,
            $"Разметка не подходит к файлу: дорожка {last.Track} начинается позже, чем он заканчивается.",
            $"Метка {last.Seconds:0.#} с, длительность файла {duration:0.#} с");
    }

    /// <summary>
    /// Разворачивает путь из плейлиста. Относительные пути считаются
    /// от папки самого плейлиста, а не от рабочей папки программы.
    /// </summary>
    internal static string? ResolvePath(string rawPath, string baseFolder)
    {
        string trimmed = rawPath.Trim().Trim('"');
        if (trimmed.Length == 0)
        {
            return null;
        }

        // Ссылки на поток по сети — не файл на диске, проверять нечего.
        if (trimmed.Contains("://", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            // В плейлистах, приехавших с других систем, встречается «/» вместо «\».
            string normalized = trimmed.Replace('/', Path.DirectorySeparatorChar);

            return Path.IsPathRooted(normalized)
                ? Path.GetFullPath(normalized)
                : Path.GetFullPath(Path.Combine(baseFolder, normalized));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Строка вообще не похожа на путь — считаем её отсутствующей записью.
            return null;
        }
    }

    /// <summary>
    /// Читает плейлист с учётом кодировки: .m3u8 и файлы с BOM — UTF-8,
    /// старые .m3u и .pls без BOM обычно в системной ANSI-кодировке.
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

        // Пробуем UTF-8 строго: если байты валидный UTF-8, это почти наверняка он.
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
