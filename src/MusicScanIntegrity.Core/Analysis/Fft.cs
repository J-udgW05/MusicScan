namespace MusicScanIntegrity.Core.Analysis;

/// <summary>
/// Radix-2 fast Fourier transform.
/// </summary>
/// <remarks>
/// Hand-written rather than a package: the algorithm fits in fifty lines, is
/// testable against signals with known answers, and avoids pulling a
/// dependency into a portable build for one job — finding how high the audio
/// reaches.
/// </remarks>
public static class Fft
{
    /// <summary>Transforms in place.</summary>
    /// <param name="real">Real part; the length must be a power of two.</param>
    /// <param name="imaginary">Imaginary part of the same length.</param>
    /// <exception cref="ArgumentException">Lengths differ or are not a power of two.</exception>
    public static void Forward(Span<double> real, Span<double> imaginary)
    {
        int length = real.Length;

        if (imaginary.Length != length)
        {
            throw new ArgumentException("Real and imaginary parts must have the same length.", nameof(imaginary));
        }

        if (length < 2 || (length & (length - 1)) != 0)
        {
            throw new ArgumentException("Length must be a power of two.", nameof(real));
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

    /// <summary>Hann window; smooths block edges that would otherwise create false frequencies.</summary>
    /// <param name="length">Window length; zero and one are allowed.</param>
    /// <returns>Window coefficients.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The length is negative.</exception>
    public static double[] HannWindow(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        // A single-sample window has nothing to divide by: the formula needs a
        // length above one. Returning 1 leaves the lone sample unchanged.
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

    /// <summary>Reorders samples by bit-reversed index.</summary>
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
