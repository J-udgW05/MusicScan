namespace MusicScanIntegrity.Core.Integrity;

/// <summary>
/// Validates a file against its own format: checksums and container
/// integrity.
/// </summary>
public interface IContainerIntegrityChecker
{
    /// <summary>Validates a file, picking the validator from its contents.</summary>
    /// <returns>The verdict; unknown formats yield "no validator".</returns>
    ContainerValidation Check(string filePath, CancellationToken cancellationToken);
}

/// <inheritdoc cref="IContainerIntegrityChecker" />
/// <remarks>
/// The format comes from the contents, not the extension: a file named .flac
/// holding MP3 data must be checked by MP3 rules, or the verdict would report
/// damage that is not there.
/// </remarks>
public sealed class ContainerIntegrityChecker : IContainerIntegrityChecker
{
    /// <summary>
    /// Validators in probe order. MP3 goes last: its signature is eleven set
    /// bits, which turns up by accident at the start of other formats more
    /// often than one would like.
    /// </summary>
    private static readonly IContainerValidator[] Validators =
    [
        new FlacValidator(),
        new OggValidator(),
        new Mp4Validator(),
        new RiffValidator(),
        new WavPackValidator(),
        new ApeValidator(),
        new Mp3Validator(),
    ];

    /// <summary>Bytes read to identify the format.</summary>
    private const int HeaderSize = 16;

    /// <inheritdoc />
    public ContainerValidation Check(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            using FileStream stream = new(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                64 * 1024,
                FileOptions.SequentialScan);

            long length = stream.Length;

            if (length < HeaderSize)
            {
                return ContainerValidation.NotSupported("неизвестный");
            }

            ContainerBounds bounds = ContainerBounds.Measure(stream, length);
            IContainerValidator? validator = SelectValidator(stream, bounds);

            if (validator is null)
            {
                return ContainerValidation.NotSupported("неизвестный");
            }

            return validator.Validate(stream, bounds, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return ContainerValidation.Unreadable("неизвестный", $"{ex.GetType().Name} · {ex.Message}");
        }
    }

    /// <summary>Picks a validator by the signature at the start of the audio data.</summary>
    /// <remarks>
    /// The signature is read from where the validator will start — past the
    /// tag, not at byte zero. Otherwise the format would be identified in one
    /// place and parsed from another, condemning healthy tagged files.
    /// </remarks>
    private static IContainerValidator? SelectValidator(FileStream stream, ContainerBounds bounds)
    {
        byte[] header = new byte[HeaderSize];
        stream.Position = bounds.AudioStart;

        IContainerValidator? found = stream.ReadAtLeast(header, HeaderSize, throwOnEndOfStream: false) == HeaderSize
            ? Find(header)
            : null;

        stream.Position = 0;
        return found;
    }

    private static IContainerValidator? Find(ReadOnlySpan<byte> header)
    {
        foreach (IContainerValidator validator in Validators)
        {
            if (validator.Matches(header))
            {
                return validator;
            }
        }

        return null;
    }
}
