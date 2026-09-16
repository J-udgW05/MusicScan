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
/// Settings screen. Edits a copy of the settings and applies it as a whole.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    /// <summary>Preset large-file thresholds.</summary>
    /// <summary>Interface languages, each named in its own language.</summary>
    public static readonly IReadOnlyList<LanguageOption> LanguageOptions =
    [
        new(AppLanguage.Russian, "Русский"),
        new(AppLanguage.English, "English"),
    ];

    public static readonly IReadOnlyList<ThresholdOption> ThresholdOptions =
    [
        new(100, "100 МБ"),
        new(500, "500 МБ"),
        new(1024, "1 ГБ"),
        new(2048, "2 ГБ"),
        new(5120, "5 ГБ"),
        new(0, "Без ограничений"),
    ];

    /// <summary>Preset per-file timeouts.</summary>
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
    /// Fields are being filled from settings rather than edited by the user;
    /// without this flag loading would immediately save itself.
    /// </summary>
    private bool _loading;

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

        // Playlists have their own toggle, so only audio extensions become chips
        // that can be switched off individually.
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

        // Subfolders, tag checking, the large-file threshold and the timeout are also
        // edited from the quick panel on the scan tab. Without this subscription the
        // screen would show a snapshot taken at startup, and ApplyAsync — which writes
        // every field at once — would overwrite what was changed in the panel.
        _settingsService.Changed += OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        // Our own save comes back through this event. There is nothing to reload,
        // and reloading mid-apply would clobber the edit in progress.
        if (_applying)
        {
            return;
        }

        // Settings can arrive from a background thread while bindings live on the UI
        // thread. When already on it, reload immediately rather than round-tripping
        // through the queue, or the old value flickers.
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

    /// <summary>Supported formats, shown as chips in the formats section.</summary>
    /// <remarks>
    /// A chip toggles: a disabled extension goes into
    /// <see cref="AppSettings.DisabledExtensions"/> and is skipped by the walk.
    /// </remarks>
    public ObservableCollection<FormatChip> Formats { get; }

    /// <summary>Chip summary, e.g. "12 of 14".</summary>
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

    /// <summary>Status colours shown in the appearance section.</summary>
    public ObservableCollection<StatusColorRow> StatusColors { get; }

    /// <summary>Raised after settings are changed and applied.</summary>
    public event EventHandler? Applied;

    // ── Appearance ───────────────────────────────────────────────────────

    [ObservableProperty]
    private AppTheme _theme;

    [ObservableProperty]
    private ListDensity _listDensity;

    [ObservableProperty]
    private LanguageOption _language = LanguageOptions[0];

    /// <summary>Results row height for the selected density.</summary>
    /// <remarks>
    /// The results view binds its row padding to this, so the density setting has
    /// a visible effect.
    /// </remarks>
    public Thickness RowPadding => ListDensity == ListDensity.Compact
        ? new Thickness(16, 7, 16, 7)
        : new Thickness(16, 11, 16, 11);

    partial void OnListDensityChanged(ListDensity value) => OnPropertyChanged(nameof(RowPadding));

    [ObservableProperty]
    private bool _showFullPaths;

    [ObservableProperty]
    private bool _monospacePaths;

    [ObservableProperty]
    private bool _showStatusBar;

    [ObservableProperty]
    private bool _micaEffect;

    [ObservableProperty]
    private bool _animations;

    /// <summary>The OS can draw a Mica backdrop.</summary>
    /// <remarks>
    /// Without this the toggle would lie: on Windows 10 it could be switched on
    /// with no effect.
    /// </remarks>
    public bool IsMicaSupported => _themeService.IsMicaSupported;

    /// <summary>
    /// Caption under the Mica toggle; only shown when the effect is unavailable,
    /// to explain why the toggle is disabled.
    /// </summary>
    public string MicaNote => IsMicaSupported
        ? string.Empty
        : "недоступно: подложку умеет рисовать только Windows 11";

    // ── Scanning ─────────────────────────────────────────────────────────

    [ObservableProperty]
    private bool _recursive;

    [ObservableProperty]
    private bool _checkMetadata;

    [ObservableProperty]
    private bool _checkPlaylists;

    [ObservableProperty]
    private CheckDepth _checkDepth;

    [ObservableProperty]
    private bool _respectDriveType;

    [ObservableProperty]
    private bool _trackChanges;

    /// <summary>How many files the history remembers.</summary>
    [ObservableProperty]
    private string _historyNote = string.Empty;

    [ObservableProperty]
    private bool _inspectCollection;

    [ObservableProperty]
    private bool _detectSilence;

    [ObservableProperty]
    private bool _detectClipping;

    [ObservableProperty]
    private bool _detectTranscode;

    [ObservableProperty]
    private bool _verifyExtension;

    [ObservableProperty]
    private bool _verifyContainerIntegrity;

    [ObservableProperty]
    private ThresholdOption _threshold = ThresholdOptions[1];

    [ObservableProperty]
    private TimeoutOption _timeout = TimeoutOptions[2];

    [ObservableProperty]
    private bool _autoParallelism;

    [ObservableProperty]
    private int _manualParallelism;

    [ObservableProperty]
    private bool _warnAboutLargeFiles;

    // ── Locked files ─────────────────────────────────────────────────────

    [ObservableProperty]
    private LockedFileAction _lockedFileAction;

    [ObservableProperty]
    private int _lockedWaitSeconds;

    [ObservableProperty]
    private int _lockedRetryCount;

    [ObservableProperty]
    private bool _detectOwnerProcess;

    [ObservableProperty]
    private bool _allowApplyToAllLocked;

    [ObservableProperty]
    private bool _offerCloseOwner;

    // ── Formats ──────────────────────────────────────────────────────────

    [ObservableProperty]
    private bool _enableIsoSacd;

    /// <summary>User extensions, comma separated.</summary>
    [ObservableProperty]
    private string _customExtensions = string.Empty;

    // ── General ──────────────────────────────────────────────────────────

    [ObservableProperty]
    private bool _rememberLastFolder;

    [ObservableProperty]
    private bool _offerStartAfterFolderSelected;

    [ObservableProperty]
    private bool _showFirstRunTip;

    [ObservableProperty]
    private bool _soundOnFinish;

    // ── Reports ──────────────────────────────────────────────────────────

    [ObservableProperty]
    private ReportFormat _defaultReportFormat;

    [ObservableProperty]
    private string _reportsFolder = string.Empty;

    [ObservableProperty]
    private bool _includeOkFilesInReport;

    [ObservableProperty]
    private bool _openReportAfterSave;


    /// <summary>Path to the settings file, shown to the user.</summary>
    public string SettingsFilePath => _settingsService.SettingsFilePath;

    /// <summary>Loads values from the current settings.</summary>
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

    /// <summary>Every change is applied immediately, with no Apply button.</summary>
    /// <remarks>
    /// That is how Windows 11 Settings behaves, and how the quick panel on the
    /// scan tab already worked; a separate button would also lose edits whenever
    /// the user forgot to press it.
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
        Language = LanguageOptions.FirstOrDefault(o => o.Code == (_draft.Language ?? AppLanguage.Current))
            ?? LanguageOptions[0];
        ListDensity = _draft.ListDensity;
        ShowFullPaths = _draft.ShowFullPaths;
        MonospacePaths = _draft.MonospacePaths;
        ShowStatusBar = _draft.ShowStatusBar;

        // Nulls were already resolved at startup, but the fallback costs nothing and
        // keeps this screen independent of call order.
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

    /// <summary>Collects the edited values and saves them.</summary>
    public async Task ApplyAsync()
    {
        _applying = true;

        AppSettings settings = _settingsService.Current.Clone();

        settings.Theme = Theme;
        settings.Language = Language.Code;
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

        // Status colours are edited separately and already live in StatusColors.
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
        AppLanguage.Apply(settings.Language ?? AppLanguage.Current);

        _loading = true;
        try
        {
            // Default status colours depend on the theme; refreshing the captions after a
            // theme change is not a user edit.
            RefreshStatusColors();
        }
        finally
        {
            _loading = false;
        }

        Applied?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Resets every setting to its default.</summary>
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

    /// <summary>Picks the reports folder.</summary>
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

    /// <summary>Wipes the scan history.</summary>
    /// <remarks>
    /// Asks first: afterwards the next scan reads every file whole again, and any
    /// silent corruption that happened before goes unnoticed — there is nothing
    /// left to compare against.
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

    /// <summary>Opens the history database if it is not open yet.</summary>
    private void EnsureHistoryOpen()
    {
        if (!_history.IsOpen)
        {
            _history.Open(ScanHistory.ResolveDefaultPath());
        }
    }

    /// <summary>Refreshes the database information caption.</summary>
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

    /// <summary>Reveals the settings file in Explorer.</summary>
    [RelayCommand]
    private void OpenSettingsFolder() => _dialogService.RevealInExplorer(SettingsFilePath);

    /// <summary>Changes the colour of one status.</summary>
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

        // Saved explicitly: colour rows are separate objects, and editing them does
        // not raise PropertyChanged on the settings that autosave relies on. Without
        // this the picked colour was lost on restart.
        _ = ApplyAsync();
    }

    /// <summary>Restores a status colour to the theme default.</summary>
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

    /// <summary>An interface language option.</summary>
    public sealed record LanguageOption(string Code, string Label)
    {
        /// <inheritdoc />
        public override string ToString() => Label;
    }

    /// <summary>A large-file threshold option.</summary>
    /// <param name="Megabytes">Size in megabytes; 0 means no limit.</param>
    public sealed record ThresholdOption(int Megabytes, string Label)
    {
        /// <inheritdoc />
        public override string ToString() => Label;
    }

    /// <summary>A number with its display caption.</summary>
    public sealed record CountOption(int Value, string Label)
    {
        /// <inheritdoc />
        public override string ToString() => Label;
    }

    /// <summary>A per-file timeout option.</summary>
    public sealed record TimeoutOption(int Seconds, string Label)
    {
        /// <inheritdoc />
        public override string ToString() => Label;
    }
}

/// <summary>Format chip: extension and whether it is scanned.</summary>
public sealed partial class FormatChip(string extension, Action onToggled) : ObservableObject
{
    private readonly Action _onToggled = onToggled;
    private bool _quiet;

    /// <summary>Upper-case extension without the dot, e.g. "FLAC".</summary>
    public string Extension { get; } = extension;

    /// <summary>Files with this extension are scanned.</summary>
    [ObservableProperty]
    private bool _enabled = true;

    /// <summary>Sets the state without triggering a settings update; used while loading.</summary>
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

/// <summary>Status colour row.</summary>
public sealed partial class StatusColorRow(CheckStatus status, string label) : ObservableObject
{
    public CheckStatus Status { get; } = status;

    public string Label { get; } = label;

    /// <summary>User colour; <see langword="null"/> means the theme colour.</summary>
    [ObservableProperty]
    private string? _override;

    /// <summary>Colour currently displayed.</summary>
    [ObservableProperty]
    private string _effectiveHex = "#808080";

    /// <summary>The colour was set by the user rather than taken from the theme.</summary>
    public bool IsCustom => Override is not null;

    partial void OnOverrideChanged(string? value) => OnPropertyChanged(nameof(IsCustom));
}
