using System.Windows;

namespace MusicScanIntegrity.App.Views.Dialogs;

/// <summary>
/// General-purpose confirmation or message dialog. The human wording goes on
/// top, the technical cause in a separate block.
/// </summary>
public partial class MessageDialog
{
    /// <param name="title">Heading shown inside the window.</param>
    /// <param name="cancelText">Secondary button caption; <see langword="null"/> hides the button.</param>
    /// <param name="destructive">Render the primary action as destructive (red).</param>
    /// <param name="isError">Show the error icon instead of the warning icon.</param>
    /// <param name="windowTitle">Title bar caption; defaults by message kind.</param>
    /// <param name="copyPath">Path for the "Copy path" button; <see langword="null"/> hides it.</param>
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

        // Short caption in the title bar, full wording inside.
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
            // SetResourceReference rather than FindResource, so the icon brush follows
            // theme changes.
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
            // The clipboard may be held by another process; not worth failing the
            // dialog over — the user can click again.
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
