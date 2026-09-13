using System.IO;
using System.Windows;
using Microsoft.Win32;
using MusicScanIntegrity.App.Services;
using MusicScanIntegrity.Core.Settings;
using CoreFormat = MusicScanIntegrity.Core.Common.Format;

namespace MusicScanIntegrity.App.Views.Dialogs;

/// <summary>Диалог экспорта отчёта: формат, состав, путь сохранения.</summary>
public partial class ExportReportDialog
{
    private readonly ExportContext _context;
    private string _folder;
    private string _fileName;

    /// <summary>Создаёт диалог.</summary>
    public ExportReportDialog(ExportContext context)
    {
        InitializeComponent();

        _context = context;
        _folder = context.DefaultFolder;
        _fileName = context.SuggestedName;

        CorruptedCount.Text = CoreFormat.Number(context.CorruptedCount);
        WarningCount.Text = CoreFormat.Number(context.WarningCount);
        PlaylistCount.Text = CoreFormat.Number(context.PlaylistMissingCount);

        // Честно предупреждаем, что полный отчёт будет тяжёлым.
        OkCount.Text = context.OkCount > 5000
            ? $"{CoreFormat.Number(context.OkCount)} — отчёт станет тяжёлым"
            : CoreFormat.Number(context.OkCount);

        IncludeOk.IsChecked = context.Settings.IncludeOkFilesInReport;

        SelectedFormat = context.Format;
        (SelectedFormat switch
        {
            ReportFormat.Csv => CsvOption,
            ReportFormat.Text => TextOption,
            _ => HtmlOption,
        }).IsChecked = true;

        UpdateTargetPath();
    }

    /// <summary>Что пользователь выбрал; заполнено, когда диалог закрыт кнопкой «Сохранить».</summary>
    public ExportChoice? Choice { get; private set; }

    private ReportFormat SelectedFormat { get; set; }

    private void OnFormatChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded && Choice is null && TargetPath is null)
        {
            return;
        }

        SelectedFormat =
            CsvOption.IsChecked == true ? ReportFormat.Csv :
            TextOption.IsChecked == true ? ReportFormat.Text :
            ReportFormat.Html;

        // Расширение файла следует за форматом, чтобы пользователю
        // не приходилось править его руками.
        string extension = SelectedFormat switch
        {
            ReportFormat.Csv => ".csv",
            ReportFormat.Text => ".txt",
            _ => ".html",
        };

        _fileName = Path.ChangeExtension(_fileName, extension);
        UpdateTargetPath();
    }

    private void OnChangePath(object sender, RoutedEventArgs e)
    {
        SaveFileDialog dialog = new()
        {
            Title = "Куда сохранить отчёт",
            FileName = _fileName,
            AddExtension = true,
            OverwritePrompt = true,
            Filter = SelectedFormat switch
            {
                ReportFormat.Csv => "Таблица CSV (*.csv)|*.csv",
                ReportFormat.Text => "Текстовый файл (*.txt)|*.txt",
                _ => "HTML-страница (*.html)|*.html",
            },
        };

        if (Directory.Exists(_folder))
        {
            dialog.InitialDirectory = _folder;
        }

        if (dialog.ShowDialog(this) == true)
        {
            _folder = Path.GetDirectoryName(dialog.FileName) ?? _folder;
            _fileName = Path.GetFileName(dialog.FileName);
            UpdateTargetPath();
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        Choice = new ExportChoice(
            SelectedFormat,
            Path.Combine(_folder, _fileName),
            IncludeCorrupted.IsChecked == true,
            IncludeWarnings.IsChecked == true,
            IncludeOk.IsChecked == true,
            IncludePlaylists.IsChecked == true);

        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void UpdateTargetPath() => TargetPath.Text = Path.Combine(_folder, _fileName);
}
