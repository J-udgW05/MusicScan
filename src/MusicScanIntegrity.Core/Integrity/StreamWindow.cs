namespace MusicScanIntegrity.Core.Integrity;

/// <summary>
/// Скользящее окно над потоком: даёт разборщикам смотреть вперёд на несколько
/// байт, не читая файл целиком в память.
/// </summary>
/// <remarks>
/// Проверять кадры и страницы приходится последовательно, но с заглядыванием
/// вперёд — то на заголовок, то на весь кадр, чтобы посчитать по нему сумму.
/// Читать ради этого файл целиком нельзя: в коллекциях попадаются образы дисков
/// на несколько гигабайт. Окно держит в памяти только то, что нужно текущему
/// кадру, и растёт лишь когда кадр действительно большой.
/// </remarks>
internal sealed class StreamWindow(Stream stream, int initialCapacity = 64 * 1024)
{
    private byte[] _buffer = new byte[Math.Max(4096, initialCapacity)];
    private int _start;
    private int _end;
    private long _consumed;
    private bool _endOfStream;

    /// <summary>Смещение текущей позиции от начала файла.</summary>
    public long Position => _consumed;

    /// <summary>Сколько байт уже лежит в окне и готово к чтению.</summary>
    public int Available => _end - _start;

    /// <summary>Поток закончился, и в окне ничего не осталось.</summary>
    public bool AtEnd => _endOfStream && Available == 0;

    /// <summary>
    /// Добивает окно до нужного числа байт.
    /// </summary>
    /// <param name="count">Сколько байт должно быть доступно.</param>
    /// <returns><see langword="false" />, если файл закончился раньше.</returns>
    public bool Ensure(int count)
    {
        if (count <= Available)
        {
            return true;
        }

        MakeRoom(count);

        while (Available < count && !_endOfStream)
        {
            int read = stream.Read(_buffer, _end, _buffer.Length - _end);
            if (read <= 0)
            {
                _endOfStream = true;
                break;
            }

            _end += read;
        }

        return Available >= count;
    }

    /// <summary>Показывает начало окна, не сдвигая позицию.</summary>
    /// <param name="count">Сколько байт нужно; больше доступного не вернётся.</param>
    /// <returns>Участок буфера.</returns>
    public ReadOnlySpan<byte> Peek(int count) =>
        _buffer.AsSpan(_start, Math.Min(count, Available));

    /// <summary>Сдвигает позицию вперёд по уже прочитанным байтам.</summary>
    /// <param name="count">На сколько байт сдвинуться.</param>
    public void Advance(int count)
    {
        int step = Math.Min(count, Available);
        _start += step;
        _consumed += step;
    }

    /// <summary>
    /// Пропускает произвольное число байт, при возможности перепрыгивая по потоку.
    /// </summary>
    /// <param name="count">Сколько байт пропустить.</param>
    /// <returns><see langword="false" />, если файл закончился раньше.</returns>
    public bool Skip(long count)
    {
        if (count <= 0)
        {
            return true;
        }

        if (count <= Available)
        {
            Advance((int)count);
            return true;
        }

        long remaining = count - Available;
        _consumed += Available;
        _start = _end = 0;

        if (stream.CanSeek)
        {
            long target = stream.Position + remaining;
            if (target > stream.Length)
            {
                _endOfStream = true;
                _consumed = stream.Length;
                stream.Position = stream.Length;
                return false;
            }

            stream.Position = target;
            _consumed += remaining;
            return true;
        }

        // Поток без перемотки — дочитываем вручную.
        while (remaining > 0)
        {
            if (!Ensure(1))
            {
                return false;
            }

            int step = (int)Math.Min(remaining, Available);
            Advance(step);
            remaining -= step;
        }

        return true;
    }

    /// <summary>Освобождает место в буфере: сдвигает остаток к началу и при нужде растит его.</summary>
    private void MakeRoom(int count)
    {
        if (_buffer.Length >= count)
        {
            // Места хватит, если сдвинуть остаток к началу: после сдвига
            // свободен весь буфер, а не только его хвост. Сравнивать с хвостом
            // значило бы заводить новый буфер того же размера на каждом кадре,
            // который не поместился в остаток, — а таких на большом файле сотни.
            if (_start > 0)
            {
                Compact();
            }

            return;
        }

        int capacity = _buffer.Length;
        while (capacity < count)
        {
            capacity *= 2;
        }

        byte[] grown = new byte[capacity];
        Array.Copy(_buffer, _start, grown, 0, Available);
        _end = Available;
        _start = 0;
        _buffer = grown;
    }

    private void Compact()
    {
        Array.Copy(_buffer, _start, _buffer, 0, Available);
        _end = Available;
        _start = 0;
    }
}
