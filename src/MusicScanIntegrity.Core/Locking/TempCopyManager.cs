
namespace MusicScanIntegrity.Core.Locking;

/// <summary>Временные копии занятых файлов.</summary>
public interface ITempCopyManager
{
    /// <summary>Папка, в которой создаются временные копии.</summary>
    string TempFolder { get; }

    /// <summary>
    /// Делает временную копию файла. Копия обязательно удаляется
    /// при освобождении возвращённого объекта — чем бы ни закончилась проверка
    /// (03_IMPLEMENTATION_GUIDE.md, раздел 2).
    /// </summary>
    Task<TempCopy> CreateAsync(string sourcePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Удаляет «осиротевшие» копии от предыдущего запуска, который завершился
    /// аварийно и не успел убрать за собой.
    /// </summary>
    /// <returns>Сколько файлов удалось удалить.</returns>
    int CleanupOrphans();
}

/// <summary>
/// Временная копия файла. Удаляется в <see cref="DisposeAsync"/> — вызывать
/// обязательно, поэтому объект всегда используется через <c>await using</c>.
/// </summary>
public sealed class TempCopy : IAsyncDisposable
{
    internal TempCopy(string path, bool created, string? error)
    {
        Path = path;
        Created = created;
        Error = error;
    }

    /// <summary>Путь к копии (или к исходному файлу, если скопировать не удалось).</summary>
    public string Path { get; }

    /// <summary>Копия действительно создана.</summary>
    public bool Created { get; }

    /// <summary>Почему копию создать не удалось.</summary>
    public string? Error { get; }

    /// <summary>Копия не создана — вернулась заглушка с описанием причины.</summary>
    public static TempCopy Failed(string sourcePath, string error) => new(sourcePath, false, error);

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (!Created)
        {
            return ValueTask.CompletedTask;
        }

        try
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
        catch (Exception)
        {
            // Не смогли удалить сейчас — уберём при следующем запуске в CleanupOrphans.
        }

        return ValueTask.CompletedTask;
    }
}

/// <inheritdoc cref="ITempCopyManager" />
public sealed class TempCopyManager : ITempCopyManager
{
    private const string CopyExtension = ".tmp";

    /// <summary>Создаёт менеджер временных копий.</summary>
    /// <param name="tempFolder">Папка для копий (по умолчанию %TEMP%\MusicScanIntegrity).</param>
    public TempCopyManager(string? tempFolder = null)
    {
        TempFolder = tempFolder ?? Path.Combine(Path.GetTempPath(), "MusicScanIntegrity");
    }

    /// <inheritdoc />
    public string TempFolder { get; }

    /// <inheritdoc />
    public async Task<TempCopy> CreateAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        string target = Path.Combine(TempFolder, Guid.NewGuid().ToString("N")[..12] + CopyExtension);

        try
        {
            Directory.CreateDirectory(TempFolder);

            await using FileStream source = new(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 1024 * 128,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            await using FileStream destination = new(
                target,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 128,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);

            return new TempCopy(target, created: true, error: null);
        }
        catch (OperationCanceledException)
        {
            TryDelete(target);
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(target);
            return TempCopy.Failed(sourcePath, $"{ex.GetType().Name} · {ex.Message}");
        }
    }

    /// <inheritdoc />
    public int CleanupOrphans()
    {
        if (!Directory.Exists(TempFolder))
        {
            return 0;
        }

        int removed = 0;

        try
        {
            foreach (string file in Directory.EnumerateFiles(TempFolder, "*" + CopyExtension))
            {
                try
                {
                    File.Delete(file);
                    removed++;
                }
                catch (IOException)
                {
                    // Файл всё ещё занят — вероятно, работает вторая копия программы.
                }
                catch (UnauthorizedAccessException)
                {
                    // Нет прав на удаление — не наша копия, не трогаем.
                }
            }
        }
        catch (Exception)
        {
            // Обход папки мог сорваться на полпути: папку удалили, диск отняли,
            // прав не хватило. Уборка мусора — не то дело, ради которого стоит
            // мешать запуску программы, поэтому возвращаем что успели.
        }

        return removed;
    }

    /// <summary>Удаляет временную копию, не поднимая шума, если не вышло.</summary>
    /// <remarks>
    /// Копия лежит в папке программы и будет подобрана уборкой при следующем
    /// запуске: <see cref="CleanupOrphans" /> для того и есть. Сообщать о
    /// неудаче некому и незачем — на результат проверки она не влияет.
    /// </remarks>
    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
        }
    }
}
