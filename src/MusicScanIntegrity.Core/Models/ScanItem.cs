namespace MusicScanIntegrity.Core.Models;

/// <summary>
/// A candidate found by the folder walk. Deliberately lightweight: a
/// collection of several hundred thousand files creates that many of these.
/// </summary>
/// <param name="SizeBytes">Size reported by the walk; -1 when it could not be read.</param>
public readonly record struct ScanItem(string FullPath, long SizeBytes, ScanItemKind Kind)
{
    /// <summary>Lower-case extension including the dot, for example ".flac".</summary>
    public string Extension => Path.GetExtension(FullPath).ToLowerInvariant();
}
