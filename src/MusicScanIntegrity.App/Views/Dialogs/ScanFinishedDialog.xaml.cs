using System.Windows.Input;
using System.Windows;
using CoreFormat = MusicScanIntegrity.Core.Common.Format;
using MusicScanIntegrity.App.Services;
using MusicScanIntegrity.Core.Models;
using MusicScanIntegrity.Core.Resources;

namespace MusicScanIntegrity.App.Views.Dialogs;

/// <summary>Summary of a finished scan.</summary>
public partial class ScanFinishedDialog
{
    /// <summary>Creates the dialog from the scan summary.</summary>
    public ScanFinishedDialog(ScanSummary summary)
    {
        InitializeComponent();
        UiScale.Apply(this);

        ScanCounters counters = summary.Counters;

        Title = summary.WasStopped ? Strings.Finished_StoppedTitle : Strings.Finished_Title;
        TitleText.Text = Title;

        HeadlineText.Text = CoreFormat.Count(counters.Checked, "Plural_Html_FilesChecked");

        ElapsedText.Text = CoreFormat.Text(Strings.Finished_In, CoreFormat.DurationWords(summary.Duration));

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
                ? CoreFormat.Text(Strings.Finished_PlaylistsMissing, CoreFormat.Number(summary.PlaylistCount), CoreFormat.Number(summary.PlaylistMissingLinks))
                : CoreFormat.Text(Strings.Finished_PlaylistsOk, CoreFormat.Number(summary.PlaylistCount));
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
