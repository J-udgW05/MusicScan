namespace MusicScanIntegrity.Core.Locking;

/// <summary>Можно ли прочитать файл прямо сейчас.</summary>
public enum FileAccessState
{
    /// <summary>Файл доступен для чтения.</summary>
    Available,

    /// <summary>Файла нет на диске.</summary>
    NotFound,

    /// <summary>Файл занят другой программой.</summary>
    Locked,

    /// <summary>Операционная система запретила доступ (права, политика).</summary>
    AccessDenied,
}

/// <summary>Результат проверки доступа.</summary>
/// <param name="State">Состояние.</param>
/// <param name="TechnicalDetail">Техническая причина, если доступа нет.</param>
public readonly record struct FileAccessCheck(FileAccessState State, string? TechnicalDetail = null);

/// <summary>Проверяет, доступен ли файл для чтения, не читая его целиком.</summary>
public static class FileAccessProbe
{
    /// <summary>
    /// Пробует открыть файл на чтение.
    /// </summary>
    /// <remarks>
    /// Решение по неоднозначности: 02_ARCHITECTURE.md говорит про «эксклюзивный
    /// доступ на чтение», но открытие с <see cref="FileShare.None"/> провалилось бы
    /// на любом файле, который другая программа держит открытым даже только на чтение
    /// (например, проигрыватель с открытым плейлистом) — при том что декодировать
    /// такой файл прекрасно можно. Поэтому «занятым» файл считается только тогда,
    /// когда его действительно не получается открыть на чтение с разделением
    /// <see cref="FileShare.ReadWrite"/> — то есть когда владелец запретил чтение.
    /// Это и есть ситуация, ради которой в спецификации описан диалог о занятом файле.
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
            // ERROR_SHARING_VIOLATION (32) и ERROR_LOCK_VIOLATION (33) — файл занят;
            // остальные IO-ошибки тоже мешают чтению, но по другой причине.
            int code = ex.HResult & 0xFFFF;
            return code is 32 or 33
                ? new FileAccessCheck(FileAccessState.Locked, $"Win32 error {code} · {ex.Message}")
                : new FileAccessCheck(FileAccessState.AccessDenied, $"IOException 0x{ex.HResult:X8} · {ex.Message}");
        }
    }
}
