using System.Windows;

namespace MusicScanIntegrity.App.Views.Dialogs;

/// <summary>Приветствие при первом запуске: три шага и обещание ничего не менять на диске.</summary>
public partial class FirstRunDialog
{
    /// <summary>Создаёт окно.</summary>
    public FirstRunDialog() => InitializeComponent();

    /// <summary>Пользователь попросил больше не показывать приветствие.</summary>
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
