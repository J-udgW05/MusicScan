using MusicScanIntegrity.App.Controls;
using MusicScanIntegrity.App.Services;
using MusicScanIntegrity.Core.Resources;

namespace MusicScanIntegrity.App.Views.Dialogs;

/// <summary>About window.</summary>
public partial class AboutDialog
{
    public AboutDialog(AboutInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        InitializeComponent();
        UiScale.Apply(this);

        VersionLine.Text = Core.Common.Format.Text(Strings.About_Version, info.Version);
        BuildLine.Text = $"{info.Version} · {info.BuildDate}";
        AudioLine.Text = info.AudioEngine;

        PluginsLine.Text = info.Plugins.Count > 0
            ? Core.Common.Format.Text(Strings.About_Plugins, string.Join(", ", info.Plugins))
            : Strings.About_NoPlugins;

        Loaded += (_, _) => Logo.Source = AppMark.ForSize(this, Logo.Width * UiScale.Factor);
    }
}
