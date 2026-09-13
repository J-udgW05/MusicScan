using MusicScanIntegrity.App.Controls;
using MusicScanIntegrity.App.Services;

namespace MusicScanIntegrity.App.Views.Dialogs;

/// <summary>Окно «О программе».</summary>
public partial class AboutDialog
{
    /// <summary>Создаёт окно.</summary>
    public AboutDialog(AboutInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        InitializeComponent();

        VersionLine.Text = $"Версия {info.Version}";
        BuildLine.Text = $"{info.Version} · {info.BuildDate}";
        AudioLine.Text = info.AudioEngine;

        PluginsLine.Text = info.Plugins.Count > 0
            ? "Плагины форматов: " + string.Join(", ", info.Plugins)
            : "Плагины форматов не загружены — часть форматов проверить не получится.";

        Loaded += (_, _) => Logo.Source = AppMark.ForSize(this, Logo.Width);
    }
}
