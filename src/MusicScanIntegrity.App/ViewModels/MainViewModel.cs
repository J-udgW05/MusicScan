using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicScanIntegrity.App.Services;
using MusicScanIntegrity.Core.Audio;
using MusicScanIntegrity.Core.Discovery;
using MusicScanIntegrity.Core.Locking;
using MusicScanIntegrity.Core.Analysis;
using MusicScanIntegrity.Core.History;
using MusicScanIntegrity.Core.Models;
using MusicScanIntegrity.Core.Reporting;
using MusicScanIntegrity.Core.Scanning;
using MusicScanIntegrity.Core.Settings;
using CoreFormat = MusicScanIntegrity.Core.Common.Format;

namespace MusicScanIntegrity.App.ViewModels;

/// <summary>Main window: toolbar, tabs, scan progress and status bar.</summary>
public sealed partial class MainViewModel : ObservableObject, ILockedFileDecisionProvider, IDisposable
{
    private const int TabScan = 0;
    private const int TabResults = 1;
    private const int TabReport = 2;

    /// <summary>How many warnings the live list on the scan tab keeps.</summary>
    private const int LiveWarningLimit = 50;

    private readonly ISettingsService _settingsService;
    private readonly IFileDiscoveryService _discovery;
    private readonly IScanEngine _engine;
    private readonly IReportService _reports;
    private readonly IDialogService _dialogs;
    private readonly IThemeService _theme;
    private readonly IAudioProbe _audioProbe;
    private readonly IScanHistory _history;

    private DiscoveryResult? _lastDiscovery;
    private ScanSummary? _lastSummary;

    /// <summary>Collection findings from the last scan.</summary>
    private IReadOnlyList<CollectionFinding> _findings = [];
    private CancellationTokenSource? _discoveryCts;
    private TaskCompletionSource<LockedFileDecision>? _pendingAnswer;

    [ObservableProperty]
    private int _selectedTab;

    /// <summary>
    /// The settings screen is open. It overlays the tab content in the same window
    /// rather than opening a separate window the user would have to find again.
    /// </summary>
    [ObservableProperty]
    private bool _isSettingsOpen;

    /// <summary>A folder is being dragged over the window; the drop zone lights up.</summary>
    [ObservableProperty]
    private bool _isDragActive;

    // Clicking a tab returns to work and closes the settings screen.
    partial void OnSelectedTabChanged(int value) => IsSettingsOpen = false;

    [ObservableProperty]
    private string _folderPath = string.Empty;

    [ObservableProperty]
    private bool _isOfferVisible;

    [ObservableProperty]
    private string _offerTitle = string.Empty;

    [ObservableProperty]
    private string _offerSubtitle = "Можно начинать — параметры проверки справа.";

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private string _currentFile = string.Empty;

    [ObservableProperty]
    private string _statusCounts = string.Empty;

    // Summary counters on the scan tab.
    [ObservableProperty]
    private string _totalFiles = "0";

    [ObservableProperty]
    private string _checkedFiles = "0";

    [ObservableProperty]
    private string _okFiles = "0";

    [ObservableProperty]
    private string _corruptedFiles = "0";

    [ObservableProperty]
    private string _warningFiles = "0";

    [ObservableProperty]
    private string _skippedFiles = "0";

    // Quick settings in the side column.
    [ObservableProperty]
    private bool _recursive = true;

    [ObservableProperty]
    private bool _checkMetadata;

    [ObservableProperty]
    private SettingsViewModel.ThresholdOption _threshold = SettingsViewModel.ThresholdOptions[1];

    [ObservableProperty]
    private SettingsViewModel.TimeoutOption _timeout = SettingsViewModel.TimeoutOptions[2];

    // Locked-file question card.
    [ObservableProperty]
    private bool _hasLockedQuestion;

    [ObservableProperty]
    private string _lockedFilePath = string.Empty;

    [ObservableProperty]
    private string _lockedOwnerText = string.Empty;

    [ObservableProperty]
    private string _lockedQueueText = string.Empty;

    [ObservableProperty]
    private bool _lockedApplyToAll;

    [ObservableProperty]
    private bool _canCloseOwner;

    [ObservableProperty]
    private bool _canApplyToAll;

    /// <summary>Total warnings raised, not just those that fit the list.</summary>
    [ObservableProperty]
    private int _liveWarningTotal;

    /// <summary>Caption under the list when not every warning is shown.</summary>
    public string LiveWarningNote => LiveWarningTotal > LiveWarnings.Count
        ? $"Показаны последние {LiveWarnings.Count} — полный список на вкладке «Результаты»"
        : string.Empty;

    partial void OnLiveWarningTotalChanged(int value) => OnPropertyChanged(nameof(LiveWarningNote));

    // Explanations under each choice are computed from the current settings and
    // the file itself, so the numbers in them are real rather than approximate.
    [ObservableProperty]
    private string _lockedWaitNote = string.Empty;

    [ObservableProperty]
    private string _lockedCopyNote = string.Empty;

    [ObservableProperty]
    private string _lockedOwnerNote = string.Empty;

    public MainViewModel(
        ISettingsService settingsService,
        IFileDiscoveryService discovery,
        IScanEngine engine,
        IReportService reports,
        IDialogService dialogs,
        IThemeService theme,
        IAudioProbe audioProbe,
        IScanHistory history,
        LockedFileDecisionRelay lockedFileRelay,
        ResultsViewModel results,
        ReportViewModel report,
        SettingsViewModel settings)
    {
        _settingsService = settingsService;
        _discovery = discovery;
        _engine = engine;
        _reports = reports;
        _dialogs = dialogs;
        _theme = theme;
        _audioProbe = audioProbe;
        _history = history;

        Results = results;
        Report = report;
        Settings = settings;

        // The engine asks about locked files through the relay; install ourselves.
        lockedFileRelay.Target = this;

        _engine.ResultsReady += OnResultsReady;
        _engine.PlaylistsReady += OnPlaylistsReady;
        _engine.ProgressChanged += OnProgressChanged;
        _engine.WarningRaised += OnWarningRaised;
        _engine.Finished += OnFinished;

        _settingsService.Changed += OnSettingsChanged;
        Settings.Applied += (_, _) => SyncQuickSettings();

        SyncQuickSettings();
    }

    /// <summary>Results tab.</summary>
    public ResultsViewModel Results { get; }

    /// <summary>Report tab.</summary>
    public ReportViewModel Report { get; }

    /// <summary>Settings screen.</summary>
    public SettingsViewModel Settings { get; }

    /// <summary>Warnings raised while the scan runs.</summary>
    public ObservableCollection<LiveWarningRow> LiveWarnings { get; } = [];

    /// <summary>Pause button caption; reads "Resume" while paused.</summary>
    public string PauseButtonText => IsPaused ? "Продолжить" : "Пауза";

    /// <summary>Pause button icon.</summary>
    public string PauseButtonIcon => IsPaused ? "play" : "pause";

    /// <summary>A scan is running; closing the window then asks for confirmation.</summary>
    public bool IsBusy => IsScanning;

    /// <summary>Progress for the taskbar, 0 to 1.</summary>
    public double TaskbarProgress => Math.Clamp(Progress / 100.0, 0, 1);

    /// <summary>
    /// Taskbar progress state. Scans take minutes and the window is usually
    /// minimised: green while running, yellow when paused or waiting for a
    /// locked-file answer.
    /// </summary>
    public TaskbarItemProgressState TaskbarState => !IsScanning
        ? TaskbarItemProgressState.None
        : IsPaused || HasLockedQuestion
            ? TaskbarItemProgressState.Paused
            : TaskbarItemProgressState.Normal;

    partial void OnProgressChanged(double value) => OnPropertyChanged(nameof(TaskbarProgress));



    // ── Folder selection ─────────────────────────────────────────────────

    /// <summary>Opens the folder picker.</summary>
    [RelayCommand]
    private async Task PickFolderAsync()
    {
        string? folder = _dialogs.PickFolder("Папка с музыкой", FolderPath);
        if (folder is not null)
        {
            await SetFolderAsync(folder);
        }
    }

    /// <summary>
    /// Accepts a folder from the picker or drag-and-drop, counts its files right
    /// away and offers to start the scan.
    /// </summary>
    public async Task SetFolderAsync(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        FolderPath = folder;
        IsOfferVisible = false;

        // A folder is picked in order to scan it. If that happened on the results or
        // report tab, the Start button would be left on another tab.
        SelectedTab = TabScan;

        // Close settings separately. Switching tabs is not enough: the scan tab is
        // usually already selected, the value does not change and no event fires, so
        // a folder picked from settings used to leave the user in settings.
        IsSettingsOpen = false;

        AppSettings settings = _settingsService.Current;

        if (settings.RememberLastFolder)
        {
            settings.LastFolder = folder;
            await _settingsService.SaveAsync();
        }

        // A previous count may still be running; cancel it first.
        if (_discoveryCts is { } previous)
        {
            await previous.CancelAsync();
            previous.Dispose();
        }

        _discoveryCts = new CancellationTokenSource();

        try
        {
            DiscoveryResult result = await _discovery.DiscoverAsync(folder, settings, progress: null, _discoveryCts.Token);
            _lastDiscovery = result;

            TotalFiles = CoreFormat.Number(result.AudioItems.Count);
            StatusCounts = $"{CoreFormat.Files(result.AudioItems.Count)} · 0 проверено";

            OfferTitle = result.AudioItems.Count == 0
                ? "Подходящих файлов не нашлось"
                : $"Найдено {CoreFormat.Number(result.AudioItems.Count)} подходящих " +
                  CoreFormat.Plural(result.AudioItems.Count, "файла", "файлов", "файлов");

            // No longer suggests "check the settings": there is no settings button next
            // to Start, and the scan options are on the right.
            OfferSubtitle = result.Playlists.Count > 0
                ? $"Ещё {CoreFormat.Number(result.Playlists.Count)} " +
                  $"{CoreFormat.Plural(result.Playlists.Count, "плейлист", "плейлиста", "плейлистов")}. " +
                  "Можно начинать — параметры проверки справа."
                : "Можно начинать — параметры проверки справа.";

            IsOfferVisible = settings.OfferStartAfterFolderSelected && result.AudioItems.Count > 0;

            // Without this Start stays disabled: RelayCommand caches CanExecute and does
            // not learn about the new folder by itself.
            StartCommand.NotifyCanExecuteChanged();

            if (result.InaccessibleFolders.Count > 0)
            {
                await ReportInaccessibleAsync(result);
            }
        }
        catch (OperationCanceledException)
        {
            // The user picked another folder while this one was being counted.
        }
        catch (Exception ex)
        {
            _lastDiscovery = null;
            StartCommand.NotifyCanExecuteChanged();

            // Three actions: copy path, retry and close. Retry makes sense because the
            // folder may have been temporarily busy or not mounted.
            bool retry = await _dialogs.ConfirmAsync(
                "Папку не удалось прочитать",
                "Обойти эту папку не получилось. Проверьте, что она существует и доступна.",
                "Повторить",
                "Понятно",
                destructive: false,
                copyPath: folder,
                technicalDetail: $"{ex.GetType().Name} · {ex.Message}",
                isError: true);

            if (retry)
            {
                await SetFolderAsync(folder);
            }
        }
    }

    // ── Scan control ─────────────────────────────────────────────────────

    /// <summary>Brings the history database in line with the setting before a scan.</summary>
    /// <remarks>
    /// Opened here rather than at startup: with tracking off the database is not
    /// needed and should not be created.
    /// </remarks>
    private void PrepareHistory(AppSettings settings)
    {
        if (settings.TrackChanges && !_history.IsOpen)
        {
            _history.Open(ScanHistory.ResolveDefaultPath());
        }
        else if (!settings.TrackChanges && _history.IsOpen)
        {
            _history.Close();
        }
    }

    /// <summary>Starts the scan.</summary>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (_lastDiscovery is null)
        {
            return;
        }

        if (!_audioProbe.IsAvailable)
        {
            await _dialogs.ShowMessageAsync(
                "Механизм декодирования не запущен",
                "Проверить файлы нельзя: библиотеки BASS не найдены или не загрузились. " +
                "Проверьте, что рядом с программой есть папка bass с bass.dll и плагинами формата.",
                isError: true);
            return;
        }

        IsOfferVisible = false;
        Results.Clear();
        Report.Clear();
        LiveWarnings.Clear();
        LiveWarningTotal = 0;

        AppSettings settings = _settingsService.Current;
        PrepareHistory(settings);

        IsScanning = true;
        IsPaused = false;
        SelectedTab = TabScan;

        try
        {
            await _engine.RunAsync(_lastDiscovery, settings);
        }
        catch (Exception ex)
        {
            await _dialogs.ShowMessageAsync(
                "Проверка прервана",
                "Во время проверки произошла ошибка, из-за которой продолжать нельзя. " +
                "Всё, что успели проверить, осталось в результатах.",
                $"{ex.GetType().Name} · {ex.Message}",
                isError: true);
        }
        finally
        {
            IsScanning = false;
            IsPaused = false;
            HasLockedQuestion = false;
        }
    }

    private bool CanStart() => !IsScanning && _lastDiscovery is { AudioItems.Count: > 0 };

    /// <summary>Pauses or resumes the scan.</summary>
    [RelayCommand(CanExecute = nameof(CanControl))]
    private void TogglePause()
    {
        if (IsPaused)
        {
            _engine.Resume();
            IsPaused = false;
        }
        else
        {
            _engine.Pause();
            IsPaused = true;
        }
    }

    /// <summary>
    /// Recounts the selected folder and restarts the scan, for when the folder
    /// changed since the last run.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRescan))]
    private async Task RescanAsync()
    {
        string folder = FolderPath;

        SelectedTab = TabScan;
        await SetFolderAsync(folder);

        if (CanStart())
        {
            await StartAsync();
        }
    }

    private bool CanRescan() =>
        !IsScanning && !string.IsNullOrWhiteSpace(FolderPath) && Directory.Exists(FolderPath);

    /// <summary>
    /// Stops the scan without asking. Used when closing the window, which has
    /// already asked; a second identical question would be absurd.
    /// </summary>
    public void StopImmediately()
    {
        // The app is closing, so there is nobody to show a summary to. Without this
        // flag a "Scan stopped" dialog popped up over the closing window and blocked
        // the shutdown.
        _closing = true;
        _engine.Stop();
    }

    private bool _closing;

    /// <summary>Stops the scan after asking for confirmation.</summary>
    [RelayCommand(CanExecute = nameof(CanControl))]
    private async Task StopAsync()
    {
        bool confirmed = await _dialogs.ConfirmAsync(
            "Остановить проверку?",
            $"Проверено {CheckedFiles} из {TotalFiles} файлов. Результаты уже найденных файлов сохранятся — " +
            "отчёт можно будет выгрузить по неполной проверке. Продолжить с этого места позже не получится.",
            "Остановить",
            "Продолжить проверку",
            destructive: true);

        if (confirmed)
        {
            _engine.Stop();
        }
    }

    private bool CanControl() => IsScanning;

    // ── Locked-file question ─────────────────────────────────────────────

    /// <summary>Shows the locked-file question as a card on the scan tab.</summary>
    /// <remarks>
    /// A card rather than a window: it does not cover the progress, and the queue
    /// already guarantees one question at a time. If another tab is open the app
    /// switches to the scan tab so the question is not missed.
    /// </remarks>
    public Task<LockedFileDecision> AskAsync(LockedFileQuestion question, CancellationToken cancellationToken)
    {
        TaskCompletionSource<LockedFileDecision> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingAnswer = answer;

        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            LockedFilePath = question.FilePath;
            LockedOwnerText = question.Owner.DisplayText;
            LockedQueueText = question.QueuedAfterThis > 0
                ? $"Вопросы задаются по одному — ещё {question.QueuedAfterThis} в очереди"
                : "Вопросы задаются по одному";

            AppSettings current = _settingsService.Current;

            LockedWaitNote = $"{CoreFormat.Seconds(current.LockedWaitSeconds)}, " +
                             $"до {current.LockedRetryCount} " +
                             CoreFormat.Plural(current.LockedRetryCount, "попытки", "попыток", "попыток");

            LockedCopyNote = $"{CoreFormat.Size(question.SizeBytes)} будет скопировано " +
                             "в %TEMP% и удалено после";

            // DisplayName explicitly: the LockOwner record has no custom ToString(), and
            // the caption used to dump the whole record.
            LockedOwnerNote = question.Owner.Owners.Count > 0
                ? "Запрос на закрытие получит: " +
                  string.Join(", ", question.Owner.Owners.Select(o => o.DisplayName))
                : string.Empty;

            LockedApplyToAll = false;
            CanCloseOwner = current.OfferCloseOwner && question.Owner.IsReliable && question.Owner.Owners.Count > 0;
            CanApplyToAll = current.AllowApplyToAllLocked;
            HasLockedQuestion = true;
            SelectedTab = TabScan;
        });

        cancellationToken.Register(() => answer.TrySetCanceled(cancellationToken));
        return answer.Task;
    }

    /// <summary>Answers the locked-file question.</summary>
    [RelayCommand]
    private void AnswerLocked(string action)
    {
        LockedFileAction decision = action switch
        {
            "skip" => LockedFileAction.Skip,
            "wait" => LockedFileAction.Wait,
            "copy" => LockedFileAction.TempCopy,
            "close" => LockedFileAction.CloseOwner,
            _ => LockedFileAction.Skip,
        };

        HasLockedQuestion = false;
        _pendingAnswer?.TrySetResult(new LockedFileDecision(decision, LockedApplyToAll));
        _pendingAnswer = null;
    }

    /// <summary>Stops the scan from within the locked-file question.</summary>
    [RelayCommand]
    private void StopFromLocked()
    {
        HasLockedQuestion = false;
        _pendingAnswer?.TrySetResult(new LockedFileDecision(LockedFileAction.Skip, StopScan: true));
        _pendingAnswer = null;
    }

    // ── Report ───────────────────────────────────────────────────────────

    /// <summary>Saves the report in the chosen format.</summary>
    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        if (_lastSummary is null)
        {
            return;
        }

        AppSettings settings = _settingsService.Current;

        try
        {
            ExportContext context = new(
                settings,
                Results.CorruptedCount,
                Results.WarningCount,
                Results.OkCount,
                Results.PlaylistMissingCount,
                _reports.SuggestFileName(Report.SelectedFormat, DateTimeOffset.Now),
                _reports.ResolveDefaultFolder(settings),
                Report.SelectedFormat);

            ExportChoice? choice = _dialogs.AskExport(context);
            if (choice is null)
            {
                return;
            }

            List<FileCheckResult> selected = [.. Results.All
                .Select(r => r.Result)
                .Where(r => r.Status switch
                {
                    CheckStatus.Corrupted => choice.IncludeCorrupted,
                    CheckStatus.Warning => choice.IncludeWarnings,
                    CheckStatus.Ok => choice.IncludeOk,
                    CheckStatus.Skipped => choice.IncludeWarnings,
                    _ => true,
                })
                .OrderByDescending(r => r.Status == CheckStatus.Corrupted)
                .ThenByDescending(r => r.Status == CheckStatus.Warning)
                .ThenBy(r => r.FullPath, StringComparer.OrdinalIgnoreCase)];

            IReadOnlyList<PlaylistCheckResult> playlists = choice.IncludePlaylists ? [.. Results.Playlists] : [];

            ReportData data = new(_lastSummary, selected, playlists, DateTimeOffset.Now, _findings);

            await _reports.SaveAsync(choice.Format, choice.FilePath, data);

            RememberReportsFolder(choice.FilePath);

            if (settings.OpenReportAfterSave)
            {
                _dialogs.OpenFile(choice.FilePath);
            }
        }
        catch (Exception ex)
        {
            // Async command errors are otherwise swallowed: the MVVM toolkit stores them
            // in the task and shows nobody. A silently failed export is the worst outcome.
            await _dialogs.ShowMessageAsync(
                "Отчёт не сохранён",
                "Сохранить отчёт не получилось. Проверьте, что папка доступна на запись и на диске есть место.",
                $"{ex.GetType().Name} · {ex.Message}",
                isError: true);
        }
    }

    /// <summary>Remembers the folder the user saved the report to.</summary>
    /// <remarks>
    /// Written through the settings view model rather than directly: it keeps its
    /// own draft and rewrites all settings from it on any toggle, which would
    /// revert a value stored behind its back.
    /// <para>
    /// Only after a successful save — a path that could not be written to is not
    /// worth offering again.
    /// </para>
    /// </remarks>
    private void RememberReportsFolder(string filePath)
    {
        string? folder = _reports.FolderToRemember(
            filePath,
            _reports.ResolveDefaultFolder(_settingsService.Current));

        if (folder is not null)
        {
            Settings.ReportsFolder = folder;
        }
    }

    private bool CanExport() => _lastSummary is not null && Results.TotalCount > 0;

    // ── Toolbar ──────────────────────────────────────────────────────────

    /// <summary>Opens the settings screen.</summary>
    [RelayCommand]
    private void OpenSettings() => IsSettingsOpen = true;

    /// <summary>Returns from settings to work.</summary>
    [RelayCommand]
    private void CloseSettings() => IsSettingsOpen = false;

    /// <summary>
    /// Action on a live warning row: dismiss it, or jump to the file on the
    /// results tab.
    /// </summary>
    [RelayCommand]
    private void LiveWarningAction(LiveWarningRow? row)
    {
        if (row is null)
        {
            return;
        }

        if (row.ActionDismisses)
        {
            LiveWarnings.Remove(row);
            return;
        }

        Results.ShowSingle(System.IO.Path.GetFileName(row.Path));
        SelectedTab = TabResults;
    }

    /// <summary>Shows help.</summary>
    [RelayCommand]
    private void ShowHelp() => _dialogs.ShowHelp();

    /// <summary>Shows the about window.</summary>
    [RelayCommand]
    private void ShowAbout()
    {
        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        string buildDate = File.GetLastWriteTime(Assembly.GetExecutingAssembly().Location).ToString("yyyy-MM-dd");

        IReadOnlyList<string> plugins = _audioProbe is BassAudioProbe bass ? bass.LoadedPlugins : [];

        _dialogs.ShowAbout(new AboutInfo(
            version,
            buildDate,
            _audioProbe.IsAvailable ? "BASS запущена" : "BASS не запущена",
            plugins));
    }

    /// <summary>Reveals the file from a results row in Explorer.</summary>
    [RelayCommand(CanExecute = nameof(HasRow))]
    private void RevealResult(FileResultViewModel? row)
    {
        if (row is not null)
        {
            _dialogs.RevealInExplorer(row.FullPath);
        }
    }

    /// <summary>Opens the file with its default application.</summary>
    [RelayCommand(CanExecute = nameof(HasRow))]
    private void OpenResult(FileResultViewModel? row)
    {
        if (row is not null)
        {
            _dialogs.OpenFile(row.FullPath);
        }
    }

    // The menu needs these checks: otherwise the item stays enabled and silently
    // does nothing, which is indistinguishable from a broken app.
    private static bool HasRow(FileResultViewModel? row) => row is not null;

    private static bool HasRows(System.Collections.IList? rows) => rows is { Count: > 0 };

    /// <summary>
    /// Copies the paths of the selected rows, one per line. Takes a list because
    /// several rows can be selected.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasRows))]
    private void CopyResultPaths(System.Collections.IList? rows)
    {
        string[] paths = [.. (rows ?? Array.Empty<object>())
            .OfType<FileResultViewModel>()
            .Select(r => r.FullPath)];

        if (paths.Length == 0)
        {
            return;
        }

        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, paths));
        }
        catch (Exception)
        {
            // Clipboard held by another process; not worth crashing over.
        }
    }

    // ── Engine events ────────────────────────────────────────────────────

    private void OnResultsReady(object? sender, IReadOnlyList<FileCheckResult> batch) =>
        Application.Current?.Dispatcher.BeginInvoke(() => Results.AddRange(batch));

    private void OnPlaylistsReady(object? sender, IReadOnlyList<PlaylistCheckResult> playlists) =>
        Application.Current?.Dispatcher.BeginInvoke(() => Results.SetPlaylists(playlists));

    private void OnProgressChanged(object? sender, ScanProgress progress) =>
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            ScanCounters counters = progress.Counters;

            Progress = counters.Progress * 100;
            TotalFiles = CoreFormat.Number(counters.Total);
            CheckedFiles = CoreFormat.Number(counters.Checked);
            OkFiles = CoreFormat.Number(counters.Ok);
            CorruptedFiles = CoreFormat.Number(counters.Corrupted);
            WarningFiles = CoreFormat.Number(counters.Warnings);
            SkippedFiles = CoreFormat.Number(counters.Skipped);

            ProgressText = $"{CoreFormat.Number(counters.Checked)} из {CoreFormat.Number(counters.Total)}";

            CurrentFile = progress.CurrentFile is { } file ? Shorten(file) : string.Empty;
            StatusCounts = $"{CoreFormat.Files(counters.Total)} · {CoreFormat.Number(counters.Checked)} проверено";
        });

    private void OnWarningRaised(object? sender, LiveWarning warning) =>
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            // The live list is capped on purpose; it is for attention during the scan,
            // and the full picture is on the results tab. The counter counts every
            // warning, otherwise the header would read "50" with three hundred findings.
            LiveWarningTotal++;

            if (LiveWarnings.Count >= LiveWarningLimit)
            {
                LiveWarnings.RemoveAt(LiveWarnings.Count - 1);
            }

            LiveWarnings.Insert(0, new LiveWarningRow(warning.Title, warning.Path, warning.Code));
        });

    /// <summary>Inspects finished results: albums per folder and duplicates.</summary>
    /// <remarks>
    /// Done here rather than in the engine: the engine delivers results in
    /// batches and does not keep them, while the results tab already holds the
    /// full list.
    /// </remarks>
    private void UpdateCollectionFindings()
    {
        if (!_settingsService.Current.InspectCollection)
        {
            _findings = [];
            Report.UpdateFindings(_findings);
            return;
        }

        _findings = CollectionInspector.Inspect([.. Results.All.Select(r => r.Result)]);
        Report.UpdateFindings(_findings);
    }

    private void OnFinished(object? sender, ScanSummary summary) =>
        Application.Current?.Dispatcher.BeginInvoke(async () =>
        {
            _lastSummary = summary;
            IsScanning = false;
            IsPaused = false;
            HasLockedQuestion = false;

            Report.Update(summary);
            UpdateCollectionFindings();
            ExportCommand.NotifyCanExecuteChanged();
            StartCommand.NotifyCanExecuteChanged();

            // The window is closing: no summary, no sound, no tab switching. Results are
            // still collected.
            if (_closing)
            {
                return;
            }

            if (summary.CriticalFailure is { } failure)
            {
                await _dialogs.ShowMessageAsync(
                    "Проверка остановлена",
                    failure + " Всё, что успели проверить, сохранено и доступно на вкладке «Результаты».",
                    isError: true);
                return;
            }

            if (_settingsService.Current.SoundOnFinish)
            {
                System.Media.SystemSounds.Asterisk.Play();
            }

            ScanFinishedAction action = await _dialogs.ShowScanFinishedAsync(summary);

            SelectedTab = action switch
            {
                ScanFinishedAction.GoToResults => TabResults,
                ScanFinishedAction.GoToReport => TabReport,
                _ => SelectedTab,
            };

            if (action == ScanFinishedAction.OpenFolder)
            {
                _dialogs.RevealInExplorer(summary.RootPath);
            }
        });

    private async Task ReportInaccessibleAsync(DiscoveryResult result)
    {
        InaccessibleFolder first = result.InaccessibleFolders[0];
        string extra = result.InaccessibleFolders.Count > 1
            ? $" Всего таких папок: {result.InaccessibleFolders.Count}."
            : string.Empty;

        await _dialogs.ShowMessageAsync(
            "Часть папок недоступна",
            $"{first.Reason} Файлы внутри останутся непроверенными.{extra}",
            $"{first.Path}\n{first.TechnicalDetail}",
            isError: false,
            copyPath: first.Path);
    }

    // ── Quick settings ───────────────────────────────────────────────────

    partial void OnRecursiveChanged(bool value) => UpdateSetting(s => s.Recursive = value);

    partial void OnCheckMetadataChanged(bool value) => UpdateSetting(s => s.CheckMetadata = value);

    partial void OnThresholdChanged(SettingsViewModel.ThresholdOption value) =>
        UpdateSetting(s => s.LargeFileThresholdMb = value.Megabytes);

    partial void OnTimeoutChanged(SettingsViewModel.TimeoutOption value) =>
        UpdateSetting(s => s.FileTimeoutSeconds = value.Seconds);

    partial void OnIsPausedChanged(bool value)
    {
        OnPropertyChanged(nameof(PauseButtonText));
        OnPropertyChanged(nameof(PauseButtonIcon));
        OnPropertyChanged(nameof(TaskbarState));
    }

    partial void OnIsScanningChanged(bool value)
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(TaskbarState));
        StartCommand.NotifyCanExecuteChanged();
        RescanCommand.NotifyCanExecuteChanged();
        TogglePauseCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
    }

    partial void OnFolderPathChanged(string value) => RescanCommand.NotifyCanExecuteChanged();

    partial void OnHasLockedQuestionChanged(bool value) => OnPropertyChanged(nameof(TaskbarState));

    private bool _syncingSettings;

    private void UpdateSetting(Action<AppSettings> change)
    {
        if (_syncingSettings)
        {
            return;
        }

        AppSettings settings = _settingsService.Current;
        change(settings);
        _ = _settingsService.SaveAsync();
    }

    private void OnSettingsChanged(object? sender, AppSettings settings) =>
        Application.Current?.Dispatcher.BeginInvoke(SyncQuickSettings);

    private void SyncQuickSettings()
    {
        AppSettings settings = _settingsService.Current;
        _syncingSettings = true;

        try
        {
            Recursive = settings.Recursive;
            CheckMetadata = settings.CheckMetadata;
            Threshold = SettingsViewModel.ThresholdOptions
                .FirstOrDefault(o => o.Megabytes == settings.LargeFileThresholdMb) ?? SettingsViewModel.ThresholdOptions[1];
            Timeout = SettingsViewModel.TimeoutOptions
                .FirstOrDefault(o => o.Seconds == settings.FileTimeoutSeconds) ?? SettingsViewModel.TimeoutOptions[2];

            // Report format and folder live in settings; the tab only displays them and
            // the user chooses in the save dialog. The default format setting used to
            // have no effect at all.
            Report.SelectedFormat = settings.DefaultReportFormat;
            Report.TargetFolder = _reports.ResolveDefaultFolder(settings);
        }
        finally
        {
            _syncingSettings = false;
        }

    }

    /// <summary>Shortens a path for single-line display under the progress bar.</summary>
    private static string Shorten(string path)
    {
        const int maxLength = 90;
        if (path.Length <= maxLength)
        {
            return path;
        }

        return "…" + path[^(maxLength - 1)..];
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _engine.ResultsReady -= OnResultsReady;
        _engine.PlaylistsReady -= OnPlaylistsReady;
        _engine.ProgressChanged -= OnProgressChanged;
        _engine.WarningRaised -= OnWarningRaised;
        _engine.Finished -= OnFinished;
        _settingsService.Changed -= OnSettingsChanged;

        _discoveryCts?.Dispose();
    }
}

/// <summary>A row in the live warning list.</summary>
/// <param name="Title">Human wording.</param>
public sealed record LiveWarningRow(string Title, string Path, IssueCode Code)
{
    /// <summary>
    /// Caption of the row action. A large file is informational with nothing to
    /// look at, so the row is dismissed; other findings belong to a file that can
    /// be opened on the results tab.
    /// </summary>
    public string ActionLabel => Code is IssueCode.LargeFile ? "Понятно" : "Показать";

    /// <summary>Whether the action dismisses the row.</summary>
    public bool ActionDismisses => Code is IssueCode.LargeFile;
}
