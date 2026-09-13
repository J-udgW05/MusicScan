using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using MusicScanIntegrity.App.Views.Dialogs;
using MusicScanIntegrity.Core.Models;
using MusicScanIntegrity.Core.Settings;

namespace MusicScanIntegrity.App.Services;

/// <summary>Диалоги и системные окна выбора.</summary>
public interface IDialogService
{
    /// <summary>Окно выбора папки; <see langword="null"/>, если пользователь отказался.</summary>
    string? PickFolder(string title, string? initialFolder);

    /// <summary>Окно сохранения файла; <see langword="null"/>, если пользователь отказался.</summary>
    string? PickSaveFile(string title, string filter, string suggestedName, string? initialFolder);

    /// <summary>Выбор цвета в формате «#RRGGBB»; <see langword="null"/> при отказе.</summary>
    string? PickColor(string title, string currentHex);

    /// <summary>Вопрос «да / нет».</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText, bool destructive = false, string? copyPath = null, string? technicalDetail = null, bool isError = false);

    /// <summary>Сообщение с одной кнопкой.</summary>
    Task ShowMessageAsync(string title, string message, string? technicalDetail = null, bool isError = false, string? copyPath = null);

    /// <summary>Диалог экспорта отчёта; <see langword="null"/> при отказе.</summary>
    ExportChoice? AskExport(ExportContext context);

    /// <summary>Итоги завершённой проверки.</summary>
    Task<ScanFinishedAction> ShowScanFinishedAsync(ScanSummary summary);

    /// <summary>Окно «О программе».</summary>
    void ShowAbout(AboutInfo info);

    /// <summary>Окно справки.</summary>
    void ShowHelp();

    /// <summary>Приветствие при первом запуске; возвращает «не показывать больше».</summary>
    bool ShowFirstRun(out bool openFolderPicker);

    /// <summary>Открывает проводник и выделяет файл.</summary>
    void RevealInExplorer(string path);

    /// <summary>Открывает файл программой по умолчанию.</summary>
    void OpenFile(string path);
}

/// <summary>Что пользователь выбрал в диалоге экспорта.</summary>
/// <param name="Format">Формат отчёта.</param>
/// <param name="FilePath">Куда сохранять.</param>
/// <param name="IncludeCorrupted">Включить повреждённые файлы.</param>
/// <param name="IncludeWarnings">Включить предупреждения.</param>
/// <param name="IncludeOk">Включить файлы «в порядке».</param>
/// <param name="IncludePlaylists">Включить битые ссылки плейлистов.</param>
public sealed record ExportChoice(
    ReportFormat Format,
    string FilePath,
    bool IncludeCorrupted,
    bool IncludeWarnings,
    bool IncludeOk,
    bool IncludePlaylists);

/// <summary>Что показать в диалоге экспорта.</summary>
/// <param name="Settings">Текущие настройки.</param>
/// <param name="CorruptedCount">Сколько повреждённых файлов.</param>
/// <param name="WarningCount">Сколько предупреждений.</param>
/// <param name="OkCount">Сколько файлов «в порядке».</param>
/// <param name="PlaylistMissingCount">Сколько битых ссылок в плейлистах.</param>
/// <param name="SuggestedName">Предлагаемое имя файла.</param>
/// <param name="DefaultFolder">Папка по умолчанию.</param>
/// <param name="Format">
/// Формат, с которым открыть диалог. Это выбор со вкладки «Отчёт», а не
/// значение по умолчанию из настроек: пользователь выбрал карточку формата,
/// нажал «Сохранить…» — и диалог обязан открыться на том же формате.
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

/// <summary>Что пользователь выбрал в окне «Проверка завершена».</summary>
public enum ScanFinishedAction
{
    /// <summary>Закрыл окно.</summary>
    Close,

    /// <summary>Перейти к результатам.</summary>
    GoToResults,

    /// <summary>Перейти к отчёту.</summary>
    GoToReport,

    /// <summary>Открыть проверенную папку.</summary>
    OpenFolder,
}

/// <summary>Сведения для окна «О программе».</summary>
/// <param name="Version">Версия сборки.</param>
/// <param name="BuildDate">Дата сборки.</param>
/// <param name="AudioEngine">Состояние механизма декодирования.</param>
/// <param name="Plugins">Загруженные плагины форматов.</param>
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
        MessageDialog dialog = new(title, message, "Понятно", cancelText: null, destructive: false, technicalDetail, isError, copyPath: copyPath)
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
            // Проводник может быть недоступен политикой — не повод падать.
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
            // Нет программы для этого типа файла — молча пропускаем.
        }
    }

    private static Window? Owner() =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        ?? Application.Current?.MainWindow;
}
