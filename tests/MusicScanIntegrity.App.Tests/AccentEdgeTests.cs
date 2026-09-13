using System.IO;
using System.Windows;
using System.Windows.Media;
using Xunit;

namespace MusicScanIntegrity.App.Tests;

/// <summary>
/// Кромка акцентной заливки должна лежать между заливкой и фоном.
/// </summary>
/// <remarks>
/// <para>
/// Ступеньки на скруглениях видны не от нехватки сглаживания — оно работает.
/// Дело в величине перепада: на шестипиксельном скруглении дуга занимает
/// считанные точки, и прыжок с фона сразу на яркую заливку глаз читает как
/// лесенку. Промежуточный тон делит этот прыжок надвое.
/// </para>
/// <para>
/// Направление смешивания важнее величины, и оно разное у тем. В тёмной кромка
/// темнее заливки, в светлой — светлее; перепутать их значит сделать хуже, чем
/// было: измерено, затемнение кромки в светлой теме поднимает наибольший
/// перепад со 140 до 171. Проверка ниже ловит именно это.
/// </para>
/// </remarks>
public sealed class AccentEdgeTests
{
    [Theory]
    [InlineData("Tokens.Dark.xaml")]
    [InlineData("Tokens.Light.xaml")]
    public void Кромка_лежит_между_заливкой_и_фоном(string theme)
    {
        Sta.Run(() =>
        {
            ResourceDictionary tokens = Load(theme);

            double background = Luminance(Color(tokens, "Color.Bg"));
            double accent = Luminance(Color(tokens, "Color.Acc"));
            double edge = Luminance(Color(tokens, "Color.AccBorder"));

            double low = Math.Min(background, accent);
            double high = Math.Max(background, accent);

            Assert.True(
                edge > low && edge < high,
                $"{theme}: яркость кромки {edge:N0} должна лежать между фоном {background:N0} и заливкой {accent:N0}");
        });
    }

    /// <summary>
    /// Кромка отличается от заливки настолько, чтобы это имело смысл.
    /// </summary>
    /// <remarks>
    /// Совпадение с заливкой — это и есть то состояние, из-за которого край
    /// прыгал одним шагом. Слишком большая разница тоже плоха: кромка начинает
    /// читаться отдельным кольцом вокруг кнопки, а не её краем.
    /// </remarks>
    [Theory]
    [InlineData("Tokens.Dark.xaml")]
    [InlineData("Tokens.Light.xaml")]
    public void Кромка_заметно_отличается_от_заливки_но_не_чрезмерно(string theme)
    {
        Sta.Run(() =>
        {
            ResourceDictionary tokens = Load(theme);

            double accent = Luminance(Color(tokens, "Color.Acc"));
            double background = Luminance(Color(tokens, "Color.Bg"));
            double edge = Luminance(Color(tokens, "Color.AccBorder"));

            double span = Math.Abs(accent - background);
            double shift = Math.Abs(accent - edge) / span;

            Assert.InRange(shift, 0.12, 0.45);
        });
    }

    [Theory]
    [InlineData("Tokens.Dark.xaml")]
    [InlineData("Tokens.Light.xaml")]
    public void Кисть_кромки_объявлена(string theme)
    {
        Sta.Run(() =>
        {
            ResourceDictionary tokens = Load(theme);

            SolidColorBrush brush = Assert.IsType<SolidColorBrush>(tokens["Brush.AccBorder"]);
            Assert.Equal(Color(tokens, "Color.AccBorder"), brush.Color);
        });
    }

    private static ResourceDictionary Load(string theme)
    {
        // Читается файл из исходников, скопированный рядом с тестами: адреса
        // pack:// разбираются только при живом Application, а его здесь нет.
        string path = Path.Combine(AppContext.BaseDirectory, "Tokens", theme);
        Assert.True(File.Exists(path), $"словарь токенов не скопирован: {path}");

        using FileStream stream = File.OpenRead(path);
        return (ResourceDictionary)System.Windows.Markup.XamlReader.Load(stream);
    }

    private static Color Color(ResourceDictionary tokens, string key) => (Color)tokens[key];

    /// <summary>Воспринимаемая яркость цвета.</summary>
    private static double Luminance(Color color) =>
        (0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B);
}
