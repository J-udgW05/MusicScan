namespace MusicScanIntegrity.Core.Analysis;

/// <summary>
/// How high the audio in a file reaches.
/// </summary>
/// <param name="CutoffHz">Upper energy edge in hertz.</param>
/// <param name="NyquistHz">Highest frequency the file could store at all.</param>
/// <param name="Blocks">How many blocks were measured.</param>
public sealed record SpectrumProfile(double CutoffHz, double NyquistHz, int Blocks)
{
    /// <summary>Nothing was measured.</summary>
    public static readonly SpectrumProfile Empty = new(0, 0, 0);

    /// <summary>Enough was measured to draw a conclusion.</summary>
    /// <remarks>
    /// Fewer than eight blocks is a fraction of a second, where a single quiet
    /// passage moves the edge and no conclusion holds.
    /// </remarks>
    public bool IsReliable => Blocks >= 8 && NyquistHz > 0;

    /// <summary>Occupied share of the band; 1 means the audio reaches the format limit.</summary>
    public double BandShare => NyquistHz <= 0 ? 0 : CutoffHz / NyquistHz;
}

/// <summary>
/// Measures the spectrum of audio blocks and finds its upper edge.
/// </summary>
/// <remarks>
/// <para>
/// Answers one question: how high does the audio reach. Lossy compression cuts
/// the top — 320 kbps near 20 kHz, 128 kbps near 16 kHz — so a "lossless" file
/// stopping at 16 kHz was almost certainly built from MP3. Almost: old and
/// deliberately narrow-band recordings lack top end without any re-encoding, so
/// the conclusion is a suspicion rather than a verdict.
/// </para>
/// <para>
/// The edge is computed per block and the largest one wins. Averaging would be
/// wrong: a symphony has more quiet passages than loud ones, and the mean would
/// show missing top end where the climaxes have it. The question is not how
/// much top end there is but whether it ever appears — in a file built from a
/// lossy source it never does.
/// </para>
/// </remarks>
public sealed class SpectrumAnalyzer(int sampleRate, int channels)
{
    /// <summary>Block size; 4096 samples give about 11 Hz resolution at 44.1 kHz.</summary>
    private const int BlockSize = 4096;

    /// <summary>How many blocks to measure; beyond this the spread is already smooth.</summary>
    private const int MaxBlocks = 64;

    /// <summary>
    /// How far below the loudest bin the spectral edge may sit and still count
    /// as audio: −65 dB.
    /// </summary>
    private const double EdgeThreshold = 3.16e-7;

    /// <summary>Consecutive bins required above the threshold; guards against single spikes.</summary>
    private const int EdgeRun = 3;

    /// <summary>Below this mean level a block is too quiet to judge bandwidth.</summary>
    private const double QuietBlockRms = 0.005;

    private readonly double[] _window = Fft.HannWindow(BlockSize);
    private readonly double[] _power = new double[(BlockSize / 2) + 1];
    private readonly double[] _block = new double[BlockSize];
    private readonly double[] _real = new double[BlockSize];
    private readonly double[] _imaginary = new double[BlockSize];

    private int _filled;
    private int _blocks;
    private double _highestCutoff;

    /// <summary>Feeds in the next batch of interleaved samples.</summary>
    /// <param name="samples">Samples in the −1…1 range.</param>
    public void Add(ReadOnlySpan<float> samples)
    {
        if (_blocks >= MaxBlocks || channels <= 0)
        {
            return;
        }

        for (int i = 0; i + channels <= samples.Length; i += channels)
        {
            // Channels are summed: the spectrum is about content rather than
            // the stereo image, so one channel is enough.
            double sum = 0;
            for (int c = 0; c < channels; c++)
            {
                sum += samples[i + c];
            }

            _block[_filled++] = sum / channels;

            if (_filled < BlockSize)
            {
                continue;
            }

            Accumulate();
            _filled = 0;

            if (_blocks >= MaxBlocks)
            {
                return;
            }
        }
    }

    /// <summary>Produces the result: how high the audio carries energy.</summary>
    /// <returns>The spectral profile.</returns>
    public SpectrumProfile Build() => _blocks == 0 || sampleRate <= 0
        ? SpectrumProfile.Empty
        : new SpectrumProfile(_highestCutoff, sampleRate / 2.0, _blocks);

    /// <summary>Computes one block's spectrum and records its upper edge.</summary>
    private void Accumulate()
    {
        double squareSum = 0;

        for (int i = 0; i < BlockSize; i++)
        {
            squareSum += _block[i] * _block[i];
            _real[i] = _block[i] * _window[i];
            _imaginary[i] = 0;
        }

        // A quiet block says nothing about bandwidth: it lacks top end simply
        // because there is nothing sounding.
        if (Math.Sqrt(squareSum / BlockSize) < QuietBlockRms)
        {
            return;
        }

        Fft.Forward(_real, _imaginary);

        double peak = 0;
        for (int bin = 0; bin < _power.Length; bin++)
        {
            _power[bin] = (_real[bin] * _real[bin]) + (_imaginary[bin] * _imaginary[bin]);

            if (_power[bin] > peak)
            {
                peak = _power[bin];
            }
        }

        _blocks++;

        if (peak <= 0)
        {
            return;
        }

        double threshold = peak * EdgeThreshold;
        double nyquist = sampleRate / 2.0;
        int run = 0;

        for (int bin = _power.Length - 1; bin >= 0; bin--)
        {
            if (_power[bin] >= threshold)
            {
                run++;

                if (run < EdgeRun)
                {
                    continue;
                }

                // The edge is the highest of the consecutive bins above the threshold.
                double cutoff = Math.Min((bin + EdgeRun - 1) * nyquist / (_power.Length - 1), nyquist);

                if (cutoff > _highestCutoff)
                {
                    _highestCutoff = cutoff;
                }

                return;
            }

            run = 0;
        }
    }
}
