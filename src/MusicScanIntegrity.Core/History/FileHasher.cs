using System.Globalization;
using System.IO.Hashing;

namespace MusicScanIntegrity.Core.History;

/// <summary>
/// Fingerprints file contents.
/// </summary>
/// <remarks>
/// XxHash3 rather than SHA-256: the only requirement is noticing that contents
/// changed, which needs no cryptographic strength. The speed difference is
/// large enough that SHA-256 would make a hundred-gigabyte collection
/// CPU-bound instead of disk-bound.
/// </remarks>
public static class FileHasher
{
    /// <summary>Read buffer size.</summary>
    private const int BufferSize = 1024 * 1024;

    /// <summary>Computes the fingerprint of a file.</summary>
    /// <param name="filePath">Path to the file.</param>
    /// <param name="cancellationToken">Cancelled by stop or by the per-file timeout.</param>
    /// <returns>Hexadecimal fingerprint, or <see langword="null" /> when the file could not be read.</returns>
    public static string? Compute(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            using FileStream stream = new(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                BufferSize,
                FileOptions.SequentialScan);

            XxHash3 hash = new();
            byte[] buffer = new byte[BufferSize];

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    break;
                }

                hash.Append(buffer.AsSpan(0, read));
            }

            return hash.GetCurrentHashAsUInt64().ToString("x16", CultureInfo.InvariantCulture);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
