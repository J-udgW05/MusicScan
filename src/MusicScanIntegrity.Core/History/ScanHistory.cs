using Microsoft.Data.Sqlite;
using MusicScanIntegrity.Core.Models;

namespace MusicScanIntegrity.Core.History;

/// <summary>What was remembered about a file from the previous scan.</summary>
public sealed record FileHistoryEntry(
    string Path,
    long SizeBytes,
    DateTime ModifiedUtc,
    string Hash,
    CheckStatus Status,
    DateTimeOffset CheckedAt);

/// <summary>
/// Scan history: what was checked, when, and what the contents looked like.
/// </summary>
/// <remarks>
/// Exists for one conclusion nothing else can reach: contents changed while
/// size and date stayed the same. That is what silent corruption looks like —
/// a failing disk or memory rewriting bytes without saying so.
/// </remarks>
public interface IScanHistory : IDisposable
{
    /// <summary>The history is open and ready.</summary>
    bool IsOpen { get; }

    /// <summary>Path to the database file.</summary>
    string DatabasePath { get; }

    /// <summary>Opens or creates the database.</summary>
    /// <param name="databasePath">Path to the database file.</param>
    /// <returns>An error description if opening failed, otherwise <see langword="null" />.</returns>
    string? Open(string databasePath);

    /// <summary>Reads the entry for a file.</summary>
    /// <param name="path">Full path to the file.</param>
    /// <returns>The entry, or <see langword="null" /> when the file is new.</returns>
    FileHistoryEntry? Find(string path);

    /// <summary>Stores the entry for a file.</summary>
    void Save(FileHistoryEntry entry);

    /// <summary>Wipes the whole history.</summary>
    void Clear();

    /// <summary>Closes the database.</summary>
    void Close();

    /// <summary>How many entries are stored.</summary>
    int Count();
}

/// <inheritdoc cref="IScanHistory" />
/// <remarks>
/// SQLite rather than a bespoke format: it survives a crash, can be read with
/// other tools and does not require holding the collection in memory. Writes
/// are batched into a single transaction — on a hundred thousand files that is
/// the difference between seconds and minutes.
/// </remarks>
public sealed class ScanHistory : IScanHistory
{
    /// <summary>Data folder next to the executable, as for settings.</summary>
    private const string DataFolderName = "data";

    /// <summary>Database file name.</summary>
    private const string FileName = "history.db";

    /// <summary>
    /// Default database path: next to the executable, or in the user profile
    /// when that folder is not writable.
    /// </summary>
    /// <returns>Full path.</returns>
    /// <remarks>
    /// The fallback matters for an installed build: Program Files is not
    /// writable for an ordinary user, and without it corruption tracking would
    /// silently do nothing.
    /// </remarks>
    public static string ResolveDefaultPath() =>
        Path.Combine(Common.WritableFolder.Resolve(DataFolderName), FileName);

    /// <summary>
    /// Date bounds that survive conversion to Windows file time.
    /// </summary>
    /// <remarks>
    /// Modification dates are stored as FILETIME, an integer that compares
    /// cheaply. <see cref="DateTime.ToFileTimeUtc" /> throws on dates before
    /// 1601, and those do occur: damaged filesystem records, archives extracted
    /// without dates, files moved from other media. Such a file used to report
    /// an unexpected error instead of a real result.
    /// </remarks>
    private static readonly DateTime FileTimeEpoch = new(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly Lock _lock = new();
    private SqliteConnection? _connection;

    /// <inheritdoc />
    public bool IsOpen
    {
        get
        {
            lock (_lock)
            {
                return _connection is not null;
            }
        }
    }

    /// <inheritdoc />
    public string DatabasePath { get; private set; } = string.Empty;

    /// <inheritdoc />
    public string? Open(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        lock (_lock)
        {
            Close();

            try
            {
                string? folder = Path.GetDirectoryName(Path.GetFullPath(databasePath));
                if (!string.IsNullOrEmpty(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                _connection = new SqliteConnection($"Data Source={databasePath}");
                _connection.Open();

                using SqliteCommand command = _connection.CreateCommand();

                // WAL: the scan writes often and reads rarely, so writes do
                // not block each other and the database survives a hard exit.
                command.CommandText = """
                    PRAGMA journal_mode = WAL;
                    PRAGMA synchronous = NORMAL;

                    CREATE TABLE IF NOT EXISTS files (
                        path        TEXT PRIMARY KEY,
                        size        INTEGER NOT NULL,
                        modified    INTEGER NOT NULL,
                        hash        TEXT NOT NULL,
                        status      INTEGER NOT NULL,
                        checked_at  INTEGER NOT NULL
                    );
                    """;
                command.ExecuteNonQuery();

                DatabasePath = databasePath;
                return null;
            }
            // The path comes from hand-editable settings, so catch what Path
            // and Directory throw on a malformed path, not just SqliteException.
            catch (Exception ex) when (ex is SqliteException
                                          or IOException
                                          or UnauthorizedAccessException
                                          or ArgumentException
                                          or NotSupportedException)
            {
                Close();
                return $"Не удалось открыть базу истории: {ex.Message}";
            }
        }
    }

    /// <inheritdoc />
    public FileHistoryEntry? Find(string path)
    {
        lock (_lock)
        {
            if (_connection is null)
            {
                return null;
            }

            try
            {
                using SqliteCommand command = _connection.CreateCommand();
                command.CommandText =
                    "SELECT size, modified, hash, status, checked_at FROM files WHERE path = $path";
                command.Parameters.AddWithValue("$path", path);

                using SqliteDataReader reader = command.ExecuteReader();

                return reader.Read()
                    ? new FileHistoryEntry(
                        path,
                        reader.GetInt64(0),
                        FromFileTime(reader.GetInt64(1)),
                        reader.GetString(2),
                        (CheckStatus)reader.GetInt32(3),
                        FromUnixSeconds(reader.GetInt64(4)))
                    : null;
            }
            catch (Exception ex) when (ex is SqliteException or InvalidCastException)
            {
                // A damaged database must not break the scan; treat the file
                // as previously unseen.
                return null;
            }
        }
    }

    /// <inheritdoc />
    public void Save(FileHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (_lock)
        {
            if (_connection is null)
            {
                return;
            }

            try
            {
                using SqliteCommand command = _connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO files (path, size, modified, hash, status, checked_at)
                    VALUES ($path, $size, $modified, $hash, $status, $checked)
                    ON CONFLICT(path) DO UPDATE SET
                        size = $size, modified = $modified, hash = $hash,
                        status = $status, checked_at = $checked
                    """;
                command.Parameters.AddWithValue("$path", entry.Path);
                command.Parameters.AddWithValue("$size", entry.SizeBytes);
                command.Parameters.AddWithValue("$modified", ToFileTime(entry.ModifiedUtc));
                command.Parameters.AddWithValue("$hash", entry.Hash);
                command.Parameters.AddWithValue("$status", (int)entry.Status);
                command.Parameters.AddWithValue("$checked", entry.CheckedAt.ToUnixTimeSeconds());
                command.ExecuteNonQuery();
            }
            catch (SqliteException)
            {
                // A failed write must not fail the scan.
            }
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (_lock)
        {
            if (_connection is null)
            {
                return;
            }

            try
            {
                using SqliteCommand command = _connection.CreateCommand();
                command.CommandText = "DELETE FROM files";
                command.ExecuteNonQuery();
            }
            catch (SqliteException)
            {
                // Invoked from a settings button, where an escaping exception
                // would crash the app — not worth it for a failed wipe.
            }
        }
    }

    /// <inheritdoc />
    public int Count()
    {
        lock (_lock)
        {
            if (_connection is null)
            {
                return 0;
            }

            try
            {
                using SqliteCommand command = _connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM files";
                return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (SqliteException)
            {
                return 0;
            }
        }
    }

    /// <inheritdoc />
    public void Dispose() => Close();

    /// <inheritdoc />
    public void Close()
    {
        // Under the lock, or a close can land in the middle of a query. This
        // happens for real: the scan writes from worker threads while the
        // corruption-tracking toggle closes the database from the UI thread.
        lock (_lock)
        {
            _connection?.Dispose();
            _connection = null;
            DatabasePath = string.Empty;
        }
    }

    /// <summary>Converts a date to file time, tolerating out-of-range values.</summary>
    private static long ToFileTime(DateTime value) =>
        value.ToUniversalTime() <= FileTimeEpoch ? 0 : value.ToFileTimeUtc();

    /// <summary>The reverse conversion, with the same tolerance.</summary>
    private static DateTime FromFileTime(long value) =>
        value <= 0 ? FileTimeEpoch : DateTime.FromFileTimeUtc(value);

    /// <summary>Check time; values outside the range fall back to the epoch.</summary>
    private static DateTimeOffset FromUnixSeconds(long value) =>
        value < DateTimeOffset.MinValue.ToUnixTimeSeconds() || value > DateTimeOffset.MaxValue.ToUnixTimeSeconds()
            ? DateTimeOffset.UnixEpoch
            : DateTimeOffset.FromUnixTimeSeconds(value);
}
