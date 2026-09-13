using MusicScanIntegrity.Core.Analysis;
using Xunit;

namespace MusicScanIntegrity.Core.Tests;

public sealed class FftTests
{
    [Fact]
    public void Синус_даёт_пик_в_своей_полосе()
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
    public void Постоянный_сигнал_собирается_в_нулевой_полосе()
    {
        double[] real = new double[256];
        double[] imaginary = new double[256];
        Array.Fill(real, 0.5);

        Fft.Forward(real, imaginary);

        Assert.InRange(real[0], 127, 129);
        Assert.InRange(Math.Abs(real[1]), 0, 1e-9);
    }

    /// <summary>
    /// Окно из одного отсчёта не должно давать «не число».
    /// </summary>
    /// <remarks>
    /// В формуле окна стоит деление на длину минус один, и при длине единица
    /// оно даёт ноль. Полученное NaN не выбрасывает исключения, а тихо
    /// расползается по всему спектру: любое сравнение с ним ложно, и верхняя
    /// граница молча оказывается нулевой. Такую ошибку не видно ни по журналу,
    /// ни по вердикту — только по неправильному ответу.
    /// </remarks>
    [Fact]
    public void Окно_из_одного_отсчёта_остаётся_числом()
    {
        double[] window = Fft.HannWindow(1);

        double value = Assert.Single(window);
        Assert.False(double.IsNaN(value));
        Assert.Equal(1, value);
    }

    [Fact]
    public void Окно_нулевой_длины_пустое()
    {
        Assert.Empty(Fft.HannWindow(0));
    }

    [Fact]
    public void Окно_отрицательной_длины_не_принимается()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Fft.HannWindow(-1));
    }

    /// <summary>Обычное окно: края прижаты к нулю, середина — к единице.</summary>
    [Fact]
    public void Окно_прижимает_края_к_нулю()
    {
        double[] window = Fft.HannWindow(64);

        Assert.Equal(0, window[0], 12);
        Assert.Equal(0, window[^1], 12);
        Assert.Equal(1, window[32], 2);
        Assert.All(window, v => Assert.InRange(v, 0, 1));
    }

    [Fact]
    public void Длина_не_степень_двойки_не_принимается()
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
    public void Полоса_обрезанного_сверху_сигнала_видна()
    {
        // Сумма тонов до 10 кГц: выше в сигнале ничего нет.
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
    public void Широкополосный_шум_доходит_до_предела_формата()
    {
        Random random = new(20260905);

        SpectrumProfile profile = Analyze(_ => (random.NextDouble() * 2) - 1);

        Assert.True(profile.IsReliable);
        Assert.True(
            profile.BandShare > 0.9,
            $"занято {profile.BandShare:P0} полосы при границе {profile.CutoffHz:0} Гц");
    }

    [Fact]
    public void Тишина_не_даёт_ложной_границы()
    {
        SpectrumProfile profile = Analyze(_ => 0);

        Assert.Equal(0, profile.CutoffHz);
    }

    [Fact]
    public void Короткого_куска_недостаточно_для_вывода()
    {
        SpectrumProfile profile = Analyze(i => Math.Sin(2 * Math.PI * 1000 * i / Rate), seconds: 0.2);

        Assert.False(profile.IsReliable);
    }
}
