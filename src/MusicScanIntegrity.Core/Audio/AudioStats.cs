namespace MusicScanIntegrity.Core.Audio;

/// <summary>
/// Что видно в самих отсчётах: громкость, перегрузка, постоянная составляющая,
/// провалы в тишину.
/// </summary>
/// <param name="Samples">Сколько отсчётов просмотрено.</param>
/// <param name="Peak">Наибольшее значение по модулю; 1 — предел шкалы.</param>
/// <param name="Rms">Средняя громкость.</param>
/// <param name="ClippedSamples">Сколько отсчётов упёрлось в предел шкалы.</param>
/// <param name="DcOffset">Среднее значение: у нормальной записи оно около нуля.</param>
/// <param name="LongestSilentRun">Самая длинная тишина внутри звучащего куска, в отсчётах.</param>
/// <param name="SampleRate">Сколько отсчётов приходится на секунду с учётом всех каналов.</param>
public sealed record AudioStats(
    long Samples,
    double Peak,
    double Rms,
    long ClippedSamples,
    double DcOffset,
    long LongestSilentRun,
    int SampleRate)
{
    /// <summary>Ничего не измеряли.</summary>
    public static readonly AudioStats Empty = new(0, 0, 0, 0, 0, 0, 0);

    /// <summary>Доля отсчётов на пределе шкалы.</summary>
    public double ClippedShare => Samples == 0 ? 0 : (double)ClippedSamples / Samples;

    /// <summary>
    /// Самая длинная тишина внутри звучащего куска, в секундах.
    /// </summary>
    /// <remarks>
    /// Тишина в начале и в конце не считается: почти у каждого трека есть
    /// подводка и затухание, и объявлять их провалом значило бы ругаться на
    /// половину коллекции. Провал — это когда звук был, пропал и снова появился.
    /// </remarks>
    public double LongestSilentSeconds => SampleRate <= 0 ? 0 : (double)LongestSilentRun / SampleRate;

    /// <summary>Прочитанное — сплошная тишина.</summary>
    /// <remarks>
    /// Порог −60 дБ, а не ноль: у настоящих записей в тихих местах остаётся
    /// шум оцифровки, и требовать точных нулей значило бы не находить ничего.
    /// </remarks>
    public bool IsSilent => Samples > 0 && Peak < 0.001;
}

/// <summary>
/// Накопитель характеристик звука: считает всё за один проход по отсчётам.
/// </summary>
/// <remarks>
/// Отсчёты и так проходят через программу при декодировании, поэтому эти числа
/// достаются бесплатно — без второго чтения диска. Вынесен в отдельный тип,
/// чтобы проверяться тестами без BASS и без звуковых файлов.
/// </remarks>
public sealed class AudioStatsAccumulator(int sampleRate)
{
    /// <summary>Уровень, ниже которого отсчёт считается тишиной.</summary>
    /// <remarks>
    /// Не ноль: в записи, прошедшей через аналоговый тракт, абсолютных нулей
    /// почти не бывает, а провал звука слышен уже на −80 дБ.
    /// </remarks>
    private const double SilenceLevel = 0.0001;

    /// <summary>Уровень, начиная с которого отсчёт считается упёршимся в предел.</summary>
    private const double ClipLevel = 0.9995;

    private long _samples;
    private double _peak;
    private double _squareSum;
    private double _sum;
    private long _clipped;
    private long _silentRun;
    private long _longestSilentRun;
    private bool _sawSound;

    /// <summary>Добавляет очередную порцию отсчётов.</summary>
    /// <param name="samples">Отсчёты в диапазоне −1…1.</param>
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
                // Тишина учитывается только когда она закончилась и до неё был
                // звук: подводка в начале и затухание в конце провалом не считаются.
                if (_sawSound && _silentRun > _longestSilentRun)
                {
                    _longestSilentRun = _silentRun;
                }

                _silentRun = 0;
                _sawSound = true;
            }
        }
    }

    /// <summary>Собирает итог.</summary>
    /// <returns>Характеристики прочитанного звука.</returns>
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
