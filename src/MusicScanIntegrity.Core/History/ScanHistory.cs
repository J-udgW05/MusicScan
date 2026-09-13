using Microsoft.Data.Sqlite;
using MusicScanIntegrity.Core.Models;

namespace MusicScanIntegrity.Core.History;

/// <summary>Что запомнено о файле с прошлой проверки.</summary>
/// <param name="Path">Полный путь.</param>
/// <param name="SizeBytes">Размер на момент прошлой проверки.</param>
/// <param name="ModifiedUtc">Дата изменения на тот же момент.</param>
/// <param name="Hash">Хеш содержимого.</param>
/// <param name="Status">Чем закончилась прошлая проверка.</param>
/// <param name="CheckedAt">Когда это было.</param>
public sealed record FileHistoryEntry(
    string Path,
    long SizeBytes,
    DateTime ModifiedUtc,
    string Hash,
    CheckStatus Status,
    DateTimeOffset CheckedAt);

/// <summary>
/// История проверок: что и когда проверяли, каким было содержимое файла.
/// </summary>
/// <remarks>
/// Нужна ради одного вывода, который иначе получить нечем: содержимое файла
/// изменилось, а размер и дата остались прежними. Так выглядит тихая порча —
/// сбойный диск или память меняют байты, ничего об этом не сообщая.
/// </remarks>
public interface IScanHistory : IDisposable
{
    /// <summary>История открыта и готова к работе.</summary>
    bool IsOpen { get; }

    /// <summary>Путь к файлу базы.</summary>
    string DatabasePath { get; }

    /// <summary>Открывает или создаёт базу.</summary>
    /// <param name="databasePath">Путь к файлу базы.</param>
    /// <returns>Описание ошибки, если открыть не удалось; иначе <see langword="null" />.</returns>
    string? Open(string databasePath);

    /// <summary>Читает запись о файле.</summary>
    /// <param name="path">Полный путь к файлу.</param>
    /// <returns>Запись или <see langword="null" />, если файл встречается впервые.</returns>
    FileHistoryEntry? Find(string path);

    /// <summary>Сохраняет запись о файле.</summary>
    /// <param name="entry">Что запомнить.</param>
    void Save(FileHistoryEntry entry);

    /// <summary>Стирает всю историю.</summary>
    void Clear();

    /// <summary>Закрывает базу.</summary>
    void Close();

    /// <summary>Сколько записей хранится.</summary>
    int Count();
}

/// <inheritdoc cref="IScanHistory" />
/// <remarks>
/// SQLite, а не свой формат: база переживает падение программы, читается
/// сторонними средствами и не требует держать всю коллекцию в памяти.
/// Запись идёт пачками в одной транзакции — на сотне тысяч файлов это разница
/// между секундами и минутами.
/// </remarks>
public sealed class ScanHistory : IScanHistory
{
    /// <summary>Папка для данных рядом с программой — как и у настроек.</summary>
    private const string DataFolderName = "data";

    /// <summary>Имя файла базы.</summary>
    private const string FileName = "history.db";

    /// <summary>
    /// Путь к базе по умолчанию: рядом с программой, а если туда писать
    /// нельзя — в профиле пользователя.
    /// </summary>
    /// <returns>Полный путь.</returns>
    /// <remarks>
    /// Запасной путь нужен установленной программе: из <c>Program Files</c>
    /// обычному пользователю писать не дают, и без него слежение за порчей
    /// молча не работало бы. Настройки так умеют с самого начала.
    /// </remarks>
    public static string ResolveDefaultPath() =>
        Path.Combine(Common.WritableFolder.Resolve(DataFolderName), FileName);

    /// <summary>
    /// Границы дат, которые переживают перевод в файловое время Windows.
    /// </summary>
    /// <remarks>
    /// Дата изменения хранится как FILETIME — целое число, по которому легко
    /// сравнивать. Но <see cref="DateTime.ToFileTimeUtc" /> бросает исключение
    /// на датах до 1601 года, а такие в коллекциях встречаются: испорченная
    /// запись в файловой системе, распаковка архива без дат, перенос с других
    /// носителей. Раньше один такой файл получал «непредвиденную ошибку»
    /// вместо честного результата проверки.
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

                // WAL: проверка пишет часто, а читает редко — так записи не
                // блокируют друг друга и база переживает аварийное закрытие.
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
            // Путь к базе берётся из настроек, то есть его мог править человек:
            // ловим и то, чем отвечают Path и Directory на негодный путь, а не
            // только ошибки самой SQLite.
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
                // Повреждённая база не должна ломать проверку: считаем, что
                // файл встречается впервые.
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
                // Не записалось — проверка от этого не должна падать.
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
                // Очистку вызывает кнопка в настройках. Исключение отсюда
                // осталось бы непойманным и уронило бы программу — а не
                // стёршаяся история этого не стоит.
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
        // Под замком — иначе закрытие может прийтись на середину запроса.
        // Случай не выдуманный: проверка пишет в базу из рабочих потоков, а
        // выключатель «Следить за порчей» в настройках закрывает её из потока
        // окна. Раньше это роняло соединение прямо под работающим запросом.
        lock (_lock)
        {
            _connection?.Dispose();
            _connection = null;
            DatabasePath = string.Empty;
        }
    }

    /// <summary>Переводит дату в файловое время, не спотыкаясь о негодные значения.</summary>
    private static long ToFileTime(DateTime value) =>
        value.ToUniversalTime() <= FileTimeEpoch ? 0 : value.ToFileTimeUtc();

    /// <summary>Обратный перевод — с той же оглядкой на негодные значения.</summary>
    private static DateTime FromFileTime(long value) =>
        value <= 0 ? FileTimeEpoch : DateTime.FromFileTimeUtc(value);

    /// <summary>Время проверки: за границами диапазона берётся начало отсчёта.</summary>
    private static DateTimeOffset FromUnixSeconds(long value) =>
        value < DateTimeOffset.MinValue.ToUnixTimeSeconds() || value > DateTimeOffset.MaxValue.ToUnixTimeSeconds()
            ? DateTimeOffset.UnixEpoch
            : DateTimeOffset.FromUnixTimeSeconds(value);
}
