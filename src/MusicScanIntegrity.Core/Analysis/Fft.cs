namespace MusicScanIntegrity.Core.Analysis;

/// <summary>
/// Быстрое преобразование Фурье по основанию два.
/// </summary>
/// <remarks>
/// Своя реализация, а не пакет: алгоритм умещается в полсотни строк, проверяется
/// на сигналах с заранее известным ответом и не тянет в переносимую сборку
/// лишнюю зависимость ради одной задачи — посмотреть, до какой частоты в файле
/// есть звук.
/// </remarks>
public static class Fft
{
    /// <summary>Считает преобразование на месте.</summary>
    /// <param name="real">Действительная часть; длина — степень двойки.</param>
    /// <param name="imaginary">Мнимая часть той же длины.</param>
    /// <exception cref="ArgumentException">Длины не совпадают или не степень двойки.</exception>
    public static void Forward(Span<double> real, Span<double> imaginary)
    {
        int length = real.Length;

        if (imaginary.Length != length)
        {
            throw new ArgumentException("Длины действительной и мнимой частей должны совпадать.", nameof(imaginary));
        }

        if (length < 2 || (length & (length - 1)) != 0)
        {
            throw new ArgumentException("Длина должна быть степенью двойки.", nameof(real));
        }

        Reorder(real, imaginary);

        for (int size = 2; size <= length; size *= 2)
        {
            double angle = -2 * Math.PI / size;
            double stepReal = Math.Cos(angle);
            double stepImaginary = Math.Sin(angle);

            for (int start = 0; start < length; start += size)
            {
                double turnReal = 1;
                double turnImaginary = 0;

                for (int offset = 0; offset < size / 2; offset++)
                {
                    int left = start + offset;
                    int right = left + (size / 2);

                    double productReal = (real[right] * turnReal) - (imaginary[right] * turnImaginary);
                    double productImaginary = (real[right] * turnImaginary) + (imaginary[right] * turnReal);

                    real[right] = real[left] - productReal;
                    imaginary[right] = imaginary[left] - productImaginary;
                    real[left] += productReal;
                    imaginary[left] += productImaginary;

                    double nextTurnReal = (turnReal * stepReal) - (turnImaginary * stepImaginary);
                    turnImaginary = (turnReal * stepImaginary) + (turnImaginary * stepReal);
                    turnReal = nextTurnReal;
                }
            }
        }
    }

    /// <summary>Окно Ханна: сглаживает края куска, иначе они дают ложные частоты.</summary>
    /// <param name="length">Длина окна; ноль и единица допустимы.</param>
    /// <returns>Коэффициенты окна.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Длина отрицательна.</exception>
    public static double[] HannWindow(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        // Окно из одного отсчёта делить не на что: формула требует длину больше
        // единицы. Единица — принятый ответ: одиночный отсчёт остаётся собой.
        if (length <= 1)
        {
            return length == 0 ? [] : [1];
        }

        double[] window = new double[length];

        for (int i = 0; i < length; i++)
        {
            window[i] = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (length - 1)));
        }

        return window;
    }

    /// <summary>Переставляет отсчёты в порядке обратных двоичных индексов.</summary>
    private static void Reorder(Span<double> real, Span<double> imaginary)
    {
        int length = real.Length;
        int target = 0;

        for (int i = 0; i < length - 1; i++)
        {
            if (i < target)
            {
                (real[i], real[target]) = (real[target], real[i]);
                (imaginary[i], imaginary[target]) = (imaginary[target], imaginary[i]);
            }

            int mask = length >> 1;
            while (mask > 0 && (target & mask) != 0)
            {
                target &= ~mask;
                mask >>= 1;
            }

            target |= mask;
        }
    }
}
