using System.Windows.Input;

namespace MusicScanIntegrity.App.Views.Dialogs;

/// <summary>Окно справки: значения статусов, принцип работы, горячие клавиши.</summary>
public partial class HelpDialog
{
    /// <summary>Создаёт окно.</summary>
    public HelpDialog() => InitializeComponent();

    // Кнопок у справки нет, поэтому Esc обрабатывается вручную: окно, которое
    // не закрывается по Esc, ощущается сломанным.
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }
}
