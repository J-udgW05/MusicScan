using System.Globalization;

namespace MusicScanIntegrity.Core.Common;

/// <summary>
/// Colour maths for the status colour picker: hex parsing, RGB/HSV conversion
/// and tinting a theme surface.
/// </summary>
/// <remarks>
/// Lives in the core rather than next to the picker window so it can be unit
/// tested; the window only maps cursor coordinates to shares.
/// </remarks>
public static class ColorMath
{
    /// <summary>A colour as three 0…255 components.</summary>
    public readonly record struct Rgb(byte R, byte G, byte B)
    {
        /// <summary>Formats the colour as "#RRGGBB".</summary>
        public override string ToString() =>
            string.Create(CultureInfo.InvariantCulture, $"#{R:X2}{G:X2}{B:X2}");
    }

    /// <summary>A colour as hue in degrees (0…360) with saturation and value in 0…1.</summary>
    public readonly record struct Hsv(double Hue, double Saturation, double Value);

    /// <summary>Parses a "#RRGGBB" string.</summary>
    /// <returns><see langword="true" /> if the string was understood.</returns>
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

    /// <summary>Converts HSV to RGB.</summary>
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

    /// <summary>Converts RGB to HSV.</summary>
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

    /// <summary>Blends a status colour into a theme surface.</summary>
    /// <param name="share">Share of the status colour, 0…1.</param>
    /// <remarks>
    /// Shares are derived from the design tokens: the light "ok" badge
    /// (#E8F6ED) is roughly a tenth of the colour over white, the dark one
    /// (#1C3227) roughly an eighth over the window background.
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

    /// <summary>Cursor position along an edge, clamped to 0…1; 0 for zero length.</summary>
    public static double Share(double position, double length) =>
        length <= 0 ? 0 : Math.Clamp(position / length, 0, 1);

    private static byte Round(double value) =>
        (byte)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, 255);
}
