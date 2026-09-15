using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicScanIntegrity.App.Services;
using MusicScanIntegrity.Core.Discovery;
using MusicScanIntegrity.Core.History;
using MusicScanIntegrity.Core.Models;
using MusicScanIntegrity.Core.Settings;

namespace MusicScanIntegrity.App.ViewModels;

/// <summary>
/// Настройки. Модель правит копию настроек и применяет её целиком —
/// так «Отмена» действительно отменяет, а не откатывает по одному полю.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    /// <summary>Готовые значения порога «большого файла» (UI_SPEC.md, раздел 7).</summary>
    public static readonly IReadOnlyList<ThresholdOption> ThresholdOptions =
    [
        new(100, "100 МБ"),
        new(500, "500 МБ"),
        new(1024, "1 ГБ"),
        new(2048, "2 ГБ"),
        new(5120, "5 ГБ"),
        new(0, "Без ограничений"),
    ];

    /// <summary>Готовые значения таймаута проверки одного файла.</summary>
    public static readonly IReadOnlyList<TimeoutOption> TimeoutOptions =
    [
        new(15, "15 с"),
        new(30, "30 с"),
        new(60, "60 с"),
        new(120, "2 мин"),
        new(300, "5 мин"),
    ];

    private readonly ISettingsService _settingsService;
    private readonly IThemeService _themeService;
    private readonly IDialogService _dialogService;
    private readonly IScanHistory _history;

    private AppSettings _draft;

    /// <summary>
    /// Идёт заполнение полей из настроек, а не правка пользователем.
    /// Без этого флага загрузка сама себя тут же и сохраняла бы.
    /// </summary>
    private bool _loading;

    /// <summary>Создаёт модель настроек.</summary>
    public SettingsViewModel(
        ISettingsService settingsService,
        IThemeService themeService,
        IDialogService dialogService,
        IScanHistory history)
    {
        _settingsService = settingsService;
        _themeService = themeService;
        _dialogService = dialogService;
        _history = history;
        _draft = settingsService.Current.Clone();

        // Плейлисты включаются отдельным переключателем, поэтому в чипы идут
        // только звуковые расширения — их и можно отключать поштучно.
        Formats = [.. AudioFormats.AllAudio
            .Select(e => e.TrimStart('.').ToUpperInvariant())
            .Distinct()
            .OrderBy(e => e, StringComparer.OrdinalIgnoreCase)
            .Select(e => new FormatChip(e, OnFormatToggled))];

        StatusColors =
        [
            new StatusColorRow(CheckStatus.Ok, "В порядке"),
            new StatusColorRow(CheckStatus.Corrupted, "Повреждён"),
            new StatusColorRow(CheckStatus.Warning, "Предупреждение"),
            new StatusColorRow(CheckStatus.Skipped, "Пропущен"),
        ];

        Reload();

        // Те же параметры — «Заходить в подпапки», «Проверять теги», порог
        // большого файла и таймаут — правятся ещё и быстрой панелью на экране
        // проверки. Без подписки этот экран показывал бы снимок, снятый при
        // запуске, а его применение затирало бы сделанное в панели: ApplyAsync
        // выкладывает в настройки все свои поля разом, включая устаревшие.
        _settingsService.Changed += OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        // Собственное сохранение возвращается сюда же этим событием. Перечитывать
        // нечего: значения уже наши, а Reload посреди применения сбил бы правку,
        // которую пользователь делает прямо сейчас.
        if (_applying)
        {
            return;
        }

        // Настройки могут прийти из фонового потока, а привязки живут в потоке
        // интерфейса. Если мы уже в нём — перечитываем сразу, без лишнего круга
        // через очередь: иначе значение успевает мелькнуть старым.
        Dispatcher dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        if (dispatcher.CheckAccess())
        {
            Reload();
        }
        else
        {
            dispatcher.BeginInvoke(Reload);
        }
    }

    private bool _applying;

    /// <summary>Список поддерживаемых форматов — чипы в разделе «Форматы».</summary>
    /// <remarks>
    /// Чип переключается: выключенное расширение попадает в
    /// <see cref="AppSettings.DisabledExtensions"/> и в обход не идёт.
    /// Раньше это был просто список подписей, и настройка была недостижима.
    /// </remarks>
    public ObservableCollection<FormatChip> Formats { get; }

    /// <summary>Сводка по чипам: «12 из 14».</summary>
    public string FormatsSummary =>
        $"{Formats.Count(f => f.Enabled)} из {Formats.Count}";

    private void OnFormatToggled()
    {
        OnPropertyChanged(nameof(FormatsSummary));

        _draft.DisabledExtensions = [.. Formats
            .Where(f => !f.Enabled)
            .Select(f => "." + f.Extension.ToLowerInvariant())];

        _ = ApplyAsync();
    }

    /// <summary>Цвета статусов, показанные в разделе «Внешний вид».</summary>
    public ObservableCollection<StatusColorRow> StatusColors { get; }

    /// <summary>Настройки изменены и применены.</summary>
    public event EventHandler? Applied;

    // ── Внешний вид ──────────────────────────────────────────────────────

    /// <summary>Тема оформления.</summary>
    [ObservableProperty]
    private AppTheme _theme;

    /// <summary>Плотность строк таблицы.</summary>
    [ObservableProperty]
    private ListDensity _listDensity;

    /// <summary>Высота строки таблицы результатов для выбранной плотности.</summary>
    /// <remarks>
    /// Настройка обязана что-то менять: значение, которое сохраняется и ни на
    /// что не влияет, хуже отсутствующей настройки. Отступ строки берётся
    /// прямо отсюда — «Результаты» привязаны к этому свойству.
    /// </remarks>
    public Thickness RowPadding => ListDensity == ListDensity.Compact
        ? new Thickness(16, 7, 16, 7)
        : new Thickness(16, 11, 16, 11);

    partial void OnListDensityChanged(ListDensity value) => OnPropertyChanged(nameof(RowPadding));

    /// <summary>Показывать полные пути.</summary>
    [ObservableProperty]
    private bool _showFullPaths;

    /// <summary>Моноширинный шрифт для путей.</summary>
    [ObservableProperty]
    private bool _monospacePaths;

    /// <summary>Показывать строку состояния внизу окна.</summary>
    [ObservableProperty]
    private bool _showStatusBar;

    /// <summary>Подложка Mica под окном.</summary>
    [ObservableProperty]
    private bool _micaEffect;

    /// <summary>Анимации.</summary>
    [ObservableProperty]
    private bool _animations;

    /// <summary>Система умеет рисовать подложку Mica.</summary>
    /// <remarks>
    /// Переключатель без этого был бы обманом: на Windows 10 его можно было бы
    /// включить, и ничего бы не произошло.
    /// </remarks>
    public bool IsMicaSupported => _themeService.IsMicaSupported;

    /// <summary>
    /// Пояснение под переключателем: показывается, только когда эффект
    /// недоступен и надо объяснить погасший тумблер.
    /// </summary>
    public string MicaNote => IsMicaSupported
        ? string.Empty
        : "недоступно: подложку умеет рисовать только Windows 11";

    // ── Проверка ─────────────────────────────────────────────────────────

    /// <summary>Рекурсивный обход подпапок.</summary>
    [ObservableProperty]
    private bool _recursive;

    /// <summary>Проверка метаданных.</summary>
    [ObservableProperty]
    private bool _checkMetadata;

    /// <summary>Проверка плейлистов.</summary>
    [ObservableProperty]
    private bool _checkPlaylists;

    /// <summary>Насколько глубоко читать файл декодером.</summary>
    [ObservableProperty]
    private CheckDepth _checkDepth;

    /// <summary>Учитывать тип диска при выборе числа потоков.</summary>
    [ObservableProperty]
    private bool _respectDriveType;

    /// <summary>Следить за порчей: хранить отпечатки файлов.</summary>
    [ObservableProperty]
    private bool _trackChanges;

    /// <summary>Сколько файлов помнит история.</summary>
    [ObservableProperty]
    private string _historyNote = string.Empty;

    /// <summary>Разбирать альбомы и искать повторы.</summary>
    [ObservableProperty]
    private bool _inspectCollection;

    /// <summary>Замечать тишину и провалы внутри трека.</summary>
    [ObservableProperty]
    private bool _detectSilence;

    /// <summary>Замечать перегрузку и смещение нуля.</summary>
    [ObservableProperty]
    private bool _detectClipping;

    /// <summary>Искать признаки перекодирования по срезанному спектру.</summary>
    [ObservableProperty]
    private bool _detectTranscode;

    /// <summary>Сверять расширение с содержимым.</summary>
    [ObservableProperty]
    private bool _verifyExtension;

    /// <summary>Проверять контрольные суммы формата и целостность контейнера.</summary>
    [ObservableProperty]
    private bool _verifyContainerIntegrity;

    /// <summary>Порог «большого файла».</summary>
    [ObservableProperty]
    private ThresholdOption _threshold = ThresholdOptions[1];

    /// <summary>Таймаут проверки файла.</summary>
    [ObservableProperty]
    private TimeoutOption _timeout = TimeoutOptions[2];

    /// <summary>Параллельность выбирается автоматически.</summary>
    [ObservableProperty]
    private bool _autoParallelism;

    /// <summary>Параллельность, заданная вручную.</summary>
    [ObservableProperty]
    private int _manualParallelism;

    /// <summary>Предупреждать о больших файлах.</summary>
    [ObservableProperty]
    private bool _warnAboutLargeFiles;

    // ── Занятые файлы ────────────────────────────────────────────────────

    /// <summary>Что делать с занятым файлом.</summary>
    [ObservableProperty]
    private LockedFileAction _lockedFileAction;

    /// <summary>Сколько ждать освобождения.</summary>
    [ObservableProperty]
    private int _lockedWaitSeconds;

    /// <summary>Сколько раз повторять попытку.</summary>
    [ObservableProperty]
    private int _lockedRetryCount;

    /// <summary>Определять программу-владельца.</summary>
    [ObservableProperty]
    private bool _detectOwnerProcess;

    /// <summary>Показывать в вопросе флажок «поступать так же со всеми».</summary>
    [ObservableProperty]
    private bool _allowApplyToAllLocked;

    /// <summary>Предлагать закрыть владельца.</summary>
    [ObservableProperty]
    private bool _offerCloseOwner;

    // ── Форматы ──────────────────────────────────────────────────────────

    /// <summary>Экспериментальная поддержка ISO (SACD).</summary>
    [ObservableProperty]
    private bool _enableIsoSacd;

    /// <summary>Пользовательские расширения через запятую.</summary>
    [ObservableProperty]
    private string _customExtensions = string.Empty;

    // ── Общие ────────────────────────────────────────────────────────────

    /// <summary>Запоминать последнюю папку.</summary>
    [ObservableProperty]
    private bool _rememberLastFolder;

    /// <summary>Предлагать старт после выбора папки.</summary>
    [ObservableProperty]
    private bool _offerStartAfterFolderSelected;

    /// <summary>Показывать приветствие при первом запуске.</summary>
    [ObservableProperty]
    private bool _showFirstRunTip;

    /// <summary>Звук по завершении проверки.</summary>
    [ObservableProperty]
    private bool _soundOnFinish;

    // ── Отчёты ───────────────────────────────────────────────────────────

    /// <summary>Формат отчёта по умолчанию.</summary>
    [ObservableProperty]
    private ReportFormat _defaultReportFormat;

    /// <summary>Папка для отчётов.</summary>
    [ObservableProperty]
    private string _reportsFolder = string.Empty;

    /// <summary>Включать в отчёт файлы «в порядке».</summary>
    [ObservableProperty]
    private bool _includeOkFilesInReport;

    /// <summary>Открывать отчёт после сохранения.</summary>
    [ObservableProperty]
    private bool _openReportAfterSave;


    /// <summary>Путь к файлу настроек — показывается пользователю.</summary>
    public string SettingsFilePath => _settingsService.SettingsFilePath;

    /// <summary>Читает значения из текущих настроек.</summary>
    public void Reload()
    {
        _loading = true;
        try
        {
            ReloadCore();
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// Любая правка применяется сразу, без кнопки «Применить».
    /// </summary>
    /// <remarks>
    /// Так устроены настройки самой Windows 11, и так уже вели себя быстрые
    /// параметры на вкладке «Проверка» — держать рядом два разных поведения
    /// значило бы путать пользователя. Отдельная кнопка ещё и требовала бы
    /// помнить о ней: закрыл вкладку, не нажав, — правки потеряны.
    /// </remarks>
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (!_loading)
        {
            _ = ApplyAsync();
        }
    }

    private void ReloadCore()
    {
        _draft = _settingsService.Current.Clone();

        Theme = _draft.Theme;
        ListDensity = _draft.ListDensity;
        ShowFullPaths = _draft.ShowFullPaths;
        MonospacePaths = _draft.MonospacePaths;
        ShowStatusBar = _draft.ShowStatusBar;

        // К этому месту пустых значений уже нет: их разрешили при запуске.
        // Но подстраховка ничего не стоит, а окно настроек не должно зависеть
        // от того, кто и в каком порядке вызывался до него.
        MicaEffect = _draft.MicaEffect ?? false;
        Animations = _draft.Animations ?? true;

        Recursive = _draft.Recursive;
        CheckMetadata = _draft.CheckMetadata;
        CheckPlaylists = _draft.CheckPlaylists;
        CheckDepth = _draft.CheckDepth;
        InspectCollection = _draft.InspectCollection;
        RespectDriveType = _draft.RespectDriveType;
        TrackChanges = _draft.TrackChanges;
        RefreshHistoryNote();
        DetectSilence = _draft.DetectSilence;
        DetectTranscode = _draft.DetectTranscode;
        DetectClipping = _draft.DetectClipping;
        VerifyExtension = _draft.VerifyExtensionMatchesContent;
        VerifyContainerIntegrity = _draft.VerifyContainerIntegrity;
        Threshold = ThresholdOptions.FirstOrDefault(o => o.Megabytes == _draft.LargeFileThresholdMb) ?? ThresholdOptions[1];
        Timeout = TimeoutOptions.FirstOrDefault(o => o.Seconds == _draft.FileTimeoutSeconds) ?? TimeoutOptions[2];
        AutoParallelism = _draft.AutoParallelism;
        ManualParallelism = _draft.ManualParallelism;
        WarnAboutLargeFiles = _draft.WarnAboutLargeFiles;

        LockedFileAction = _draft.LockedFileAction;
        LockedWaitSeconds = _draft.LockedWaitSeconds;
        LockedRetryCount = _draft.LockedRetryCount;
        DetectOwnerProcess = _draft.DetectOwnerProcess;
        OfferCloseOwner = _draft.OfferCloseOwner;
        AllowApplyToAllLocked = _draft.AllowApplyToAllLocked;

        EnableIsoSacd = _draft.EnableIsoSacd;

        HashSet<string> disabled = new(_draft.DisabledExtensions, StringComparer.OrdinalIgnoreCase);
        foreach (FormatChip chip in Formats)
        {
            chip.SetEnabledQuietly(!disabled.Contains("." + chip.Extension));
        }

        OnPropertyChanged(nameof(FormatsSummary));
        CustomExtensions = string.Join(", ", _draft.CustomExtensions);

        RememberLastFolder = _draft.RememberLastFolder;
        OfferStartAfterFolderSelected = _draft.OfferStartAfterFolderSelected;
        ShowFirstRunTip = _draft.ShowFirstRunTip;
        SoundOnFinish = _draft.SoundOnFinish;

        DefaultReportFormat = _draft.DefaultReportFormat;
        ReportsFolder = _draft.ReportsFolder ?? string.Empty;
        IncludeOkFilesInReport = _draft.IncludeOkFilesInReport;
        OpenReportAfterSave = _draft.OpenReportAfterSave;


        RefreshStatusColors();
    }

    /// <summary>Собирает изменённые настройки и сохраняет их.</summary>
    public async Task ApplyAsync()
    {
        _applying = true;

        AppSettings settings = _settingsService.Current.Clone();

        settings.Theme = Theme;
        settings.ListDensity = ListDensity;
        settings.ShowFullPaths = ShowFullPaths;
        settings.MonospacePaths = MonospacePaths;
        settings.ShowStatusBar = ShowStatusBar;
        settings.MicaEffect = MicaEffect;
        settings.Animations = Animations;

        settings.Recursive = Recursive;
        settings.CheckMetadata = CheckMetadata;
        settings.CheckPlaylists = CheckPlaylists;
        settings.CheckDepth = CheckDepth;
        settings.InspectCollection = InspectCollection;
        settings.RespectDriveType = RespectDriveType;
        settings.TrackChanges = TrackChanges;
        settings.DetectSilence = DetectSilence;
        settings.DetectTranscode = DetectTranscode;
        settings.DetectClipping = DetectClipping;
        settings.VerifyExtensionMatchesContent = VerifyExtension;
        settings.VerifyContainerIntegrity = VerifyContainerIntegrity;
        settings.LargeFileThresholdMb = Threshold.Megabytes;
        settings.FileTimeoutSeconds = Timeout.Seconds;
        settings.AutoParallelism = AutoParallelism;
        settings.ManualParallelism = ManualParallelism;
        settings.WarnAboutLargeFiles = WarnAboutLargeFiles;

        settings.LockedFileAction = LockedFileAction;
        settings.LockedWaitSeconds = LockedWaitSeconds;
        settings.LockedRetryCount = LockedRetryCount;
        settings.DetectOwnerProcess = DetectOwnerProcess;
        settings.OfferCloseOwner = OfferCloseOwner;
        settings.AllowApplyToAllLocked = AllowApplyToAllLocked;

        settings.EnableIsoSacd = EnableIsoSacd;
        settings.DisabledExtensions = [.. _draft.DisabledExtensions];
        settings.CustomExtensions = [.. CustomExtensions
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

        settings.RememberLastFolder = RememberLastFolder;
        settings.OfferStartAfterFolderSelected = OfferStartAfterFolderSelected;
        settings.ShowFirstRunTip = ShowFirstRunTip;
        settings.SoundOnFinish = SoundOnFinish;

        settings.DefaultReportFormat = DefaultReportFormat;
        settings.ReportsFolder = string.IsNullOrWhiteSpace(ReportsFolder) ? null : ReportsFolder;
        settings.IncludeOkFilesInReport = IncludeOkFilesInReport;
        settings.OpenReportAfterSave = OpenReportAfterSave;

        // Цвета статусов правятся отдельно и уже лежат в StatusColors.
        settings.StatusColors = new StatusColorOverrides
        {
            Ok = StatusColors[0].Override,
            Corrupted = StatusColors[1].Override,
            Warning = StatusColors[2].Override,
            Skipped = StatusColors[3].Override,
        };

        try
        {
            await _settingsService.ApplyAsync(settings);
        }
        finally
        {
            _applying = false;
        }

        _themeService.Apply(settings);

        _loading = true;
        try
        {
            // Цвета статусов по умолчанию зависят от темы: после её смены
            // подписи в списке обновляются, но правкой это не считается.
            RefreshStatusColors();
        }
        finally
        {
            _loading = false;
        }

        Applied?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Сбрасывает все настройки к значениям по умолчанию.</summary>
    [RelayCommand]
    private async Task ResetAllAsync()
    {
        bool confirmed = await _dialogService.ConfirmAsync(
            "Сбросить все настройки?",
            "Все параметры вернутся к значениям по умолчанию: рекурсия включена, проверка тегов выключена, " +
            "порог большого файла 500 МБ, таймаут 60 секунд, тема — как в системе.",
            "Сбросить",
            "Отмена",
            destructive: true);

        if (!confirmed)
        {
            return;
        }

        await _settingsService.ResetAsync();
        _themeService.Apply(_settingsService.Current);
        Reload();
        Applied?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Выбирает папку для отчётов.</summary>
    [RelayCommand]
    private void PickReportsFolder()
    {
        string? folder = _dialogService.PickFolder(
            "Куда сохранять отчёты",
            string.IsNullOrWhiteSpace(ReportsFolder) ? null : ReportsFolder);

        if (folder is not null)
        {
            ReportsFolder = folder;
        }
    }

    partial void OnTrackChangesChanged(bool value)
    {
        if (value)
        {
            EnsureHistoryOpen();
        }
        else
        {
            _history.Close();
        }

        RefreshHistoryNote();
    }

    /// <summary>Стирает историю проверок.</summary>
    /// <remarks>
    /// Спрашивает подтверждение: после очистки первая же проверка снова прочитает
    /// каждый файл целиком, а тихая порча, случившаяся до этого, останется
    /// незамеченной — сравнивать будет не с чем.
    /// </remarks>
    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        bool confirmed = await _dialogService.ConfirmAsync(
            "Очистить историю проверок?",
            "Отпечатки всех файлов будут забыты. Следующая проверка прочитает коллекцию заново, " +
            "а порча, случившаяся до этого момента, останется незамеченной — сравнивать будет не с чем.",
            "Очистить",
            "Отмена",
            destructive: true);

        if (!confirmed)
        {
            return;
        }

        EnsureHistoryOpen();
        _history.Clear();
        RefreshHistoryNote();
    }

    /// <summary>Открывает базу истории, если она ещё не открыта.</summary>
    private void EnsureHistoryOpen()
    {
        if (!_history.IsOpen)
        {
            _history.Open(ScanHistory.ResolveDefaultPath());
        }
    }

    /// <summary>Обновляет подпись со сведениями о базе.</summary>
    private void RefreshHistoryNote()
    {
        if (!TrackChanges)
        {
            HistoryNote = string.Empty;
            return;
        }

        EnsureHistoryOpen();

        int count = _history.Count();
        HistoryNote = count == 0
            ? "История пока пуста — она заполнится при первой проверке."
            : $"В истории {Core.Common.Format.Number(count)} " +
              $"{Core.Common.Format.Plural(count, "файл", "файла", "файлов")}; база лежит в {_history.DatabasePath}";
    }

    /// <summary>Открывает папку с файлом настроек в проводнике.</summary>
    [RelayCommand]
    private void OpenSettingsFolder() => _dialogService.RevealInExplorer(SettingsFilePath);

    /// <summary>Меняет цвет одного статуса.</summary>
    [RelayCommand]
    private void PickStatusColor(StatusColorRow? row)
    {
        if (row is null)
        {
            return;
        }

        string? picked = _dialogService.PickColor(row.Label, row.EffectiveHex);
        if (picked is null)
        {
            return;
        }

        row.Override = picked;
        row.EffectiveHex = picked;

        // Сохранение вызывается вручную: строки цвета — отдельные объекты, и
        // их правка не поднимает PropertyChanged у самих настроек, на котором
        // держится автосохранение. Без этой строки выбранный цвет оставался
        // только в списке и пропадал при перезапуске.
        _ = ApplyAsync();
    }

    /// <summary>Возвращает цвету статуса значение из темы.</summary>
    [RelayCommand]
    private void ResetStatusColor(StatusColorRow? row)
    {
        if (row is null)
        {
            return;
        }

        row.Override = null;
        row.EffectiveHex = _themeService.DefaultColorHex(row.Status);
        _ = ApplyAsync();
    }

    private void RefreshStatusColors()
    {
        StatusColorOverrides overrides = _settingsService.Current.StatusColors;

        Set(0, overrides.Ok);
        Set(1, overrides.Corrupted);
        Set(2, overrides.Warning);
        Set(3, overrides.Skipped);

        void Set(int index, string? value)
        {
            StatusColorRow row = StatusColors[index];
            row.Override = value;
            row.EffectiveHex = value ?? _themeService.DefaultColorHex(row.Status);
        }
    }

    /// <summary>Вариант порога «большого файла».</summary>
    /// <param name="Megabytes">Размер в мегабайтах; 0 — без ограничений.</param>
    /// <param name="Label">Подпись для списка.</param>
    public sealed record ThresholdOption(int Megabytes, string Label)
    {
        /// <inheritdoc />
        public override string ToString() => Label;
    }

    /// <summary>Вариант «число + подпись»: сколько файлов журнала, сколько мегабайт.</summary>
    /// <param name="Value">Само число.</param>
    /// <param name="Label">Подпись для списка.</param>
    public sealed record CountOption(int Value, string Label)
    {
        /// <inheritdoc />
        public override string ToString() => Label;
    }

    /// <summary>Вариант таймаута проверки файла.</summary>
    /// <param name="Seconds">Секунды.</param>
    /// <param name="Label">Подпись для списка.</param>
    public sealed record TimeoutOption(int Seconds, string Label)
    {
        /// <inheritdoc />
        public override string ToString() => Label;
    }
}

/// <summary>Чип формата в настройках: расширение и признак «обходить».</summary>
public sealed partial class FormatChip(string extension, Action onToggled) : ObservableObject
{
    private readonly Action _onToggled = onToggled;
    private bool _quiet;

    /// <summary>Расширение без точки, прописными: «FLAC».</summary>
    public string Extension { get; } = extension;

    /// <summary>Обходить файлы с этим расширением.</summary>
    [ObservableProperty]
    private bool _enabled = true;

    /// <summary>Ставит состояние, не поднимая пересчёт настроек (при загрузке).</summary>
    public void SetEnabledQuietly(bool value)
    {
        _quiet = true;
        Enabled = value;
        _quiet = false;
    }

    partial void OnEnabledChanged(bool value)
    {
        if (!_quiet)
        {
            _onToggled();
        }
    }
}

/// <summary>Строка настройки цвета статуса.</summary>
public sealed partial class StatusColorRow(CheckStatus status, string label) : ObservableObject
{
    /// <summary>Статус, к которому относится цвет.</summary>
    public CheckStatus Status { get; } = status;

    /// <summary>Подпись строки.</summary>
    public string Label { get; } = label;

    /// <summary>Цвет, выбранный пользователем; <see langword="null"/> — цвет темы.</summary>
    [ObservableProperty]
    private string? _override;

    /// <summary>Цвет, который сейчас показан.</summary>
    [ObservableProperty]
    private string _effectiveHex = "#808080";

    /// <summary>Цвет задан пользователем, а не взят из темы.</summary>
    public bool IsCustom => Override is not null;

    partial void OnOverrideChanged(string? value) => OnPropertyChanged(nameof(IsCustom));
}
