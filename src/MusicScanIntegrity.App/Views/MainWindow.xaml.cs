using System.IO;
using System.Windows.Data;
using System.Windows.Shell;
using System.Windows;
using MusicScanIntegrity.App.Services;
using MusicScanIntegrity.App.ViewModels;
using MusicScanIntegrity.Core.Resources;

namespace MusicScanIntegrity.App.Views;

/// <summary>Main application window.</summary>
public partial class MainWindow
{
    private readonly MainViewModel _viewModel;
    private readonly IDialogService _dialogs;

    public MainWindow(MainViewModel viewModel, IDialogService dialogs)
    {
        _viewModel = viewModel;
        _dialogs = dialogs;

        InitializeComponent();
        DataContext = viewModel;

        SetUpTaskbarProgress(viewModel);
    }

    /// <summary>
    /// Scan progress on the taskbar button; during a long scan the window is
    /// usually minimised and this is the only visible progress.
    /// </summary>
    /// <remarks>
    /// Bound in code rather than XAML: <see cref="TaskbarItemInfo"/> is a
    /// Freezable outside the visual tree and does not inherit DataContext, so
    /// <c>{Binding TaskbarState}</c> in markup silently binds to nothing.
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

    /// <summary>Dropping a folder onto the window selects it, same as the picker.</summary>
    private void OnDragOver(object sender, DragEventArgs e)
    {
        bool acceptable = TryGetFolder(e, out _);
        e.Effects = acceptable ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.Visibility = acceptable ? Visibility.Visible : Visibility.Collapsed;

        // The same flag highlights the drop zone, so it responds even if the window
        // hint goes unnoticed.
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

    /// <summary>Closing during an active scan asks for confirmation.</summary>
    private async void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        // Ask only once. Re-entry here is our own second Close() while the scan is
        // still finishing its last files; IsBusy stays true, and the question used
        // to repeat forever without the window ever closing.
        if (_closeConfirmed || !_viewModel.IsBusy)
        {
            return;
        }

        e.Cancel = true;

        bool confirmed = await _dialogs.ConfirmAsync(
            Strings.Main_ExitConfirm_Title,
            Strings.Main_ExitConfirm_Text,
            Strings.Main_Exit,
            Strings.Main_Stay,
            destructive: true);

        if (confirmed)
        {
            _closeConfirmed = true;

            // StopImmediately rather than StopCommand: the command asks "Stop the
            // scan?" itself, and the user got two confirmations in a row.
            _viewModel.StopImmediately();

            await Dispatcher.InvokeAsync(Close, System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    /// <summary>The user already agreed to exit; do not ask again.</summary>
    private bool _closeConfirmed;

    /// <summary>Extracts a folder path from drag-and-drop data.</summary>
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

        // A dropped file means its containing folder, which is almost certainly
        // what the user meant.
        if (File.Exists(candidate) && Path.GetDirectoryName(candidate) is { } parent && Directory.Exists(parent))
        {
            folder = parent;
            return true;
        }

        return false;
    }
}
