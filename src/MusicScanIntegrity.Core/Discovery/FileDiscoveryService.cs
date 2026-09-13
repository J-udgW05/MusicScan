using System.Diagnostics;
using MusicScanIntegrity.Core.Models;
using MusicScanIntegrity.Core.Settings;

namespace MusicScanIntegrity.Core.Discovery;

/// <summary>Быстрый обход папки без декодирования файлов.</summary>
public interface IFileDiscoveryService
{
    /// <summary>
    /// Обходит папку и собирает список кандидатов на проверку.
    /// Файлы не открываются — только перечисление каталога
    /// (02_ARCHITECTURE.md, раздел 9).
    /// </summary>
    Task<DiscoveryResult> DiscoverAsync(
        string rootPath,
        AppSettings settings,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Обход папки собственным стеком, а не <c>Directory.EnumerateFiles(recursive)</c>.
/// </summary>
/// <remarks>
/// Причина: встроенный рекурсивный обход падает целиком на первой же папке,
/// куда нет доступа, и не даёт сказать пользователю, какие именно папки пропущены.
/// Свой обход переживает недоступную папку, продолжает работу и честно сообщает
/// о ней (01_SPECIFICATION.md, раздел 9 — «честно сказать об этом пользователю»).
/// </remarks>
public sealed class FileDiscoveryService : IFileDiscoveryService
{
    /// <summary>Как часто сообщать о прогрессе обхода.</summary>
    private const int ProgressReportEvery = 500;

    private static readonly EnumerationOptions EnumerationOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.Device,
        MatchType = MatchType.Simple,
    };

    /// <inheritdoc />
    public Task<DiscoveryResult> DiscoverAsync(
        string rootPath,
        AppSettings settings,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(settings);

        // Обход диска — операция синхронная и долгая: уводим её с потока интерфейса.
        return Task.Run(() => Discover(rootPath, settings, progress, cancellationToken), cancellationToken);
    }

    private DiscoveryResult Discover(
        string rootPath,
        AppSettings settings,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        FormatSelection formats = AudioFormats.ForSettings(settings);

        List<ScanItem> audio = [];
        List<ScanItem> playlists = [];
        List<InaccessibleFolder> inaccessible = [];
        int folderCount = 0;

        if (!Directory.Exists(rootPath))
        {
            inaccessible.Add(new InaccessibleFolder(rootPath, "Папка не найдена.", "DirectoryNotFound"));
            return Build(rootPath, audio, playlists, folderCount, inaccessible, stopwatch.Elapsed);
        }

        Stack<string> pending = new();
        pending.Push(rootPath);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string folder = pending.Pop();
            folderCount++;

            try
            {
                foreach (FileSystemInfo entry in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", EnumerationOptions))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (entry is DirectoryInfo directory)
                    {
                        // Точки повторного разбора (симлинки, junction) пропускаем:
                        // иначе обход может зациклиться на самом себе.
                        if (settings.Recursive && !directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                        {
                            pending.Push(directory.FullName);
                        }

                        continue;
                    }

                    if (entry is not FileInfo file)
                    {
                        continue;
                    }

                    ScanItemKind? kind = formats.Classify(file.FullName);
                    if (kind is null)
                    {
                        continue;
                    }

                    long size = TryGetLength(file);
                    ScanItem item = new(file.FullName, size, kind.Value);

                    if (kind == ScanItemKind.Playlist)
                    {
                        playlists.Add(item);
                    }
                    else
                    {
                        audio.Add(item);
                    }

                    int total = audio.Count + playlists.Count;
                    if (progress is not null && total % ProgressReportEvery == 0)
                    {
                        progress.Report(total);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (UnauthorizedAccessException ex)
            {
                inaccessible.Add(new InaccessibleFolder(
                    folder,
                    "Windows отказал в доступе к папке.",
                    $"UnauthorizedAccessException · {ex.Message}"));
            }
            catch (IOException ex)
            {
                inaccessible.Add(new InaccessibleFolder(
                    folder,
                    "Папку не удалось прочитать.",
                    $"IOException · {ex.Message}"));
            }
        }

        progress?.Report(audio.Count + playlists.Count);
        stopwatch.Stop();

        return Build(rootPath, audio, playlists, folderCount, inaccessible, stopwatch.Elapsed);
    }

    private static DiscoveryResult Build(
        string rootPath,
        List<ScanItem> audio,
        List<ScanItem> playlists,
        int folderCount,
        List<InaccessibleFolder> inaccessible,
        TimeSpan duration) => new()
        {
            RootPath = rootPath,
            AudioItems = audio,
            Playlists = playlists,
            FolderCount = folderCount,
            InaccessibleFolders = inaccessible,
            Duration = duration,
        };

    private static long TryGetLength(FileInfo file)
    {
        try
        {
            return file.Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Размер — не повод прерывать обход; проверка узнает его сама.
            return -1;
        }
    }
}
