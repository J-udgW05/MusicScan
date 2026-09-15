namespace MusicScanIntegrity.Core.Audio;

/// <summary>
/// What the samples themselves show: level, clipping, DC offset and dropouts.
/// </summary>
/// <param name="Samples">How many samples were examined.</param>
/// <param name="Peak">Largest absolute value; 1 is full scale.</param>
/// <param name="Rms">Mean level.</param>
/// <param name="ClippedSamples">How many samples reached full scale.</param>
/// <param name="DcOffset">Mean value; close to zero in a healthy recording.</param>
/// <param name="LongestSilentRun">Longest silence inside audible material, in samples.</param>
/// <param name="SampleRate">Samples per second across all channels.</param>
public sealed record AudioStats(
    long Samples,
    double Peak,
    double Rms,
    long ClippedSamples,
    double DcOffset,
    long LongestSilentRun,
    int SampleRate)
{
    /// <summary>Nothing was measured.</summary>
    public static readonly AudioStats Empty = new(0, 0, 0, 0, 0, 0, 0);

    /// <summary>Share of samples at full scale.</summary>
    public double ClippedShare => Samples == 0 ? 0 : (double)ClippedSamples / Samples;

    /// <summary>
    /// Longest silence inside audible material, in seconds.
    /// </summary>
    /// <remarks>
    /// Silence at the start and the end does not count: nearly every track has
    /// an intro and a fade, and calling those dropouts would flag half a
    /// collection. A dropout is sound that stops and then returns.
    /// </remarks>
    public double LongestSilentSeconds => SampleRate <= 0 ? 0 : (double)LongestSilentRun / SampleRate;

    /// <summary>What was read is entirely silent.</summary>
    /// <remarks>
    /// The threshold is −60 dB rather than zero: real recordings keep
    /// conversion noise in quiet passages, and demanding exact zeroes would
    /// find nothing.
    /// </remarks>
    public bool IsSilent => Samples > 0 && Peak < 0.001;
}

/// <summary>
/// Accumulates audio statistics in a single pass over the samples.
/// </summary>
/// <remarks>
/// The samples pass through during decoding anyway, so these numbers are free
/// — no second disk read. Kept as its own type so it can be tested without
/// BASS and without audio files.
/// </remarks>
public sealed class AudioStatsAccumulator(int sampleRate)
{
    /// <summary>Level below which a sample counts as silence.</summary>
    /// <remarks>
    /// Not zero: a recording that passed through an analogue path almost never
    /// contains absolute zeroes, and a dropout is audible from −80 dB.
    /// </remarks>
    private const double SilenceLevel = 0.0001;

    /// <summary>Level from which a sample counts as clipped.</summary>
    private const double ClipLevel = 0.9995;

    private long _samples;
    private double _peak;
    private double _squareSum;
    private double _sum;
    private long _clipped;
    private long _silentRun;
    private long _longestSilentRun;
    private bool _sawSound;

    /// <summary>Feeds in the next batch of samples.</summary>
    /// <param name="samples">Samples in the −1…1 range.</param>
    public void Add(ReadOnlySpan<float> samples)
    {
        foreach (float value in samples)
        {
            double sample = value;
            double magnitude = Math.Abs(sample);

            _samples++;
            _sum += sample;
            _squareSum += sample * sample;

            if (magnitude > _peak)
            {
                _peak = magnitude;
            }

            if (magnitude >= ClipLevel)
            {
                _clipped++;
            }

            if (magnitude < SilenceLevel)
            {
                _silentRun++;
            }
            else
            {
                // Silence counts only once it ends and sound preceded it: the
                // intro and the fade are not dropouts.
                if (_sawSound && _silentRun > _longestSilentRun)
                {
                    _longestSilentRun = _silentRun;
                }

                _silentRun = 0;
                _sawSound = true;
            }
        }
    }

    /// <summary>Produces the result.</summary>
    /// <returns>Statistics for the audio that was read.</returns>
    public AudioStats Build() => _samples == 0
        ? AudioStats.Empty
        : new AudioStats(
            _samples,
            _peak,
            Math.Sqrt(_squareSum / _samples),
            _clipped,
            _sum / _samples,
            _longestSilentRun,
            sampleRate);
}
