
namespace MusicScanIntegrity.Core.Locking;

/// <summary>Temporary copies of locked files.</summary>
public interface ITempCopyManager
{
    /// <summary>Folder the temporary copies are created in.</summary>
    string TempFolder { get; }

    /// <summary>
    /// Makes a temporary copy. The copy is always removed when the returned
    /// object is disposed, however the check ends.
    /// </summary>
    Task<TempCopy> CreateAsync(string sourcePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes orphaned copies left by a previous run that crashed before
    /// cleaning up.
    /// </summary>
    /// <returns>How many files were removed.</returns>
    int CleanupOrphans();
}

/// <summary>
/// A temporary copy of a file, removed in <see cref="DisposeAsync"/>; always
/// use it through <c>await using</c>.
/// </summary>
public sealed class TempCopy : IAsyncDisposable
{
    internal TempCopy(string path, bool created, string? error)
    {
        Path = path;
        Created = created;
        Error = error;
    }

    /// <summary>Path to the copy, or to the original when copying failed.</summary>
    public string Path { get; }

    /// <summary>A copy was actually made.</summary>
    public bool Created { get; }

    /// <summary>Why the copy could not be made.</summary>
    public string? Error { get; }

    /// <summary>No copy; a stub carrying the reason.</summary>
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
            // Could not delete now; CleanupOrphans will get it next start.
        }

        return ValueTask.CompletedTask;
    }
}

/// <inheritdoc cref="ITempCopyManager" />
public sealed class TempCopyManager : ITempCopyManager
{
    private const string CopyExtension = ".tmp";

    /// <summary>Creates the temporary copy manager.</summary>
    /// <param name="tempFolder">Folder for copies; defaults to %TEMP%\MusicScanIntegrity.</param>
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
                    // Still locked; probably a second instance is running.
                }
                catch (UnauthorizedAccessException)
                {
                    // No delete permission; not our copy, leave it alone.
                }
            }
        }
        catch (Exception)
        {
            // The walk can fail midway: folder deleted, drive removed, no
            // permission. Cleanup is not worth blocking startup, so return
            // whatever was managed.
        }

        return removed;
    }

    /// <summary>Deletes a temporary copy, staying quiet on failure.</summary>
    /// <remarks>
    /// The copy sits in the application's temp folder and
    /// <see cref="CleanupOrphans" /> will collect it next start. A failure here
    /// does not affect the scan result and has nobody to report to.
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
