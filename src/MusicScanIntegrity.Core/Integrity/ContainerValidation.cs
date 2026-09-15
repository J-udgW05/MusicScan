namespace MusicScanIntegrity.Core.Integrity;

/// <summary>Outcome of validating a file against its own format.</summary>
public enum ContainerVerdict
{
    /// <summary>No validator for this format; there is no verdict.</summary>
    NotSupported,

    /// <summary>
    /// Structure is intact, but the format carries no checksums, or they
    /// cannot be verified without decompressing. Weaker than a matched sum.
    /// </summary>
    StructureOnly,

    /// <summary>Checksums matched: the file is what was encoded.</summary>
    Verified,

    /// <summary>Damage found: a checksum mismatch or broken structure.</summary>
    Damaged,

    /// <summary>The file could not be read; nothing is known about its contents.</summary>
    Unreadable,
}

/// <summary>How the file is damaged, which decides the wording shown to the user.</summary>
public enum ContainerDamage
{
    /// <summary>No damage.</summary>
    None,

    /// <summary>Broken structure: frames, pages or blocks do not line up.</summary>
    Structure,

    /// <summary>A checksum did not match.</summary>
    Checksum,

    /// <summary>Truncated: less data than the header promises.</summary>
    Truncation,
}

/// <summary>
/// Result of validating a file against its own checksums and container
/// structure.
/// </summary>
/// <param name="Format">Format as the validator identified it.</param>
/// <param name="TechnicalDetail">Offset plus expected and actual checksum.</param>
/// <param name="UnitsChecked">How many frames, pages or blocks were checked.</param>
/// <param name="ErrorOffset">Offset of the first error from the start of the file.</param>
/// <param name="TrailingBytes">Extra bytes left after the last frame.</param>
public sealed record ContainerValidation(
    ContainerVerdict Verdict,
    string Format,
    string? Message = null,
    string? TechnicalDetail = null,
    int UnitsChecked = 0,
    long? ErrorOffset = null,
    bool Truncated = false,
    long TrailingBytes = 0,
    ContainerDamage Damage = ContainerDamage.None)
{
    /// <summary>No validator for the format.</summary>
    public static ContainerValidation NotSupported(string format) =>
        new(ContainerVerdict.NotSupported, format);

    /// <summary>Checksums matched.</summary>
    public static ContainerValidation Verified(string format, int units, long trailing = 0) =>
        new(ContainerVerdict.Verified, format, UnitsChecked: units, TrailingBytes: trailing);

    /// <summary>Structure intact, but the format has no checksums.</summary>
    public static ContainerValidation StructureOnly(string format, int units, string? detail = null) =>
        new(ContainerVerdict.StructureOnly, format, TechnicalDetail: detail, UnitsChecked: units);

    /// <summary>Damage was found.</summary>
    public static ContainerValidation Damaged(
        string format,
        string message,
        string? detail = null,
        int units = 0,
        long? offset = null,
        bool truncated = false,
        ContainerDamage damage = ContainerDamage.Structure) =>
        new(
            ContainerVerdict.Damaged,
            format,
            message,
            detail,
            units,
            offset,
            truncated,
            Damage: truncated ? ContainerDamage.Truncation : damage);

    /// <summary>The file cannot be read.</summary>
    public static ContainerValidation Unreadable(string format, string detail) =>
        new(ContainerVerdict.Unreadable, format, TechnicalDetail: detail);
}
