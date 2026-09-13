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
    public void Пустой_набор_даёт_нули()
    {
        AudioStats stats = Measure();

        Assert.Equal(0, stats.Samples);
        Assert.False(stats.IsSilent);
    }

    [Fact]
    public void Сплошная_тишина_видна_по_уровню()
    {
        AudioStats stats = Measure(Silence(3));

        Assert.True(stats.IsSilent);
        Assert.Equal(0, stats.Peak);
    }

    [Fact]
    public void Обычный_звук_тишиной_не_считается()
    {
        AudioStats stats = Measure(Tone(1));

        Assert.False(stats.IsSilent);
        Assert.InRange(stats.Peak, 0.49, 0.51);

        // Среднеквадратичное синуса — амплитуда, делённая на корень из двух.
        Assert.InRange(stats.Rms, 0.34, 0.36);
    }

    [Fact]
    public void Провал_внутри_трека_измеряется()
    {
        AudioStats stats = Measure(Tone(1), Silence(2), Tone(1));

        Assert.InRange(stats.LongestSilentSeconds, 1.9, 2.1);
    }

    [Fact]
    public void Тишина_в_начале_и_в_конце_провалом_не_считается()
    {
        // Подводка и затухание есть почти у каждого трека: если считать их
        // провалом, предупреждение получит половина коллекции.
        AudioStats stats = Measure(Silence(3), Tone(1), Silence(3));

        // Одиночные нули на переходах синуса через ноль в счёт идут, но провалом
        // считается только заметная пауза — доли миллисекунды ею не являются.
        Assert.True(
            stats.LongestSilentSeconds < 0.01,
            $"насчитано {stats.LongestSilentSeconds:0.####} с тишины внутри звучания");
    }

    [Fact]
    public void Перегрузка_считается_долей_отсчётов()
    {
        float[] loud = new float[1000];
        Array.Fill(loud, 1f);

        AudioStats stats = Measure(Tone(0.1, 0.2), loud);

        Assert.Equal(1000, stats.ClippedSamples);
        Assert.InRange(stats.ClippedShare, 0.15, 0.2);
    }

    [Fact]
    public void Смещение_нуля_видно_в_постоянной_составляющей()
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
    public void У_ровной_записи_ноль_на_месте()
    {
        AudioStats stats = Measure(Tone(1));

        Assert.InRange(Math.Abs(stats.DcOffset), 0, 0.01);
    }
}
