using MusicScanIntegrity.Core.Analysis;
using Xunit;

namespace MusicScanIntegrity.Core.Tests;

public sealed class FftTests
{
    [Fact]
    public void Sine_peaks_in_its_own_bin()
    {
        const int size = 1024;
        const int bin = 64;

        double[] real = new double[size];
        double[] imaginary = new double[size];

        for (int i = 0; i < size; i++)
        {
            real[i] = Math.Sin(2 * Math.PI * bin * i / size);
        }

        Fft.Forward(real, imaginary);

        int loudest = 0;
        double best = 0;

        for (int i = 1; i < size / 2; i++)
        {
            double power = (real[i] * real[i]) + (imaginary[i] * imaginary[i]);
            if (power > best)
            {
                best = power;
                loudest = i;
            }
        }

        Assert.Equal(bin, loudest);
    }

    [Fact]
    public void Constant_signal_lands_in_bin_zero()
    {
        double[] real = new double[256];
        double[] imaginary = new double[256];
        Array.Fill(real, 0.5);

        Fft.Forward(real, imaginary);

        Assert.InRange(real[0], 127, 129);
        Assert.InRange(Math.Abs(real[1]), 0, 1e-9);
    }

    /// <remarks>
    /// The window formula divides by length minus one, which is zero for a single
    /// sample. The resulting NaN throws nothing and silently spreads through the
    /// spectrum: every comparison with it is false and the upper edge quietly
    /// becomes zero. Visible only as a wrong answer.
    /// </remarks>
    [Fact]
    public void Single_sample_window_is_a_number()
    {
        double[] window = Fft.HannWindow(1);

        double value = Assert.Single(window);
        Assert.False(double.IsNaN(value));
        Assert.Equal(1, value);
    }

    [Fact]
    public void Zero_length_window_is_empty()
    {
        Assert.Empty(Fft.HannWindow(0));
    }

    [Fact]
    public void Negative_length_window_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Fft.HannWindow(-1));
    }

    /// <summary>Regular window: edges pulled to zero, middle to one.</summary>
    [Fact]
    public void Window_pulls_edges_to_zero()
    {
        double[] window = Fft.HannWindow(64);

        Assert.Equal(0, window[0], 12);
        Assert.Equal(0, window[^1], 12);
        Assert.Equal(1, window[32], 2);
        Assert.All(window, v => Assert.InRange(v, 0, 1));
    }

    [Fact]
    public void Non_power_of_two_length_is_rejected()
    {
        double[] real = new double[100];
        double[] imaginary = new double[100];

        Assert.Throws<ArgumentException>(() => Fft.Forward(real, imaginary));
    }
}

public sealed class SpectrumAnalyzerTests
{
    private const int Rate = 44100;

    private static SpectrumProfile Analyze(Func<int, double> signal, double seconds = 2)
    {
        SpectrumAnalyzer analyzer = new(Rate, 1);
        int total = (int)(Rate * seconds);
        float[] buffer = new float[4096];

        for (int offset = 0; offset < total; offset += buffer.Length)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = (float)signal(offset + i);
            }

            analyzer.Add(buffer);
        }

        return analyzer.Build();
    }

    [Fact]
    public void Band_of_low_passed_signal_is_detected()
    {
        // Sum of tones up to 10 kHz; nothing above that in the signal.
        SpectrumProfile profile = Analyze(i =>
        {
            double t = i / (double)Rate;
            return (0.3 * Math.Sin(2 * Math.PI * 400 * t))
                + (0.3 * Math.Sin(2 * Math.PI * 3000 * t))
                + (0.3 * Math.Sin(2 * Math.PI * 9800 * t));
        });

        Assert.True(profile.IsReliable);
        Assert.InRange(profile.CutoffHz, 9000, 11500);
    }

    [Fact]
    public void Wideband_noise_reaches_format_limit()
    {
        Random random = new(20260905);

        SpectrumProfile profile = Analyze(_ => (random.NextDouble() * 2) - 1);

        Assert.True(profile.IsReliable);
        Assert.True(
            profile.BandShare > 0.9,
            $"занято {profile.BandShare:P0} полосы при границе {profile.CutoffHz:0} Гц");
    }

    [Fact]
    public void Silence_gives_no_false_edge()
    {
        SpectrumProfile profile = Analyze(_ => 0);

        Assert.Equal(0, profile.CutoffHz);
    }

    [Fact]
    public void Short_block_is_not_enough_to_conclude()
    {
        SpectrumProfile profile = Analyze(i => Math.Sin(2 * Math.PI * 1000 * i / Rate), seconds: 0.2);

        Assert.False(profile.IsReliable);
    }
}
