using System.Diagnostics;
using MusicScanIntegrity.Core.Models;
using MusicScanIntegrity.Core.Resources;
using MusicScanIntegrity.Core.Settings;

namespace MusicScanIntegrity.Core.Discovery;

/// <summary>Fast folder walk that decodes nothing.</summary>
public interface IFileDiscoveryService
{
    /// <summary>
    /// Walks the folder and collects candidates for checking. No file is
    /// opened; this is directory enumeration only.
    /// </summary>
    Task<DiscoveryResult> DiscoverAsync(
        string rootPath,
        AppSettings settings,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Walks with its own stack rather than <c>Directory.EnumerateFiles(recursive)</c>.
/// </summary>
/// <remarks>
/// The built-in recursive walk aborts entirely on the first inaccessible
/// folder and cannot report which folders were skipped. This one survives a
/// denied folder, carries on and reports it.
/// </remarks>
public sealed class FileDiscoveryService : IFileDiscoveryService
{
    /// <summary>How often walk progress is reported.</summary>
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

        // Walking the disk is synchronous and slow; keep it off the UI thread.
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
            inaccessible.Add(new InaccessibleFolder(rootPath, Strings.Folder_NotFound, "DirectoryNotFound"));
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
                        // Reparse points (symlinks, junctions) are skipped, or
                        // the walk can loop back on itself.
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
                    Strings.Folder_AccessDenied,
                    $"UnauthorizedAccessException · {ex.Message}"));
            }
            catch (IOException ex)
            {
                inaccessible.Add(new InaccessibleFolder(
                    folder,
                    Strings.Folder_Unreadable,
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
            // A missing size is no reason to stop; the check reads it later.
            return -1;
        }
    }
}
