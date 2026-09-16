using System.Windows;
using System.Windows.Input;
using MusicScanIntegrity.App.Services;
using MusicScanIntegrity.Core.Models;
using CoreFormat = MusicScanIntegrity.Core.Common.Format;

namespace MusicScanIntegrity.App.Views.Dialogs;

/// <summary>Summary of a finished scan.</summary>
public partial class ScanFinishedDialog
{
    /// <summary>Creates the dialog from the scan summary.</summary>
    public ScanFinishedDialog(ScanSummary summary)
    {
        InitializeComponent();

        ScanCounters counters = summary.Counters;

        Title = summary.WasStopped ? "Проверка остановлена" : "Проверка завершена";
        TitleText.Text = Title;

        HeadlineText.Text = $"{CoreFormat.Number(counters.Checked)} " +
                            $"{CoreFormat.Plural(counters.Checked, "файл проверен", "файла проверено", "файлов проверено")}";

        ElapsedText.Text = $"За {CoreFormat.DurationWords(summary.Duration)}";

        CorruptedValue.Text = CoreFormat.Number(counters.Corrupted);
        WarningValue.Text = CoreFormat.Number(counters.Warnings);
        SkippedValue.Text = CoreFormat.Number(counters.Skipped);
        OkValue.Text = CoreFormat.Number(counters.Ok);

        if (summary.WasStopped)
        {
            // SetResourceReference rather than FindResource, so the icon brush follows
            // theme changes.
            StoppedNote.Visibility = Visibility.Visible;
            StatusIcon.Kind = "status-warning";
            StatusIcon.SetResourceReference(ForegroundProperty, "Brush.Warn");
            IconBox.SetResourceReference(BackgroundProperty, "Brush.WarnBg");
        }

        if (summary.PlaylistCount > 0)
        {
            PlaylistNote.Visibility = Visibility.Visible;
            PlaylistNote.Text = summary.PlaylistMissingLinks > 0
                ? $"Плейлистов проверено: {CoreFormat.Number(summary.PlaylistCount)}, " +
                  $"путей не найдено: {CoreFormat.Number(summary.PlaylistMissingLinks)}."
                : $"Плейлистов проверено: {CoreFormat.Number(summary.PlaylistCount)}, все пути на месте.";
        }
    }

    /// <summary>What the user chose.</summary>
    public ScanFinishedAction Action { get; private set; } = ScanFinishedAction.Close;

    private void OnGoToResults(object sender, RoutedEventArgs e) => CloseWith(ScanFinishedAction.GoToResults);

    private void OnGoToReport(object sender, RoutedEventArgs e) => CloseWith(ScanFinishedAction.GoToReport);

    private void OnOpenFolder(object sender, RoutedEventArgs e) => CloseWith(ScanFinishedAction.OpenFolder);

    // Esc means "do nothing": Action stays Close.
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void CloseWith(ScanFinishedAction action)
    {
        Action = action;
        DialogResult = true;
        Close();
    }
}
