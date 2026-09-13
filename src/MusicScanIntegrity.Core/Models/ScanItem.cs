namespace MusicScanIntegrity.Core.Models;

/// <summary>
/// Кандидат на проверку, найденный при быстром обходе папки.
/// Умышленно лёгкий тип: на коллекции в сотни тысяч файлов таких объектов
/// создаётся очень много (02_ARCHITECTURE.md, раздел 2 — расход памяти).
/// </summary>
/// <param name="FullPath">Полный путь к файлу.</param>
/// <param name="SizeBytes">Размер по данным обхода; -1, если размер узнать не удалось.</param>
/// <param name="Kind">Тип найденного объекта.</param>
public readonly record struct ScanItem(string FullPath, long SizeBytes, ScanItemKind Kind)
{
    /// <summary>Расширение в нижнем регистре с точкой, например «.flac».</summary>
    public string Extension => Path.GetExtension(FullPath).ToLowerInvariant();
}
