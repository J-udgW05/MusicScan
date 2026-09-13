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

/// <summary>Главное окно: панель инструментов, вкладки, ход проверки, статусная строка.</summary>
public sealed partial class MainViewModel : ObservableObject, ILockedFileDecisionProvider, IDisposable
{
    private const int TabScan = 0;
    private const int TabResults = 1;
    private const int TabReport = 2;

    /// <summary>Сколько предупреждений держать в живом списке на вкладке «Проверка».</summary>
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

    /// <summary>Замечания по коллекции с последней проверки.</summary>
    private IReadOnlyList<CollectionFinding> _findings = [];
    private CancellationTokenSource? _discoveryCts;
    private TaskCompletionSource<LockedFileDecision>? _pendingAnswer;

    [ObservableProperty]
    private int _selectedTab;

    /// <summary>
    /// Открыт экран настроек. Он лежит поверх содержимого вкладок в том же
    /// окне: настройки — не этап работы, но и не повод уводить пользователя
    /// в отдельное окно, которое потом надо искать на панели задач.
    /// </summary>
    [ObservableProperty]
    private bool _isSettingsOpen;

    /// <summary>Над окном тащат папку — зона перетаскивания подсвечивается.</summary>
    [ObservableProperty]
    private bool _isDragActive;

    // Щелчок по вкладке уводит к работе — экран настроек при этом закрывается.
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

    // Счётчики сводки на вкладке «Проверка».
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

    // Быстрые параметры в боковой колонке.
    [ObservableProperty]
    private bool _recursive = true;

    [ObservableProperty]
    private bool _checkMetadata;

    [ObservableProperty]
    private SettingsViewModel.ThresholdOption _threshold = SettingsViewModel.ThresholdOptions[1];

    [ObservableProperty]
    private SettingsViewModel.TimeoutOption _timeout = SettingsViewModel.TimeoutOptions[2];

    // Карточка вопроса о занятом файле.
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

    /// <summary>Сколько предупреждений было всего, а не сколько влезло в список.</summary>
    [ObservableProperty]
    private int _liveWarningTotal;

    /// <summary>Подпись под списком, когда показаны не все предупреждения.</summary>
    public string LiveWarningNote => LiveWarningTotal > LiveWarnings.Count
        ? $"Показаны последние {LiveWarnings.Count} — полный список на вкладке «Результаты»"
        : string.Empty;

    partial void OnLiveWarningTotalChanged(int value) => OnPropertyChanged(nameof(LiveWarningNote));

    // Пояснения к вариантам решения: каждое считается по текущим настройкам
    // и по самому файлу, чтобы цифры в них были настоящими, а не примерными.
    [ObservableProperty]
    private string _lockedWaitNote = string.Empty;

    [ObservableProperty]
    private string _lockedCopyNote = string.Empty;

    [ObservableProperty]
    private string _lockedOwnerNote = string.Empty;

    /// <summary>Создаёт модель главного окна.</summary>
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

        // Движок спрашивает про занятые файлы через переходник — подставляем себя.
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

    /// <summary>Вкладка «Результаты».</summary>
    public ResultsViewModel Results { get; }

    /// <summary>Вкладка «Отчёт».</summary>
    public ReportViewModel Report { get; }

    /// <summary>Вкладка «Настройки».</summary>
    public SettingsViewModel Settings { get; }

    /// <summary>Предупреждения, появляющиеся прямо по ходу проверки.</summary>
    public ObservableCollection<LiveWarningRow> LiveWarnings { get; } = [];

    /// <summary>Подпись кнопки паузы меняется на «Продолжить», когда проверка стоит.</summary>
    public string PauseButtonText => IsPaused ? "Продолжить" : "Пауза";

    /// <summary>Значок кнопки паузы.</summary>
    public string PauseButtonIcon => IsPaused ? "play" : "pause";

    /// <summary>Идёт ли проверка — от этого зависит подтверждение при закрытии окна.</summary>
    public bool IsBusy => IsScanning;

    /// <summary>Доля выполнения для панели задач: 0…1.</summary>
    public double TaskbarProgress => Math.Clamp(Progress / 100.0, 0, 1);

    /// <summary>
    /// Состояние индикатора в панели задач. Проверка идёт минутами, окно при
    /// этом обычно свёрнуто, поэтому ход виден прямо на кнопке в панели:
    /// зелёная полоса — идёт, жёлтая — пауза или ждём ответа про занятый файл.
    /// </summary>
    public TaskbarItemProgressState TaskbarState => !IsScanning
        ? TaskbarItemProgressState.None
        : IsPaused || HasLockedQuestion
            ? TaskbarItemProgressState.Paused
            : TaskbarItemProgressState.Normal;

    partial void OnProgressChanged(double value) => OnPropertyChanged(nameof(TaskbarProgress));



    // ── Выбор папки ──────────────────────────────────────────────────────

    /// <summary>Открывает диалог выбора папки.</summary>
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
    /// Принимает папку (из диалога или перетаскиванием), сразу считает файлы
    /// и предлагает начать проверку (02_ARCHITECTURE.md, раздел 9).
    /// </summary>
    public async Task SetFolderAsync(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        FolderPath = folder;
        IsOfferVisible = false;

        // Папку выбирают, чтобы её проверить. Если это сделали с «Результатов»
        // или «Отчёта», кнопка «Начать» осталась бы на другой вкладке.
        SelectedTab = TabScan;

        // И отдельно — закрыть настройки. Одной строки выше для этого мало:
        // экран настроек закрывается по смене вкладки, а вкладка «Проверка»
        // обычно уже выбрана, значение не меняется, и событие не приходит.
        // Папку тогда выбирали из настроек и в них же и оставались.
        IsSettingsOpen = false;

        AppSettings settings = _settingsService.Current;

        if (settings.RememberLastFolder)
        {
            settings.LastFolder = folder;
            await _settingsService.SaveAsync();
        }

        // Предыдущий подсчёт мог ещё идти — прерываем его, прежде чем начать новый.
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

            // Про «заглянуть в настройки» здесь больше не говорится: кнопки
            // настроек рядом с «Начать» нет, а параметры проверки — справа.
            OfferSubtitle = result.Playlists.Count > 0
                ? $"Ещё {CoreFormat.Number(result.Playlists.Count)} " +
                  $"{CoreFormat.Plural(result.Playlists.Count, "плейлист", "плейлиста", "плейлистов")}. " +
                  "Можно начинать — параметры проверки справа."
                : "Можно начинать — параметры проверки справа.";

            IsOfferVisible = settings.OfferStartAfterFolderSelected && result.AudioItems.Count > 0;

            // Без этого кнопка «Начать» осталась бы неактивной: RelayCommand
            // кэширует результат CanExecute и сам о новой папке не узнает.
            StartCommand.NotifyCanExecuteChanged();

            if (result.InaccessibleFolders.Count > 0)
            {
                await ReportInaccessibleAsync(result);
            }
        }
        catch (OperationCanceledException)
        {
            // Пользователь выбрал другую папку, пока считали эту.
        }
        catch (Exception ex)
        {
            _lastDiscovery = null;
            StartCommand.NotifyCanExecuteChanged();

            // Макет на такой случай даёт три действия: скопировать путь,
            // повторить и закрыть. Повтор здесь осмыслен — папка могла быть
            // временно занята или не примонтирована.
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

    // ── Управление проверкой ─────────────────────────────────────────────

    /// <summary>Запускает проверку.</summary>
    /// <summary>
    /// Приводит базу истории в соответствие с настройкой перед проверкой.
    /// </summary>
    /// <remarks>
    /// Открывается здесь, а не при запуске программы: если слежение выключено,
    /// база не нужна и создавать её незачем.
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

    /// <summary>Ставит проверку на паузу или продолжает её.</summary>
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
    /// Пересчитывает выбранную папку и сразу начинает проверку заново.
    /// Нужна, когда содержимое папки поменялось после прошлой проверки:
    /// выбирать тот же путь второй раз незачем.
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
    /// Останавливает проверку без вопросов. Нужен закрытию окна: там про
    /// остановку уже спросили, и второй такой же вопрос подряд — издевательство.
    /// </summary>
    public void StopImmediately()
    {
        // Программу закрывают: итоги показывать некому и незачем. Без этого
        // флага поверх закрывающегося окна успевало всплыть окно «Проверка
        // остановлена», и закрытие упиралось в него.
        _closing = true;
        _engine.Stop();
    }

    private bool _closing;

    /// <summary>Останавливает проверку, спросив подтверждение.</summary>
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

    // ── Вопрос о занятом файле ───────────────────────────────────────────

    /// <summary>
    /// Показывает вопрос о занятом файле карточкой на вкладке «Проверка».
    /// </summary>
    /// <remarks>
    /// Решение по неоднозначности: макет показывает вопрос и карточкой в боковой
    /// колонке, и отдельным окном. Выбрана карточка — она не перекрывает окно и
    /// не мешает смотреть на ход проверки, а очередь вопросов уже гарантирует,
    /// что вопрос показывается ровно один (03_IMPLEMENTATION_GUIDE.md, раздел 2).
    /// Если пользователь смотрит другую вкладку, программа переключает его на
    /// «Проверку», чтобы вопрос не остался незамеченным.
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

            // Именно DisplayName: у записи LockOwner своего ToString() нет,
            // и в подпись вываливалась вся структура целиком —
            // «LockOwner { ProcessId = 11444, ProcessName = pwsh, … }».
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

    /// <summary>Отвечает на вопрос о занятом файле.</summary>
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

    /// <summary>Останавливает проверку прямо из вопроса о занятом файле.</summary>
    [RelayCommand]
    private void StopFromLocked()
    {
        HasLockedQuestion = false;
        _pendingAnswer?.TrySetResult(new LockedFileDecision(LockedFileAction.Skip, StopScan: true));
        _pendingAnswer = null;
    }

    // ── Отчёт ────────────────────────────────────────────────────────────

    /// <summary>Сохраняет отчёт в выбранном формате.</summary>
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
            // Ошибки асинхронной команды иначе просто теряются: библиотека MVVM
            // складывает их в задачу и никому не показывает. Молчаливый отказ
            // экспорта — худшее, что здесь может быть.
            await _dialogs.ShowMessageAsync(
                "Отчёт не сохранён",
                "Сохранить отчёт не получилось. Проверьте, что папка доступна на запись и на диске есть место.",
                $"{ex.GetType().Name} · {ex.Message}",
                isError: true);
        }
    }

    /// <summary>
    /// Запоминает папку, в которую человек сохранил отчёт.
    /// </summary>
    /// <param name="filePath">Путь сохранённого отчёта.</param>
    /// <remarks>
    /// <para>
    /// Записывается через вью-модель настроек, а не прямо в настройки. У неё
    /// свой черновик, и правка любого переключателя переписывает настройки
    /// целиком из него — значение, положенное в обход, вернулось бы к старому.
    /// </para>
    /// <para>
    /// Только после удачного сохранения: путь, по которому записать не вышло,
    /// запоминать незачем — он будет подставляться и мешать каждый раз.
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

    // ── Панель инструментов ──────────────────────────────────────────────

    /// <summary>Открывает вкладку настроек.</summary>
    [RelayCommand]
    private void OpenSettings() => IsSettingsOpen = true;

    /// <summary>Возвращает из настроек к работе.</summary>
    [RelayCommand]
    private void CloseSettings() => IsSettingsOpen = false;

    /// <summary>
    /// Действие по строке живого предупреждения: либо убрать её из списка,
    /// либо перейти к этому файлу на вкладке «Результаты».
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

    /// <summary>Показывает справку.</summary>
    [RelayCommand]
    private void ShowHelp() => _dialogs.ShowHelp();

    /// <summary>Показывает окно «О программе».</summary>
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

    /// <summary>Открывает папку файла из строки результатов.</summary>
    [RelayCommand(CanExecute = nameof(HasRow))]
    private void RevealResult(FileResultViewModel? row)
    {
        if (row is not null)
        {
            _dialogs.RevealInExplorer(row.FullPath);
        }
    }

    /// <summary>Открывает файл программой по умолчанию.</summary>
    [RelayCommand(CanExecute = nameof(HasRow))]
    private void OpenResult(FileResultViewModel? row)
    {
        if (row is not null)
        {
            _dialogs.OpenFile(row.FullPath);
        }
    }

    // Проверки нужны меню: без них пункт остаётся включённым и по нажатию
    // молча ничего не делает — а это неотличимо от сломанной программы.
    private static bool HasRow(FileResultViewModel? row) => row is not null;

    private static bool HasRows(System.Collections.IList? rows) => rows is { Count: > 0 };

    /// <summary>
    /// Копирует пути выделенных строк — по одному на строку.
    /// Принимает список, а не одну строку: выделить можно несколько файлов,
    /// и переписывать их пути руками из таблицы было бы издевательством.
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
            // Буфер обмена занят другой программой — не повод падать.
        }
    }

    // ── Реакция на события движка ────────────────────────────────────────

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
            // Список живых предупреждений намеренно ограничен: он для внимания
            // по ходу проверки, а полный разбор — на вкладке «Результаты».
            // Счётчик считает все предупреждения, а список хранит только
            // последние: иначе в шапке стояло бы «50» при трёх сотнях находок.
            LiveWarningTotal++;

            if (LiveWarnings.Count >= LiveWarningLimit)
            {
                LiveWarnings.RemoveAt(LiveWarnings.Count - 1);
            }

            LiveWarnings.Insert(0, new LiveWarningRow(warning.Title, warning.Path, warning.Code));
        });

    /// <summary>
    /// Разбирает готовые результаты: альбомы по папкам и повторы по коллекции.
    /// </summary>
    /// <remarks>
    /// Считается здесь, а не в движке: движок отдаёт результаты порциями и
    /// целиком их не хранит, а список готовых результатов и так лежит на
    /// вкладке «Результаты».
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

            // Окно уже закрывается — ни итогов, ни звука, ни переходов
            // по вкладкам. Результаты при этом собраны и сохранены.
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

    // ── Быстрые параметры ────────────────────────────────────────────────

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

            // Формат и папка отчёта живут в настройках; вкладка их только
            // показывает, а выбирает пользователь в окне сохранения. Раньше
            // «Формат отчёта по умолчанию» из настроек ни на что не влиял.
            Report.SelectedFormat = settings.DefaultReportFormat;
            Report.TargetFolder = _reports.ResolveDefaultFolder(settings);
        }
        finally
        {
            _syncingSettings = false;
        }

    }

    /// <summary>Сокращает путь для однострочного показа под прогрессом.</summary>
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

/// <summary>Строка живого списка предупреждений.</summary>
/// <param name="Title">Человеческая формулировка.</param>
/// <param name="Path">Путь к файлу.</param>
/// <param name="Code">Код замечания.</param>
public sealed record LiveWarningRow(string Title, string Path, IssueCode Code)
{
    /// <summary>
    /// Подпись действия справа в строке. Большой файл — это предупреждение
    /// «к сведению», по нему нечего смотреть, поэтому строку просто убирают.
    /// Остальные замечания привязаны к конкретному файлу, и его можно открыть
    /// в «Результатах».
    /// </summary>
    public string ActionLabel => Code is IssueCode.LargeFile ? "Понятно" : "Показать";

    /// <summary>Убирает ли действие строку из списка.</summary>
    public bool ActionDismisses => Code is IssueCode.LargeFile;
}
