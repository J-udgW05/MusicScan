using System.Windows.Input;
using MusicScanIntegrity.App.Services;

namespace MusicScanIntegrity.App.Views.Dialogs;

/// <summary>Help window: status meanings, how the check works, keyboard shortcuts.</summary>
public partial class HelpDialog
{
    public HelpDialog()
    {
        InitializeComponent();
        UiScale.Apply(this);
    }

    // No buttons here, so Esc is handled by hand: a window that ignores Esc
    // feels broken.
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }
}
