using System.Windows;

namespace MusicScanIntegrity.App.Views.Dialogs;

/// <summary>
/// Универсальный диалог: подтверждение или сообщение.
/// Человеческая формулировка сверху, техническая причина — отдельным блоком
/// (UI_SPEC.md, раздел 9).
/// </summary>
public partial class MessageDialog
{
    /// <summary>Создаёт диалог.</summary>
    /// <param name="title">Заголовок сообщения — крупная строка внутри окна.</param>
    /// <param name="message">Человеческая формулировка.</param>
    /// <param name="confirmText">Подпись основной кнопки.</param>
    /// <param name="cancelText">Подпись второстепенной кнопки; <see langword="null"/> — кнопки не будет.</param>
    /// <param name="destructive">Основное действие деструктивное — красная кнопка.</param>
    /// <param name="technicalDetail">Техническая причина.</param>
    /// <param name="isError">Показать значок ошибки вместо предупреждения.</param>
    /// <param name="windowTitle">Короткая подпись в заголовке окна; по умолчанию — по типу сообщения.</param>
    /// <param name="copyPath">Путь для кнопки «Скопировать путь»; <see langword="null"/> — кнопки не будет.</param>
    public MessageDialog(
        string title,
        string message,
        string confirmText,
        string? cancelText,
        bool destructive = false,
        string? technicalDetail = null,
        bool isError = false,
        string? windowTitle = null,
        string? copyPath = null)
    {
        InitializeComponent();

        _copyPath = copyPath;

        // В заголовке окна — короткая подпись, внутри — сама формулировка.
        Title = windowTitle ?? (isError ? "Ошибка" : cancelText is null ? "Сообщение" : "Подтверждение");
        TitleText.Text = Title;
        HeadlineText.Text = title;
        MessageText.Text = message;

        ConfirmButton.Content = confirmText;

        if (cancelText is null)
        {
            CancelButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            CancelButton.Content = cancelText;
        }

        if (destructive)
        {
            ConfirmButton.Style = (Style)FindResource("Button.DangerFilled");
        }

        if (isError)
        {
            // Именно SetResourceReference, а не FindResource: тот отдал бы кисть
            // текущей темы намертво, и при переключении темы значок остался бы
            // в прежних цветах.
            StatusIcon.Kind = "status-broken";
            StatusIcon.SetResourceReference(ForegroundProperty, "Brush.Err");
            IconBox.SetResourceReference(BackgroundProperty, "Brush.ErrBg");
        }

        if (!string.IsNullOrWhiteSpace(copyPath))
        {
            CopyButton.Visibility = Visibility.Visible;
        }

        if (!string.IsNullOrWhiteSpace(technicalDetail))
        {
            DetailText.Text = technicalDetail;
            DetailBox.Visibility = Visibility.Visible;
        }
    }

    private readonly string? _copyPath;

    private void OnCopyPath(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_copyPath))
        {
            return;
        }

        try
        {
            Clipboard.SetText(_copyPath);
        }
        catch (Exception)
        {
            // Буфер обмена может быть занят другой программой — это не повод
            // ронять диалог: пользователь просто нажмёт ещё раз.
        }
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
