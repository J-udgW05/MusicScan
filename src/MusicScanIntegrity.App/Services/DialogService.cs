using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using MusicScanIntegrity.App.Views.Dialogs;
using MusicScanIntegrity.Core.Models;
using MusicScanIntegrity.Core.Resources;
using MusicScanIntegrity.Core.Settings;

namespace MusicScanIntegrity.App.Services;

/// <summary>Dialogs and system pickers.</summary>
public interface IDialogService
{
    /// <summary>Folder picker; <see langword="null"/> when cancelled.</summary>
    string? PickFolder(string title, string? initialFolder);

    /// <summary>Save file dialog; <see langword="null"/> when cancelled.</summary>
    string? PickSaveFile(string title, string filter, string suggestedName, string? initialFolder);

    /// <summary>Colour picker returning "#RRGGBB"; <see langword="null"/> when cancelled.</summary>
    string? PickColor(string title, string currentHex);

    /// <summary>Yes/no question.</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText, bool destructive = false, string? copyPath = null, string? technicalDetail = null, bool isError = false);

    /// <summary>Message with a single button.</summary>
    Task ShowMessageAsync(string title, string message, string? technicalDetail = null, bool isError = false, string? copyPath = null);

    /// <summary>Report export dialog; <see langword="null"/> when cancelled.</summary>
    ExportChoice? AskExport(ExportContext context);

    /// <summary>Summary of a finished scan.</summary>
    Task<ScanFinishedAction> ShowScanFinishedAsync(ScanSummary summary);

    /// <summary>About window.</summary>
    void ShowAbout(AboutInfo info);

    /// <summary>Help window.</summary>
    void ShowHelp();

    /// <summary>First-run welcome; returns "do not show again".</summary>
    bool ShowFirstRun(out bool openFolderPicker);

    /// <summary>Opens Explorer with the file selected.</summary>
    void RevealInExplorer(string path);

    /// <summary>Opens the file with its default application.</summary>
    void OpenFile(string path);
}

/// <summary>What the user chose in the export dialog.</summary>
/// <param name="IncludePlaylists">Include broken playlist references.</param>
public sealed record ExportChoice(
    ReportFormat Format,
    string FilePath,
    bool IncludeCorrupted,
    bool IncludeWarnings,
    bool IncludeOk,
    bool IncludePlaylists);

/// <summary>What the export dialog shows.</summary>
/// <param name="Format">
/// Format to open the dialog on. This is the choice made on the report tab,
/// not the default from settings: the user picked a format card and pressed
/// Save, so the dialog must open on that same format.
/// </param>
public sealed record ExportContext(
    AppSettings Settings,
    int CorruptedCount,
    int WarningCount,
    int OkCount,
    int PlaylistMissingCount,
    string SuggestedName,
    string DefaultFolder,
    ReportFormat Format);

/// <summary>What the user chose in the scan-finished dialog.</summary>
public enum ScanFinishedAction
{
    /// <summary>Closed the dialog.</summary>
    Close,

    /// <summary>Go to the results.</summary>
    GoToResults,

    /// <summary>Go to the report.</summary>
    GoToReport,

    /// <summary>Open the scanned folder.</summary>
    OpenFolder,
}

/// <summary>Information for the about window.</summary>
/// <param name="AudioEngine">Decoder status.</param>
/// <param name="Plugins">Loaded format plug-ins.</param>
public sealed record AboutInfo(
    string Version,
    string BuildDate,
    string AudioEngine,
    IReadOnlyList<string> Plugins);

/// <inheritdoc cref="IDialogService" />
public sealed class DialogService : IDialogService
{
    /// <inheritdoc />
    public string? PickFolder(string title, string? initialFolder)
    {
        OpenFolderDialog dialog = new()
        {
            Title = title,
            Multiselect = false,
        };

        if (!string.IsNullOrWhiteSpace(initialFolder) && Directory.Exists(initialFolder))
        {
            dialog.InitialDirectory = initialFolder;
        }

        return dialog.ShowDialog(Owner()) == true ? dialog.FolderName : null;
    }

    /// <inheritdoc />
    public string? PickSaveFile(string title, string filter, string suggestedName, string? initialFolder)
    {
        SaveFileDialog dialog = new()
        {
            Title = title,
            Filter = filter,
            FileName = suggestedName,
            AddExtension = true,
            OverwritePrompt = true,
        };

        if (!string.IsNullOrWhiteSpace(initialFolder) && Directory.Exists(initialFolder))
        {
            dialog.InitialDirectory = initialFolder;
        }

        return dialog.ShowDialog(Owner()) == true ? dialog.FileName : null;
    }

    /// <inheritdoc />
    public string? PickColor(string title, string currentHex)
    {
        ColorPickDialog dialog = new(title, currentHex) { Owner = Owner() };
        return dialog.ShowDialog() == true ? dialog.SelectedHex : null;
    }

    /// <inheritdoc />
    public Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText, bool destructive = false, string? copyPath = null, string? technicalDetail = null, bool isError = false)
    {
        MessageDialog dialog = new(title, message, confirmText, cancelText, destructive, technicalDetail, isError, copyPath: copyPath)
        {
            Owner = Owner(),
        };

        return Task.FromResult(dialog.ShowDialog() == true);
    }

    /// <inheritdoc />
    public Task ShowMessageAsync(string title, string message, string? technicalDetail = null, bool isError = false, string? copyPath = null)
    {
        MessageDialog dialog = new(title, message, Strings.Ui_GotIt, cancelText: null, destructive: false, technicalDetail, isError, copyPath: copyPath)
        {
            Owner = Owner(),
        };

        dialog.ShowDialog();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ExportChoice? AskExport(ExportContext context)
    {
        ExportReportDialog dialog = new(context) { Owner = Owner() };
        return dialog.ShowDialog() == true ? dialog.Choice : null;
    }

    /// <inheritdoc />
    public Task<ScanFinishedAction> ShowScanFinishedAsync(ScanSummary summary)
    {
        ScanFinishedDialog dialog = new(summary) { Owner = Owner() };
        dialog.ShowDialog();
        return Task.FromResult(dialog.Action);
    }

    /// <inheritdoc />
    public void ShowAbout(AboutInfo info)
    {
        AboutDialog dialog = new(info) { Owner = Owner() };
        dialog.ShowDialog();
    }

    /// <inheritdoc />
    public void ShowHelp()
    {
        HelpDialog dialog = new() { Owner = Owner() };
        dialog.ShowDialog();
    }


    /// <inheritdoc />
    public bool ShowFirstRun(out bool openFolderPicker)
    {
        FirstRunDialog dialog = new() { Owner = Owner() };
        bool? result = dialog.ShowDialog();
        openFolderPicker = result == true;
        return dialog.DoNotShowAgain;
    }

    /// <inheritdoc />
    public void RevealInExplorer(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
            else if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
        }
        catch (Exception)
        {
            // Explorer may be blocked by policy; not worth crashing over.
        }
    }

    /// <inheritdoc />
    public void OpenFile(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // No application registered for this file type; ignore.
        }
    }

    private static Window? Owner() =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        ?? Application.Current?.MainWindow;
}
