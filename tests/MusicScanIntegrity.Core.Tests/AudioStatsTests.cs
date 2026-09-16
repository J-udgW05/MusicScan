using MusicScanIntegrity.Core.Audio;
using Xunit;

namespace MusicScanIntegrity.Core.Tests;

public sealed class AudioStatsTests
{
    private const int Rate = 48000;

    private static AudioStats Measure(params float[][] parts)
    {
        AudioStatsAccumulator accumulator = new(Rate);

        foreach (float[] part in parts)
        {
            accumulator.Add(part);
        }

        return accumulator.Build();
    }

    private static float[] Tone(double seconds, double amplitude = 0.5)
    {
        float[] samples = new float[(int)(Rate * seconds)];

        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)(amplitude * Math.Sin(2 * Math.PI * 440 * i / Rate));
        }

        return samples;
    }

    private static float[] Silence(double seconds) => new float[(int)(Rate * seconds)];

    [Fact]
    public void Empty_input_yields_zeros()
    {
        AudioStats stats = Measure();

        Assert.Equal(0, stats.Samples);
        Assert.False(stats.IsSilent);
    }

    [Fact]
    public void Digital_silence_shows_in_level()
    {
        AudioStats stats = Measure(Silence(3));

        Assert.True(stats.IsSilent);
        Assert.Equal(0, stats.Peak);
    }

    [Fact]
    public void Ordinary_audio_is_not_silence()
    {
        AudioStats stats = Measure(Tone(1));

        Assert.False(stats.IsSilent);
        Assert.InRange(stats.Peak, 0.49, 0.51);

        // RMS of a sine is its amplitude divided by the square root of two.
        Assert.InRange(stats.Rms, 0.34, 0.36);
    }

    [Fact]
    public void Dropout_inside_track_is_measured()
    {
        AudioStats stats = Measure(Tone(1), Silence(2), Tone(1));

        Assert.InRange(stats.LongestSilentSeconds, 1.9, 2.1);
    }

    [Fact]
    public void Silence_at_start_and_end_is_not_a_dropout()
    {
        // Nearly every track has an intro and a fade; counting them as dropouts would
        // warn about half the collection.
        AudioStats stats = Measure(Silence(3), Tone(1), Silence(3));

        // Single zeros at sine zero crossings are counted, but only a noticeable pause
        // is a dropout — fractions of a millisecond are not.
        Assert.True(
            stats.LongestSilentSeconds < 0.01,
            $"насчитано {stats.LongestSilentSeconds:0.####} с тишины внутри звучания");
    }

    [Fact]
    public void Clipping_is_a_share_of_samples()
    {
        float[] loud = new float[1000];
        Array.Fill(loud, 1f);

        AudioStats stats = Measure(Tone(0.1, 0.2), loud);

        Assert.Equal(1000, stats.ClippedSamples);
        Assert.InRange(stats.ClippedShare, 0.15, 0.2);
    }

    [Fact]
    public void Dc_offset_shows_in_mean()
    {
        float[] shifted = Tone(1);
        for (int i = 0; i < shifted.Length; i++)
        {
            shifted[i] += 0.1f;
        }

        AudioStats stats = Measure(shifted);

        Assert.InRange(stats.DcOffset, 0.09, 0.11);
    }

    [Fact]
    public void Centred_recording_has_no_offset()
    {
        AudioStats stats = Measure(Tone(1));

        Assert.InRange(Math.Abs(stats.DcOffset), 0, 0.01);
    }
}
