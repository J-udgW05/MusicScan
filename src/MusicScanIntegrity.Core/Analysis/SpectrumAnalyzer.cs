namespace MusicScanIntegrity.Core.Analysis;

/// <summary>
/// До какой частоты в файле есть звук.
/// </summary>
/// <param name="CutoffHz">Верхняя граница энергии в герцах.</param>
/// <param name="NyquistHz">Наибольшая частота, которую вообще может хранить файл.</param>
/// <param name="Blocks">Сколько кусков усреднено.</param>
public sealed record SpectrumProfile(double CutoffHz, double NyquistHz, int Blocks)
{
    /// <summary>Ничего не измеряли.</summary>
    public static readonly SpectrumProfile Empty = new(0, 0, 0);

    /// <summary>Измерения достаточно, чтобы о чём-то говорить.</summary>
    /// <remarks>
    /// Меньше восьми кусков — это доли секунды звука: на таком отрезке верхняя
    /// граница скачет от одного тихого места, и выводы делать нельзя.
    /// </remarks>
    public bool IsReliable => Blocks >= 8 && NyquistHz > 0;

    /// <summary>Доля занятой полосы: 1 — звук доходит до предела формата.</summary>
    public double BandShare => NyquistHz <= 0 ? 0 : CutoffHz / NyquistHz;
}

/// <summary>
/// Считает усреднённый спектр по кускам звука и находит его верхнюю границу.
/// </summary>
/// <remarks>
/// <para>
/// Смысл в одном вопросе: докуда в файле есть звук. Сжатие с потерями срезает
/// верх — 320 кбит/с примерно на 20 кГц, 128 кбит/с около 16 кГц. Поэтому
/// «лослесс» с границей 16 кГц почти наверняка собран из MP3. Слово «почти»
/// здесь принципиально: у старых и намеренно узкополосных записей верха нет и
/// без всякого перекодирования, поэтому вывод — подозрение, а не приговор.
/// </para>
/// <para>
/// Граница считается по каждому куску отдельно, а в итог идёт наибольшая.
/// Усреднение здесь было бы ошибкой: в симфонии тихих мест больше, чем громких,
/// и среднее по ним показывало бы отсутствие верхов там, где они есть в
/// кульминациях. Вопрос ведь не «много ли верхов», а «бывают ли они вообще» —
/// у файла, собранного из сжатого, их не бывает нигде.
/// </para>
/// </remarks>
public sealed class SpectrumAnalyzer(int sampleRate, int channels)
{
    /// <summary>Длина куска: 4096 отсчётов дают шаг около 11 Гц при 44,1 кГц.</summary>
    private const int BlockSize = 4096;

    /// <summary>Сколько кусков усреднять — больше не нужно, разброс уже сглажен.</summary>
    private const int MaxBlocks = 64;

    /// <summary>
    /// Насколько тише самой громкой полосы может быть край спектра, чтобы его
    /// ещё считали звуком: −65 дБ.
    /// </summary>
    private const double EdgeThreshold = 3.16e-7;

    /// <summary>Сколько полос подряд должны быть выше порога — защита от одиночных всплесков.</summary>
    private const int EdgeRun = 3;

    /// <summary>Ниже этой средней громкости кусок слишком тих, чтобы судить по нему о полосе.</summary>
    private const double QuietBlockRms = 0.005;

    private readonly double[] _window = Fft.HannWindow(BlockSize);
    private readonly double[] _power = new double[(BlockSize / 2) + 1];
    private readonly double[] _block = new double[BlockSize];
    private readonly double[] _real = new double[BlockSize];
    private readonly double[] _imaginary = new double[BlockSize];

    private int _filled;
    private int _blocks;
    private double _highestCutoff;

    /// <summary>Добавляет очередную порцию отсчётов (чередующиеся каналы).</summary>
    /// <param name="samples">Отсчёты в диапазоне −1…1.</param>
    public void Add(ReadOnlySpan<float> samples)
    {
        if (_blocks >= MaxBlocks || channels <= 0)
        {
            return;
        }

        for (int i = 0; i + channels <= samples.Length; i += channels)
        {
            // Каналы складываются в один: спектр интересует по содержанию,
            // а не по стереокартине, и одного канала хватает.
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

    /// <summary>Собирает итог: до какой частоты в звуке есть энергия.</summary>
    /// <returns>Профиль спектра.</returns>
    public SpectrumProfile Build() => _blocks == 0 || sampleRate <= 0
        ? SpectrumProfile.Empty
        : new SpectrumProfile(_highestCutoff, sampleRate / 2.0, _blocks);

    /// <summary>Считает спектр одного куска и запоминает его верхнюю границу.</summary>
    private void Accumulate()
    {
        double squareSum = 0;

        for (int i = 0; i < BlockSize; i++)
        {
            squareSum += _block[i] * _block[i];
            _real[i] = _block[i] * _window[i];
            _imaginary[i] = 0;
        }

        // Тихий кусок ничего не говорит о полосе: верхов в нём нет просто
        // потому, что там нечему звучать.
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

                // Граница — верхняя из подряд идущих полос выше порога.
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
