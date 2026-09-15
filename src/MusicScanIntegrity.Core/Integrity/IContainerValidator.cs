namespace MusicScanIntegrity.Core.Integrity;

/// <summary>
/// Validator for one family of formats: reads the file by its own rules and
/// reports whether the checksums match.
/// </summary>
/// <remarks>
/// This is not decoding: no audio is reconstructed, only headers, frame
/// boundaries and checksums are read. It is therefore bound by disk speed
/// rather than CPU, and definite where a decoder only says "it opened".
/// </remarks>
internal interface IContainerValidator
{
    /// <summary>Format name used in messages.</summary>
    string Format { get; }

    /// <summary>Whether the leading bytes identify a file this validator handles.</summary>
    bool Matches(ReadOnlySpan<byte> header);

    /// <summary>Validates an open file.</summary>
    /// <param name="stream">Stream positioned at the start of the file.</param>
    /// <param name="bounds">Where the audio data sits, excluding tags.</param>
    /// <remarks>
    /// Offsets in the verdict are measured from the start of the file, not the
    /// stream: that is where the reader looks for the damage.
    /// </remarks>
    ContainerValidation Validate(Stream stream, ContainerBounds bounds, CancellationToken cancellationToken);
}
