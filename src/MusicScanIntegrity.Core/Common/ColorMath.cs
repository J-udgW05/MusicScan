using System.Globalization;

namespace MusicScanIntegrity.Core.Common;

/// <summary>
/// Расчёты для выбора цвета: разбор кода, перевод между RGB и HSV, подмешивание
/// цвета к поверхности темы.
/// </summary>
/// <remarks>
/// Расчёты живут в ядре, а не рядом с окном выбора цвета, по одной причине:
/// окно нельзя провести мышью в автоматической проверке, а эти функции —
/// можно. В окне остаётся только перенос координат курсора в доли.
/// </remarks>
public static class ColorMath
{
    /// <summary>Цвет как три составляющие 0…255.</summary>
    /// <param name="R">Красная составляющая.</param>
    /// <param name="G">Зелёная составляющая.</param>
    /// <param name="B">Синяя составляющая.</param>
    public readonly record struct Rgb(byte R, byte G, byte B)
    {
        /// <summary>Записывает цвет как «#RRGGBB».</summary>
        public override string ToString() =>
            string.Create(CultureInfo.InvariantCulture, $"#{R:X2}{G:X2}{B:X2}");
    }

    /// <summary>Цвет как оттенок 0…360, насыщенность и яркость 0…1.</summary>
    /// <param name="Hue">Оттенок в градусах.</param>
    /// <param name="Saturation">Насыщенность.</param>
    /// <param name="Value">Яркость.</param>
    public readonly record struct Hsv(double Hue, double Saturation, double Value);

    /// <summary>Разбирает запись вида «#RRGGBB».</summary>
    /// <param name="hex">Код цвета.</param>
    /// <param name="color">Разобранный цвет.</param>
    /// <returns><see langword="true" />, если запись понята.</returns>
    public static bool TryParse(string? hex, out Rgb color)
    {
        color = default;

        if (hex is null)
        {
            return false;
        }

        string text = hex.Trim();
        if (text.Length != 7 || text[0] != '#')
        {
            return false;
        }

        if (!byte.TryParse(text.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r)
            || !byte.TryParse(text.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g)
            || !byte.TryParse(text.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
        {
            return false;
        }

        color = new Rgb(r, g, b);
        return true;
    }

    /// <summary>Переводит оттенок, насыщенность и яркость в составляющие цвета.</summary>
    /// <param name="hsv">Исходные значения.</param>
    /// <returns>Цвет в RGB.</returns>
    public static Rgb ToRgb(Hsv hsv)
    {
        double hue = hsv.Hue % 360;
        if (hue < 0)
        {
            hue += 360;
        }

        double saturation = Math.Clamp(hsv.Saturation, 0, 1);
        double value = Math.Clamp(hsv.Value, 0, 1);

        double chroma = value * saturation;
        double second = chroma * (1 - Math.Abs(((hue / 60) % 2) - 1));
        double shift = value - chroma;

        (double R, double G, double B) parts = hue switch
        {
            < 60 => (chroma, second, 0d),
            < 120 => (second, chroma, 0d),
            < 180 => (0d, chroma, second),
            < 240 => (0d, second, chroma),
            < 300 => (second, 0d, chroma),
            _ => (chroma, 0d, second),
        };

        return new Rgb(
            Round((parts.R + shift) * 255),
            Round((parts.G + shift) * 255),
            Round((parts.B + shift) * 255));
    }

    /// <summary>Переводит составляющие цвета в оттенок, насыщенность и яркость.</summary>
    /// <param name="color">Исходный цвет.</param>
    /// <returns>Значения HSV.</returns>
    public static Hsv ToHsv(Rgb color)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        double hue = 0;
        if (delta > 0)
        {
            if (max == r)
            {
                hue = 60 * (((g - b) / delta) % 6);
            }
            else if (max == g)
            {
                hue = 60 * (((b - r) / delta) + 2);
            }
            else
            {
                hue = 60 * (((r - g) / delta) + 4);
            }
        }

        if (hue < 0)
        {
            hue += 360;
        }

        return new Hsv(hue, max <= 0 ? 0 : delta / max, max);
    }

    /// <summary>
    /// Подложка метки: цвет, подмешанный к поверхности темы.
    /// </summary>
    /// <param name="color">Цвет статуса.</param>
    /// <param name="surface">Цвет поверхности, на которой лежит метка.</param>
    /// <param name="share">Доля цвета, 0…1.</param>
    /// <returns>Цвет подложки.</returns>
    /// <remarks>
    /// Доли подобраны по готовым токенам: светлая подложка «в порядке»
    /// (#E8F6ED) — это примерно десятая часть цвета на белом, тёмная
    /// (#1C3227) — примерно восьмая на фоне окна.
    /// </remarks>
    public static Rgb Tint(Rgb color, Rgb surface, double share)
    {
        double part = Math.Clamp(share, 0, 1);

        return new Rgb(
            Mix(surface.R, color.R, part),
            Mix(surface.G, color.G, part),
            Mix(surface.B, color.B, part));

        static byte Mix(byte from, byte to, double share) =>
            Round(from + ((to - from) * share));
    }

    /// <summary>Доля вдоль стороны: положение курсора, приведённое к 0…1.</summary>
    /// <param name="position">Координата курсора внутри области.</param>
    /// <param name="length">Длина стороны области.</param>
    /// <returns>Доля от 0 до 1; для нулевой длины — 0.</returns>
    public static double Share(double position, double length) =>
        length <= 0 ? 0 : Math.Clamp(position / length, 0, 1);

    private static byte Round(double value) =>
        (byte)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, 255);
}
