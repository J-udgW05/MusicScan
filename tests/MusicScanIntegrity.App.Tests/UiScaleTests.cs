using System.Windows;
using System.Windows.Controls;
using MusicScanIntegrity.App.Services;
using Xunit;

namespace MusicScanIntegrity.App.Tests;

/// <summary>The interface scale reaches popups and only the sizes a window sets itself.</summary>
public sealed class UiScaleTests
{
    [Fact]
    public void Tooltips_and_context_menus_are_scaled()
    {
        Sta.Run(() =>
        {
            UiScale.RegisterPopups();
            UiScale.RegisterPopups();

            Assert.Same(UiScale.Transform, new ToolTip().LayoutTransform);
            Assert.Same(UiScale.Transform, new ContextMenu().LayoutTransform);
        });
    }

    [Fact]
    public void Window_sizes_it_sets_are_scaled_and_defaults_are_left_alone()
    {
        Sta.Run(() =>
        {
            Window window = new() { Width = 400, Content = new Grid() };

            UiScale.Apply(window);

            Assert.Equal(Math.Min(400 * UiScale.Factor, SystemParameters.WorkArea.Width), window.Width, 3);
            Assert.Equal(0, window.MinWidth);
            Assert.True(double.IsPositiveInfinity(window.MaxHeight));
            Assert.Same(UiScale.Transform, ((Grid)window.Content).LayoutTransform);
        });
    }
}
