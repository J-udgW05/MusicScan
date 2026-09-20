using System.Windows;
using MusicScanIntegrity.App.Services;

namespace MusicScanIntegrity.App.Views.Dialogs;

/// <summary>First-run welcome: three steps and a promise not to modify anything on disk.</summary>
public partial class FirstRunDialog
{
    public FirstRunDialog()
    {
        InitializeComponent();
        UiScale.Apply(this);
    }

    /// <summary>The user asked not to show the welcome again.</summary>
    public bool DoNotShowAgain => DoNotShow.IsChecked == true;

    private void OnPickFolder(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnLater(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
