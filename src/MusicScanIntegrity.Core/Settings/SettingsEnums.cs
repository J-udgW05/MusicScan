namespace MusicScanIntegrity.Core.Settings;

/// <summary>Application theme.</summary>
public enum AppTheme
{
    /// <summary>Follow the Windows theme.</summary>
    System,

    /// <summary>Always light.</summary>
    Light,

    /// <summary>Always dark.</summary>
    Dark,
}

/// <summary>What to do when a file is locked by another process.</summary>
public enum LockedFileAction
{
    /// <summary>Ask the user, one file at a time. The default.</summary>
    Ask,

    /// <summary>Always skip.</summary>
    Skip,

    /// <summary>Wait for the lock to clear and retry.</summary>
    Wait,

    /// <summary>Check a temporary copy instead.</summary>
    TempCopy,

    /// <summary>Name the owning process and offer to close it.</summary>
    CloseOwner,
}

/// <summary>Export format of the report.</summary>
public enum ReportFormat
{
    /// <summary>Styled HTML page carrying the same status colours.</summary>
    Html,

    /// <summary>CSV table for Excel and further processing.</summary>
    Csv,

    /// <summary>Plain text list for reading.</summary>
    Text,
}

/// <summary>How much of each file the decoder reads.</summary>
public enum CheckDepth
{
    /// <summary>The first two seconds only. Fast, but deaf to the middle.</summary>
    Quick,

    /// <summary>Start, end and a few places in between.</summary>
    Sampled,

    /// <summary>The whole file. Most accurate and slowest.</summary>
    Full,
}

/// <summary>Row density of the results table.</summary>
public enum ListDensity
{
    /// <summary>Normal.</summary>
    Normal,

    /// <summary>Compact.</summary>
    Compact,
}
