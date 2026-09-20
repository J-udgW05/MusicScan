using System.Diagnostics;
using System.Windows;
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

        RepositoryLink.ToolTip = AppLinks.Repository;
        WebsiteLink.ToolTip = AppLinks.Website;
    }

    private void OnOpenRepository(object sender, RoutedEventArgs e) => Open(AppLinks.Repository);

    private void OnOpenWebsite(object sender, RoutedEventArgs e) => Open(AppLinks.Website);

    /// <summary>Opens an address in the default browser, staying quiet on failure.</summary>
    private static void Open(string url)
    {
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // No browser, or opening is blocked by policy; nothing to report here.
        }
    }
}
