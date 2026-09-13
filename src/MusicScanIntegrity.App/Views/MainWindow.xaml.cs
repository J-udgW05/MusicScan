using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Shell;
using MusicScanIntegrity.App.Services;
using MusicScanIntegrity.App.ViewModels;

namespace MusicScanIntegrity.App.Views;

/// <summary>Главное окно программы.</summary>
public partial class MainWindow
{
    private readonly MainViewModel _viewModel;
    private readonly IDialogService _dialogs;

    /// <summary>Создаёт главное окно.</summary>
    public MainWindow(MainViewModel viewModel, IDialogService dialogs)
    {
        _viewModel = viewModel;
        _dialogs = dialogs;

        InitializeComponent();
        DataContext = viewModel;

        SetUpTaskbarProgress(viewModel);
    }

    /// <summary>
    /// Ход проверки на кнопке в панели задач: на длинной проверке окно обычно
    /// свёрнуто, и полоса на кнопке — единственный способ видеть прогресс.
    /// </summary>
    /// <remarks>
    /// Привязки задаются здесь, а не в XAML: <see cref="TaskbarItemInfo"/> —
    /// это Freezable, он не входит в визуальное дерево и DataContext не
    /// наследует, поэтому <c>{Binding TaskbarState}</c> в разметке молча ни к
    /// чему не привязывается. Источник приходится указывать явно.
    /// </remarks>
    private void SetUpTaskbarProgress(MainViewModel viewModel)
    {
        TaskbarItemInfo info = new();

        BindingOperations.SetBinding(
            info,
            TaskbarItemInfo.ProgressStateProperty,
            new Binding(nameof(MainViewModel.TaskbarState)) { Source = viewModel });

        BindingOperations.SetBinding(
            info,
            TaskbarItemInfo.ProgressValueProperty,
            new Binding(nameof(MainViewModel.TaskbarProgress)) { Source = viewModel });

        TaskbarItemInfo = info;
    }

    /// <summary>
    /// Перетаскивание папки прямо на окно — такой же способ выбора, как диалог
    /// (01_SPECIFICATION.md, раздел 6).
    /// </summary>
    private void OnDragOver(object sender, DragEventArgs e)
    {
        bool acceptable = TryGetFolder(e, out _);
        e.Effects = acceptable ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.Visibility = acceptable ? Visibility.Visible : Visibility.Collapsed;

        // Тот же признак подсвечивает и саму зону перетаскивания: если
        // подсказка окна почему-то не замечена, зона всё равно отзовётся.
        _viewModel.IsDragActive = acceptable;
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        _viewModel.IsDragActive = false;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        _viewModel.IsDragActive = false;

        if (TryGetFolder(e, out string? folder) && folder is not null)
        {
            await _viewModel.SetFolderAsync(folder);
        }
    }

    /// <summary>
    /// Закрытие во время активной проверки требует подтверждения
    /// (02_ARCHITECTURE.md, раздел 3).
    /// </summary>
    private async void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        // Согласие спрашивают один раз. Второй заход сюда — это наш же
        // повторный Close(), и проверка к этому моменту ещё может доживать
        // последние файлы: IsBusy остаётся истинным, и вопрос задавался
        // снова и снова, а программа так и не закрывалась.
        if (_closeConfirmed || !_viewModel.IsBusy)
        {
            return;
        }

        e.Cancel = true;

        bool confirmed = await _dialogs.ConfirmAsync(
            "Прервать проверку и выйти?",
            "Проверка ещё идёт. Если закрыть программу сейчас, она остановится, " +
            "а результаты останутся непрочитанными: отчёт по неполной проверке " +
            "можно выгрузить, только если сначала остановить её и не закрывать окно.",
            "Выйти",
            "Остаться",
            destructive: true);

        if (confirmed)
        {
            _closeConfirmed = true;

            // Именно StopImmediately, а не StopCommand: команда задаёт свой
            // вопрос «Остановить проверку?», и пользователь получал два
            // подтверждения подряд об одном и том же.
            _viewModel.StopImmediately();

            await Dispatcher.InvokeAsync(Close, System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    /// <summary>Пользователь уже согласился выйти — больше не переспрашиваем.</summary>
    private bool _closeConfirmed;

    /// <summary>Достаёт из перетаскиваемых данных путь к папке.</summary>
    private static bool TryGetFolder(DragEventArgs e, out string? folder)
    {
        folder = null;

        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return false;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths)
        {
            return false;
        }

        string candidate = paths[0];

        if (Directory.Exists(candidate))
        {
            folder = candidate;
            return true;
        }

        // Бросили файл — берём папку, в которой он лежит: пользователь почти
        // наверняка имел в виду именно её.
        if (File.Exists(candidate) && Path.GetDirectoryName(candidate) is { } parent && Directory.Exists(parent))
        {
            folder = parent;
            return true;
        }

        return false;
    }
}
