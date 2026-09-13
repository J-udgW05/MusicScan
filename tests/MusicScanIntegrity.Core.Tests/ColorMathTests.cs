using MusicScanIntegrity.Core.Common;
using Xunit;

namespace MusicScanIntegrity.Core.Tests;

public sealed class ColorMathTests
{
    [Theory]
    [InlineData("#000000", 0, 0, 0)]
    [InlineData("#FFFFFF", 255, 255, 255)]
    [InlineData("#0F7B3F", 15, 123, 63)]
    [InlineData("  #ff7075  ", 255, 112, 117)]
    public void Код_цвета_разбирается(string hex, byte r, byte g, byte b)
    {
        Assert.True(ColorMath.TryParse(hex, out ColorMath.Rgb color));
        Assert.Equal(new ColorMath.Rgb(r, g, b), color);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0F7B3F")]
    [InlineData("#0F7B3")]
    [InlineData("#0F7B3FF")]
    [InlineData("#ZZZZZZ")]
    public void Мусор_вместо_кода_цвета_не_принимается(string? hex)
    {
        Assert.False(ColorMath.TryParse(hex, out _));
    }

    [Fact]
    public void Цвет_записывается_решёткой_и_шестью_знаками()
    {
        Assert.Equal("#0F7B3F", new ColorMath.Rgb(15, 123, 63).ToString());
    }

    [Theory]
    [InlineData(0, 1, 1, 255, 0, 0)]
    [InlineData(120, 1, 1, 0, 255, 0)]
    [InlineData(240, 1, 1, 0, 0, 255)]
    [InlineData(0, 0, 1, 255, 255, 255)]
    [InlineData(0, 0, 0, 0, 0, 0)]
    [InlineData(0, 1, 0.5, 128, 0, 0)]
    public void Оттенок_переводится_в_составляющие(
        double hue, double saturation, double value, byte r, byte g, byte b)
    {
        ColorMath.Rgb color = ColorMath.ToRgb(new ColorMath.Hsv(hue, saturation, value));

        Assert.Equal(new ColorMath.Rgb(r, g, b), color);
    }

    [Theory]
    [InlineData("#FF0000", 0, 1, 1)]
    [InlineData("#00FF00", 120, 1, 1)]
    [InlineData("#0000FF", 240, 1, 1)]
    [InlineData("#FFFFFF", 0, 0, 1)]
    [InlineData("#808080", 0, 0, 0.502)]
    public void Составляющие_переводятся_в_оттенок(
        string hex, double hue, double saturation, double value)
    {
        Assert.True(ColorMath.TryParse(hex, out ColorMath.Rgb color));
        ColorMath.Hsv hsv = ColorMath.ToHsv(color);

        Assert.Equal(hue, hsv.Hue, 1);
        Assert.Equal(saturation, hsv.Saturation, 2);
        Assert.Equal(value, hsv.Value, 2);
    }

    [Theory]
    [InlineData("#0F7B3F")]
    [InlineData("#FF7075")]
    [InlineData("#F0B429")]
    [InlineData("#7C4DBE")]
    [InlineData("#9A9AA2")]
    [InlineData("#010203")]
    public void Перевод_туда_и_обратно_даёт_исходный_цвет(string hex)
    {
        Assert.True(ColorMath.TryParse(hex, out ColorMath.Rgb color));

        ColorMath.Rgb back = ColorMath.ToRgb(ColorMath.ToHsv(color));

        Assert.Equal(color, back);
    }

    [Fact]
    public void Подложка_метки_близка_к_готовым_токенам()
    {
        ColorMath.Rgb white = new(0xFF, 0xFF, 0xFF);
        ColorMath.Rgb dark = new(0x1C, 0x1C, 0x1E);

        Assert.True(ColorMath.TryParse("#0F7B3F", out ColorMath.Rgb light));
        ColorMath.Rgb lightTint = ColorMath.Tint(light, white, 0.10);

        // Токен темы — #E8F6ED; допускается расхождение в несколько единиц.
        Assert.InRange(lightTint.R, 0xE0, 0xF0);
        Assert.InRange(lightTint.G, 0xEE, 0xFA);
        Assert.InRange(lightTint.B, 0xE5, 0xF3);

        Assert.True(ColorMath.TryParse("#5EC27F", out ColorMath.Rgb night));
        ColorMath.Rgb darkTint = ColorMath.Tint(night, dark, 0.13);

        // Токен темы — #1C3227.
        Assert.InRange(darkTint.R, 0x18, 0x2A);
        Assert.InRange(darkTint.G, 0x2C, 0x3A);
        Assert.InRange(darkTint.B, 0x20, 0x30);
    }

    [Fact]
    public void Подложка_остаётся_поверхностью_при_нулевой_доле()
    {
        ColorMath.Rgb surface = new(0x1C, 0x1C, 0x1E);

        Assert.Equal(surface, ColorMath.Tint(new ColorMath.Rgb(255, 0, 0), surface, 0));
    }

    [Theory]
    [InlineData(0, 200, 0)]
    [InlineData(100, 200, 0.5)]
    [InlineData(200, 200, 1)]
    [InlineData(-40, 200, 0)]
    [InlineData(400, 200, 1)]
    [InlineData(50, 0, 0)]
    public void Положение_курсора_приводится_к_доле(double position, double length, double expected)
    {
        Assert.Equal(expected, ColorMath.Share(position, length), 4);
    }
}
