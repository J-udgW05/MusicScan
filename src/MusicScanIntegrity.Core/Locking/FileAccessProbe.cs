namespace MusicScanIntegrity.Core.Locking;

/// <summary>Whether the file can be read right now.</summary>
public enum FileAccessState
{
    /// <summary>Readable.</summary>
    Available,

    /// <summary>Not present on disk.</summary>
    NotFound,

    /// <summary>Locked by another process.</summary>
    Locked,

    /// <summary>The OS denied access: permissions or policy.</summary>
    AccessDenied,
}

/// <summary>Outcome of the access probe.</summary>
/// <param name="TechnicalDetail">Technical cause when access was denied.</param>
public readonly record struct FileAccessCheck(FileAccessState State, string? TechnicalDetail = null);

/// <summary>Checks whether a file is readable without reading all of it.</summary>
public static class FileAccessProbe
{
    /// <summary>
    /// Tries to open the file for reading.
    /// </summary>
    /// <remarks>
    /// Opening with <see cref="FileShare.None"/> would fail on any file another
    /// process merely holds open for reading — a player with a playlist open,
    /// say — even though such a file decodes perfectly well. A file therefore
    /// counts as locked only when it cannot be opened with
    /// <see cref="FileShare.ReadWrite"/>, meaning the owner denied reads.
    /// </remarks>
    public static FileAccessCheck Check(string filePath)
    {
        try
        {
            using FileStream stream = new(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 1,
                FileOptions.None);

            return new FileAccessCheck(FileAccessState.Available);
        }
        catch (FileNotFoundException ex)
        {
            return new FileAccessCheck(FileAccessState.NotFound, ex.Message);
        }
        catch (DirectoryNotFoundException ex)
        {
            return new FileAccessCheck(FileAccessState.NotFound, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new FileAccessCheck(FileAccessState.AccessDenied, $"UnauthorizedAccessException · {ex.Message}");
        }
        catch (IOException ex)
        {
            // ERROR_SHARING_VIOLATION (32) and ERROR_LOCK_VIOLATION (33) mean
            // locked; other IO errors also block reads, for other reasons.
            int code = ex.HResult & 0xFFFF;
            return code is 32 or 33
                ? new FileAccessCheck(FileAccessState.Locked, $"Win32 error {code} · {ex.Message}")
                : new FileAccessCheck(FileAccessState.AccessDenied, $"IOException 0x{ex.HResult:X8} · {ex.Message}");
        }
    }
}
