using System.IO;
using MusicScanIntegrity.Core.Models;
using CoreFormat = MusicScanIntegrity.Core.Common.Format;

namespace MusicScanIntegrity.App.ViewModels;

/// <summary>A row in the results table.</summary>
/// <remarks>
/// Immutable and without <c>INotifyPropertyChanged</c> on purpose: there can be
/// hundreds of thousands of rows, and a result never changes once it arrives.
/// </remarks>
public sealed class FileResultViewModel(FileCheckResult result)
{
    /// <summary>The underlying check result.</summary>
    public FileCheckResult Result { get; } = result;

    public string FileName => Result.FileName;

    /// <summary>Folder containing the file.</summary>
    public string DirectoryPath => Result.DirectoryPath;

    public string FullPath => Result.FullPath;

    /// <summary>Format, e.g. "FLAC", or "FLAC?" when it disagrees with the extension.</summary>
    public string Format => Result.Format;

    /// <summary>Human-readable size.</summary>
    public string Size => CoreFormat.Size(Result.SizeBytes);

    /// <summary>Size in bytes; the size column sorts on this.</summary>
    public long SizeBytes => Result.SizeBytes;

    public CheckStatus Status => Result.Status;

    /// <summary>Status caption; specific when there is a single finding.</summary>
    public string StatusLabel => Result.StatusLabel;

    /// <summary>Status glyph for the 20×20 square in the row.</summary>
    public string StatusIcon => Result.Status.MarkKey();

    /// <summary>Human-readable description for the details pane.</summary>
    public string Description => Result.Description;

    /// <summary>Technical cause, the second line in the details pane.</summary>
    public string? TechnicalDetail => Result.TechnicalDetail;

    /// <summary>Details pane heading: "name · status".</summary>
    public string DetailsTitle => $"{FileName} · {StatusLabel}";

    /// <summary>Upper-case extension used by the format filter.</summary>
    public string Extension => Path.GetExtension(Result.FullPath).TrimStart('.').ToUpperInvariant();
}
