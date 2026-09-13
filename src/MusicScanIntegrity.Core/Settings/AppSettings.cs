using System.Text.Json.Serialization;

namespace MusicScanIntegrity.Core.Settings;

/// <summary>
/// Все настройки пользователя. Разделы соответствуют окну настроек из референса
/// («Общие», «Проверка», «Занятые файлы», «Форматы», «Отчёты», «Внешний вид»),
/// значения по умолчанию — UI_SPEC.md, раздел 7.
/// </summary>
/// <remarks>
/// Класс намеренно плоский и сериализуемый в JSON: файл настроек должен оставаться
/// читаемым и восстановимым вручную. Свойства с сеттерами, а не record — окно
/// настроек правит поля по одному.
/// </remarks>
public sealed class AppSettings
{
    /// <summary>Версия формата файла настроек — на случай будущих миграций.</summary>
    public int SchemaVersion { get; set; } = 1;

    // ── Общие ────────────────────────────────────────────────────────────────

    /// <summary>Запоминать последнюю папку и подставлять её при запуске.</summary>
    public bool RememberLastFolder { get; set; } = true;

    /// <summary>Последняя выбранная папка.</summary>
    public string? LastFolder { get; set; }

    /// <summary>
    /// Предлагать начать проверку сразу после выбора папки
    /// (02_ARCHITECTURE.md, раздел 9).
    /// </summary>
    public bool OfferStartAfterFolderSelected { get; set; } = true;

    /// <summary>Показывать приветствие при первом запуске.</summary>
    public bool ShowFirstRunTip { get; set; } = true;

    /// <summary>Системный звук по завершении проверки.</summary>
    public bool SoundOnFinish { get; set; }

    // ── Проверка ─────────────────────────────────────────────────────────────

    /// <summary>Рекурсивный обход подпапок.</summary>
    public bool Recursive { get; set; } = true;

    /// <summary>
    /// Проверять теги. Выключено по умолчанию: отсутствие тегов — не повреждение
    /// (03_IMPLEMENTATION_GUIDE.md, раздел 2).
    /// </summary>
    public bool CheckMetadata { get; set; }

    /// <summary>Искать отсутствующие пути внутри плейлистов.</summary>
    public bool CheckPlaylists { get; set; } = true;

    /// <summary>
    /// Насколько глубоко читать файл декодером.
    /// </summary>
    /// <remarks>
    /// По умолчанию выборочная: начало, конец и несколько мест в середине.
    /// Только начало (как было раньше) не слышит повреждения в середине трека,
    /// а полное декодирование коллекции занимает часы.
    /// </remarks>
    public CheckDepth CheckDepth { get; set; } = CheckDepth.Sampled;

    /// <summary>
    /// Замечать сплошную тишину и провалы внутри трека.
    /// </summary>
    /// <remarks>
    /// Включено по умолчанию: файл, который декодируется в тишину, формально
    /// исправен, а слушать в нём нечего. Считается по тем же отсчётам, что уже
    /// прошли через декодер, поэтому ничего не стоит.
    /// </remarks>
    public bool DetectSilence { get; set; } = true;

    /// <summary>
    /// Замечать перегрузку и постоянную составляющую.
    /// </summary>
    /// <remarks>
    /// Выключено по умолчанию, и намеренно: у современных мастерингов отсчёты
    /// упираются в предел шкалы сплошь и рядом, это их обычное состояние.
    /// Включённая по умолчанию проверка ругалась бы на половину коллекции.
    /// </remarks>
    public bool DetectClipping { get; set; }

    /// <summary>
    /// Искать признаки перекодирования: обрезанный сверху спектр.
    /// </summary>
    /// <remarks>
    /// Результат — подозрение, а не приговор: у старых записей и намеренно
    /// узкополосных вещей верхних частот нет и без всякого перекодирования.
    /// Поэтому замечание жёлтое и формулируется словом «похоже».
    /// </remarks>
    public bool DetectTranscode { get; set; } = true;

    /// <summary>
    /// Разбирать альбомы по папкам и искать повторяющиеся треки.
    /// </summary>
    /// <remarks>
    /// Считается по готовым результатам, файлы второй раз не читаются. Часть
    /// проверок работает только при включённых тегах: без них не видно ни
    /// номеров дорожек, ни названий альбомов.
    /// </remarks>
    public bool InspectCollection { get; set; } = true;

    /// <summary>
    /// Следить за порчей: считать отпечаток каждого файла и хранить историю.
    /// </summary>
    /// <remarks>
    /// Выключено по умолчанию, потому что стоит дорого: первая проверка читает
    /// каждый файл целиком и заводит базу рядом с программой. Зато появляется
    /// то, что иначе не увидеть, — содержимое изменилось, а размер и дата
    /// прежние. Так выглядит сбойный диск.
    /// </remarks>
    public bool TrackChanges { get; set; }

    /// <summary>Сверять расширение с реальным содержимым файла.</summary>
    public bool VerifyExtensionMatchesContent { get; set; } = true;

    /// <summary>
    /// Проверять файл его собственными контрольными суммами и структурой.
    /// </summary>
    /// <remarks>
    /// Включено по умолчанию: это единственная проверка, дающая точный ответ
    /// вместо «декодер открыл файл». Читается весь файл, но без распаковки,
    /// поэтому упирается в скорость диска, а не в процессор.
    /// </remarks>
    public bool VerifyContainerIntegrity { get; set; } = true;

    /// <summary>
    /// Порог «большого файла» в мегабайтах. 0 означает «без ограничений».
    /// Готовые варианты: 100, 500, 1024, 2048, 5120, 0.
    /// </summary>
    public int LargeFileThresholdMb { get; set; } = 500;

    /// <summary>Таймаут проверки одного файла в секундах.</summary>
    public int FileTimeoutSeconds { get; set; } = 60;

    /// <summary>Параллельность выбирается автоматически по числу ядер.</summary>
    public bool AutoParallelism { get; set; } = true;

    /// <summary>Параллельность, заданная вручную (когда <see cref="AutoParallelism"/> выключено).</summary>
    public int ManualParallelism { get; set; } = 4;

    /// <summary>
    /// Учитывать тип диска при выборе числа потоков.
    /// </summary>
    /// <remarks>
    /// На жёстком диске восемь параллельных чтений заставляют головку метаться
    /// между дорожками, и проверка идёт медленнее, чем в два потока. На
    /// твердотельном перемотки нет, и ограничивать нечего.
    /// </remarks>
    public bool RespectDriveType { get; set; } = true;

    /// <summary>Предупреждать о больших файлах до начала их проверки.</summary>
    public bool WarnAboutLargeFiles { get; set; } = true;

    // ── Занятые файлы ────────────────────────────────────────────────────────

    /// <summary>Что делать по умолчанию с занятым файлом.</summary>
    public LockedFileAction LockedFileAction { get; set; } = LockedFileAction.Ask;

    /// <summary>Сколько секунд ждать освобождения перед повторной попыткой.</summary>
    public int LockedWaitSeconds { get; set; } = 30;

    /// <summary>Сколько раз повторять попытку.</summary>
    public int LockedRetryCount { get; set; } = 3;

    /// <summary>Пытаться определить программу-владельца занятого файла.</summary>
    public bool DetectOwnerProcess { get; set; } = true;

    /// <summary>
    /// Предлагать закрыть программу-владельца. Выключено по умолчанию:
    /// закрытие чужого процесса потенциально опасно.
    /// </summary>
    public bool OfferCloseOwner { get; set; }

    /// <summary>Показывать в вопросе флажок «поступать так же со всеми занятыми файлами».</summary>
    public bool AllowApplyToAllLocked { get; set; } = true;

    // ── Форматы ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Расширения аудио, выключенные пользователем. По умолчанию включены все
    /// из 01_SPECIFICATION.md, раздел 4. Хранятся именно исключения, чтобы новые
    /// поддерживаемые форматы включались автоматически.
    /// </summary>
    public List<string> DisabledExtensions { get; set; } = [];

    /// <summary>Дополнительные расширения, добавленные пользователем (например «.mpc»).</summary>
    public List<string> CustomExtensions { get; set; } = [];

    /// <summary>
    /// Экспериментальная поддержка образов дисков ISO (SACD).
    /// Выключена по умолчанию: обычный .iso — это не аудиофайл.
    /// </summary>
    public bool EnableIsoSacd { get; set; }

    // ── Отчёты ───────────────────────────────────────────────────────────────

    /// <summary>Формат отчёта по умолчанию.</summary>
    public ReportFormat DefaultReportFormat { get; set; } = ReportFormat.Html;

    /// <summary>Папка, куда сохраняются отчёты. Пусто — «Документы».</summary>
    public string? ReportsFolder { get; set; }

    /// <summary>Включать в отчёт файлы со статусом «в порядке».</summary>
    public bool IncludeOkFilesInReport { get; set; }

    /// <summary>Открывать отчёт сразу после сохранения.</summary>
    public bool OpenReportAfterSave { get; set; } = true;

    // ── Внешний вид ──────────────────────────────────────────────────────────

    /// <summary>Тема оформления.</summary>
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>
    /// Пользовательские цвета статусов в формате «#RRGGBB».
    /// Пустое значение означает «взять цвет из темы» (UI_SPEC.md, раздел 1).
    /// </summary>
    public StatusColorOverrides StatusColors { get; set; } = new();

    /// <summary>Плотность строк в таблице результатов.</summary>
    public ListDensity ListDensity { get; set; } = ListDensity.Normal;

    /// <summary>Показывать полные пути (иначе путь сокращается с начала).</summary>
    public bool ShowFullPaths { get; set; } = true;

    /// <summary>Моноширинный шрифт для путей.</summary>
    public bool MonospacePaths { get; set; } = true;

    /// <summary>Показывать строку состояния внизу окна.</summary>
    public bool ShowStatusBar { get; set; } = true;

    /// <summary>
    /// Подложка Mica под окном; <see langword="null" /> — выбора ещё не было.
    /// </summary>
    /// <remarks>
    /// Три значения, а не два, и это принципиально. «Ещё не выбирали» и
    /// «выключено пользователем» — разные вещи: в первом случае при первом
    /// запуске надо посмотреть, включены ли эффекты в самой Windows, во втором
    /// смотреть некуда, решение уже принято. Пустое значение разрешается один
    /// раз при запуске и тут же записывается в файл.
    /// </remarks>
    public bool? MicaEffect { get; set; }

    /// <summary>
    /// Анимации; <see langword="null" /> — выбора ещё не было.
    /// </summary>
    /// <remarks>
    /// Смысл трёх значений тот же, что у <see cref="MicaEffect" />: при первом
    /// запуске берётся системная настройка «Эффекты анимации», дальше — то, что
    /// выбрал человек.
    /// </remarks>
    public bool? Animations { get; set; }

    // ── Производные значения ─────────────────────────────────────────────────

    /// <summary>
    /// Сколько файлов проверять одновременно. «Авто» — по числу ядер,
    /// но не больше восьми (UI_SPEC.md, раздел 7).
    /// </summary>
    [JsonIgnore]
    public int EffectiveParallelism => AutoParallelism
        ? Math.Clamp(Environment.ProcessorCount, 1, 8)
        : Math.Clamp(ManualParallelism, 1, 64);

    /// <summary>Наибольшее число потоков на диске с подвижной головкой.</summary>
    public const int HardDiskParallelism = 2;

    /// <summary>Порог «большого файла» в байтах; <see langword="null"/> — без ограничений.</summary>
    [JsonIgnore]
    public long? LargeFileThresholdBytes =>
        LargeFileThresholdMb <= 0 ? null : (long)LargeFileThresholdMb * 1024 * 1024;

    /// <summary>Таймаут проверки одного файла.</summary>
    [JsonIgnore]
    public TimeSpan FileTimeout => TimeSpan.FromSeconds(Math.Clamp(FileTimeoutSeconds, 1, 3600));

    /// <summary>
    /// Глубокая копия. Окно настроек правит копию и применяет её только по «Сохранить»,
    /// поэтому списки тоже должны копироваться, а не разделяться ссылкой.
    /// </summary>
    public AppSettings Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        RememberLastFolder = RememberLastFolder,
        LastFolder = LastFolder,
        OfferStartAfterFolderSelected = OfferStartAfterFolderSelected,
        ShowFirstRunTip = ShowFirstRunTip,
        SoundOnFinish = SoundOnFinish,
        Recursive = Recursive,
        CheckMetadata = CheckMetadata,
        CheckPlaylists = CheckPlaylists,
        CheckDepth = CheckDepth,
        InspectCollection = InspectCollection,
        TrackChanges = TrackChanges,
        DetectSilence = DetectSilence,
        DetectTranscode = DetectTranscode,
        DetectClipping = DetectClipping,
        VerifyExtensionMatchesContent = VerifyExtensionMatchesContent,
        VerifyContainerIntegrity = VerifyContainerIntegrity,
        LargeFileThresholdMb = LargeFileThresholdMb,
        FileTimeoutSeconds = FileTimeoutSeconds,
        AutoParallelism = AutoParallelism,
        ManualParallelism = ManualParallelism,
        WarnAboutLargeFiles = WarnAboutLargeFiles,
        RespectDriveType = RespectDriveType,
        LockedFileAction = LockedFileAction,
        LockedWaitSeconds = LockedWaitSeconds,
        LockedRetryCount = LockedRetryCount,
        DetectOwnerProcess = DetectOwnerProcess,
        OfferCloseOwner = OfferCloseOwner,
        AllowApplyToAllLocked = AllowApplyToAllLocked,
        DisabledExtensions = [.. DisabledExtensions],
        CustomExtensions = [.. CustomExtensions],
        EnableIsoSacd = EnableIsoSacd,
        DefaultReportFormat = DefaultReportFormat,
        ReportsFolder = ReportsFolder,
        IncludeOkFilesInReport = IncludeOkFilesInReport,
        OpenReportAfterSave = OpenReportAfterSave,
        Theme = Theme,
        StatusColors = StatusColors.Clone(),
        ListDensity = ListDensity,
        ShowFullPaths = ShowFullPaths,
        MicaEffect = MicaEffect,
        Animations = Animations,
        MonospacePaths = MonospacePaths,
        ShowStatusBar = ShowStatusBar,
    };
}

/// <summary>Пользовательские цвета статусов; <see langword="null"/> — цвет из темы.</summary>
public sealed class StatusColorOverrides
{
    /// <summary>Цвет статуса «в порядке».</summary>
    public string? Ok { get; set; }

    /// <summary>Цвет статуса «повреждён».</summary>
    public string? Corrupted { get; set; }

    /// <summary>Цвет статуса «предупреждение».</summary>
    public string? Warning { get; set; }

    /// <summary>Цвет статуса «пропущен».</summary>
    public string? Skipped { get; set; }

    /// <summary>Все цвета сброшены к теме.</summary>
    public bool IsEmpty => Ok is null && Corrupted is null && Warning is null && Skipped is null;

    /// <summary>Копия значений.</summary>
    public StatusColorOverrides Clone() => new()
    {
        Ok = Ok,
        Corrupted = Corrupted,
        Warning = Warning,
        Skipped = Skipped,
    };
}
