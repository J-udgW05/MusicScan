using MusicScanIntegrity.Core.Models;
using MusicScanIntegrity.Core.Resources;

namespace MusicScanIntegrity.Core.Audio;

/// <summary>
/// Attempts to read a file as audio. The abstraction exists so the scan engine
/// can be tested in full without the native BASS library.
/// </summary>
public interface IAudioProbe
{
    /// <summary>Whether the decoder is ready.</summary>
    bool IsAvailable { get; }

    /// <summary>Initialises the decoder; called once at startup.</summary>
    /// <returns>An error description on failure, otherwise <see langword="null"/>.</returns>
    string? Initialize();

    /// <summary>
    /// Decodes the file to the requested depth. Never throws on a bad file;
    /// problems come back in the result.
    /// </summary>
    /// <param name="filePath">Path to the file.</param>
    /// <param name="scope">How much to read: start, samples or everything.</param>
    /// <param name="cancellationToken">Cancelled by stop or this file's timeout.</param>
    AudioProbeResult Probe(string filePath, DecodeScope scope, CancellationToken cancellationToken);
}

/// <summary>How much audio to read from a file.</summary>
public enum DecodeScope
{
    /// <summary>The first two seconds.</summary>
    Quick,

    /// <summary>Several windows: start, end and middle.</summary>
    Sampled,

    /// <summary>The whole file.</summary>
    Full,
}

/// <summary>Outcome of decoding a file.</summary>
/// <param name="Message">Human wording of the problem.</param>
/// <param name="TechnicalDetail">Technical cause, such as a BASS error code.</param>
/// <param name="DecodedSeconds">Seconds of audio actually read.</param>
/// <param name="DetectedFormat">Format the decoder identified.</param>
/// <param name="DeclaredSeconds">Duration the header claims.</param>
/// <param name="Truncated">Audio ended before the header promised.</param>
/// <param name="BitrateKbps">Bitrate per the decoder; 0 when unknown.</param>
public sealed record AudioProbeResult(
    AudioProbeOutcome Outcome,
    string? Message = null,
    string? TechnicalDetail = null,
    double DecodedSeconds = 0,
    string? DetectedFormat = null,
    double DeclaredSeconds = 0,
    bool Truncated = false,
    AudioStats? Stats = null,
    Analysis.SpectrumProfile? Spectrum = null,
    int BitrateKbps = 0)
{
    /// <summary>The file was read as audio successfully.</summary>
    public static AudioProbeResult Success(double seconds, string? format, double declared = 0) =>
        new(AudioProbeOutcome.Ok, DecodedSeconds: seconds, DetectedFormat: format, DeclaredSeconds: declared);

    /// <summary>Finding for this outcome; <see langword="null"/> on success.</summary>
    public CheckIssue? ToIssue() => Outcome switch
    {
        AudioProbeOutcome.Ok => null,

        AudioProbeOutcome.OpenFailed => new CheckIssue(
            IssueCode.DecodeStartFailed,
            Message ?? Strings.Probe_DecodeStartFailed,
            TechnicalDetail),

        AudioProbeOutcome.ReadFailed => new CheckIssue(
            IssueCode.AudioReadFailed,
            Message ?? Strings.Probe_AudioReadFailed,
            TechnicalDetail),

        AudioProbeOutcome.PasswordProtected => new CheckIssue(
            IssueCode.PasswordProtected,
            Message ?? Strings.Probe_PasswordProtected,
            TechnicalDetail),

        AudioProbeOutcome.Empty => new CheckIssue(
            IssueCode.EmptyFile,
            Message ?? Strings.Probe_EmptyFile,
            TechnicalDetail),

        AudioProbeOutcome.EngineFailure => new CheckIssue(
            IssueCode.UnexpectedError,
            Message ?? Strings.Probe_EngineFailure,
            TechnicalDetail),

        _ => new CheckIssue(IssueCode.UnexpectedError, Message ?? Strings.Probe_Unknown, TechnicalDetail),
    };
}

/// <summary>Result of a decode attempt.</summary>
public enum AudioProbeOutcome
{
    /// <summary>Read as audio.</summary>
    Ok,

    /// <summary>The decode stream could not be opened at all.</summary>
    OpenFailed,

    /// <summary>The stream opened, but reading the data failed.</summary>
    ReadFailed,

    /// <summary>Password- or DRM-protected.</summary>
    PasswordProtected,

    /// <summary>The file holds no data.</summary>
    Empty,

    /// <summary>
    /// Critical decoder failure; the scan cannot continue.
    /// </summary>
    EngineFailure,
}
