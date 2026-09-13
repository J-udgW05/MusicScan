namespace MusicScanIntegrity.Core.Models;

/// <summary>
/// Конкретная причина, по которой файл получил свой статус.
/// Перечень покрывает таблицу особых ситуаций из 03_IMPLEMENTATION_GUIDE.md, раздел 1.
/// </summary>
public enum IssueCode
{
    /// <summary>Замечаний нет.</summary>
    None = 0,

    /// <summary>Файл пропал с диска между построением списка и проверкой.</summary>
    FileNotFound,

    /// <summary>Файл существует, но операционная система не дала его прочитать.</summary>
    AccessDenied,

    /// <summary>Файл занят другой программой, пользователь решил его пропустить.</summary>
    LockedSkippedByUser,

    /// <summary>Файл занят другой программой, время ожидания разблокировки истекло.</summary>
    LockedWaitTimeout,

    /// <summary>Файл был занят, проверка выполнена по временной копии.</summary>
    LockedCheckedViaCopy,

    /// <summary>Не удалось сделать временную копию занятого файла.</summary>
    LockedCopyFailed,

    /// <summary>Файл защищён паролем/DRM (встречается у части WMA и AAC).</summary>
    PasswordProtected,

    /// <summary>Не удалось вообще начать декодирование.</summary>
    DecodeStartFailed,

    /// <summary>Декодирование началось, но чтение аудиоданных сорвалось.</summary>
    AudioReadFailed,

    /// <summary>Файл пустой или заведомо короче, чем может быть валидное аудио.</summary>
    EmptyFile,

    /// <summary>Проверка одного файла превысила разрешённый таймаут.</summary>
    CheckTimeout,

    /// <summary>Расширение не совпадает с реальным содержимым, но это аудио.</summary>
    ExtensionMismatch,

    /// <summary>Включена проверка тегов, а теги отсутствуют или не читаются.</summary>
    MetadataProblem,

    /// <summary>Размер файла больше настроенного порога «большого файла».</summary>
    LargeFile,

    /// <summary>Файл, указанный в плейлисте, отсутствует на диске.</summary>
    PlaylistTargetMissing,

    /// <summary>Метки дорожек в cue-листе выходят за длительность файла.</summary>
    CueMarksBeyondFile,

    /// <summary>Не удалось разобрать сам файл плейлиста.</summary>
    PlaylistUnreadable,

    /// <summary>Контрольная сумма внутри формата не сошлась — файл повреждён.</summary>
    ChecksumMismatch,

    /// <summary>Структура файла разрушена: кадры, страницы или блоки не сходятся.</summary>
    ContainerDamaged,

    /// <summary>Файл обрывается: данных меньше, чем обещано его же заголовком.</summary>
    Truncated,

    /// <summary>Файл декодируется, но звука в нём нет — сплошная тишина.</summary>
    DigitalSilence,

    /// <summary>Внутри звучащего трека провал в тишину — похоже на потерянный кусок.</summary>
    AudioDropout,

    /// <summary>Заметная часть отсчётов упирается в предел шкалы.</summary>
    Clipping,

    /// <summary>У записи есть постоянная составляющая — признак плохой оцифровки.</summary>
    DcOffset,

    /// <summary>
    /// Содержимое изменилось при неизменных размере и дате — тихая порча.
    /// </summary>
    SilentCorruption,

    /// <summary>Текст в тегах прочитан не в той кодировке — кракозябры.</summary>
    BrokenTagText,

    /// <summary>Похоже, файл собран перекодированием из сжатого с потерями.</summary>
    TranscodeSuspected,

    /// <summary>Непредвиденная ошибка при проверке именно этого файла.</summary>
    UnexpectedError,
}
