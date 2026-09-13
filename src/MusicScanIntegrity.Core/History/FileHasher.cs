using System.Globalization;
using System.IO.Hashing;

namespace MusicScanIntegrity.Core.History;

/// <summary>
/// Считает отпечаток содержимого файла.
/// </summary>
/// <remarks>
/// XxHash3, а не SHA-256: от хеша здесь нужно единственное — заметить, что
/// содержимое изменилось. Криптографическая стойкость для этого не нужна, а
/// разница в скорости заметная: коллекцию в сотню гигабайт SHA-256 считал бы
/// заметно дольше, и проверка упиралась бы уже не в диск, а в процессор.
/// </remarks>
public static class FileHasher
{
    /// <summary>Размер порции чтения.</summary>
    private const int BufferSize = 1024 * 1024;

    /// <summary>Считает отпечаток файла.</summary>
    /// <param name="filePath">Путь к файлу.</param>
    /// <param name="cancellationToken">Отмена: стоп или таймаут по файлу.</param>
    /// <returns>Отпечаток в шестнадцатеричном виде или <see langword="null" />, если файл не прочитался.</returns>
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
