using System.IO;
using System.Windows;
using System.Windows.Media;
using Xunit;

namespace MusicScanIntegrity.App.Tests;

/// <summary>The accent edge tone must lie between the fill and the background.</summary>
/// <remarks>
/// Stepped corners are not a lack of anti-aliasing but the size of the jump:
/// on a 6 px corner the arc covers a handful of pixels, and going straight
/// from background to bright fill reads as a staircase. An intermediate tone
/// halves the jump.
/// <para>
/// The mixing direction matters more than the amount and differs per theme:
/// darker than the fill in dark, lighter in light. Getting it backwards is worse
/// than nothing — darkening the edge in the light theme raised the largest
/// jump from 140 to 171.
/// </para>
/// </remarks>
public sealed class AccentEdgeTests
{
    [Theory]
    [InlineData("Tokens.Dark.xaml")]
    [InlineData("Tokens.Light.xaml")]
    public void Edge_lies_between_fill_and_background(string theme)
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

    /// <remarks>
    /// Equal to the fill is exactly the single-step edge being fixed; too different
    /// and the edge reads as a separate ring around the button.
    /// </remarks>
    [Theory]
    [InlineData("Tokens.Dark.xaml")]
    [InlineData("Tokens.Light.xaml")]
    public void Edge_differs_from_fill_noticeably_but_not_excessively(string theme)
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
    public void Edge_brush_is_declared(string theme)
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
        // Load the source file copied next to the tests: pack:// URIs resolve only
        // with a live Application, and there is none here.
        string path = Path.Combine(AppContext.BaseDirectory, "Tokens", theme);
        Assert.True(File.Exists(path), $"словарь токенов не скопирован: {path}");

        using FileStream stream = File.OpenRead(path);
        return (ResourceDictionary)System.Windows.Markup.XamlReader.Load(stream);
    }

    private static Color Color(ResourceDictionary tokens, string key) => (Color)tokens[key];

    /// <summary>Perceived luminance of a colour.</summary>
    private static double Luminance(Color color) =>
        (0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B);
}
