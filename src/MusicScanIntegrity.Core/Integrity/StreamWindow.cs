namespace MusicScanIntegrity.Core.Integrity;

/// <summary>
/// A sliding window over a stream, letting validators look ahead without
/// reading the whole file into memory.
/// </summary>
/// <remarks>
/// Frames and pages are checked in order but need look-ahead — a header here,
/// a whole frame there to checksum it. Reading the file whole is not an option:
/// collections contain multi-gigabyte disc images. The window holds only what
/// the current frame needs and grows only for genuinely large frames.
/// </remarks>
internal sealed class StreamWindow(Stream stream, int initialCapacity = 64 * 1024)
{
    private byte[] _buffer = new byte[Math.Max(4096, initialCapacity)];
    private int _start;
    private int _end;
    private long _consumed;
    private bool _endOfStream;

    /// <summary>Offset of the current position from the start of the file.</summary>
    public long Position => _consumed;

    /// <summary>How many bytes are buffered and ready to read.</summary>
    public int Available => _end - _start;

    /// <summary>The stream ended and the window is empty.</summary>
    public bool AtEnd => _endOfStream && Available == 0;

    /// <summary>
    /// Tops the window up to the requested number of bytes.
    /// </summary>
    /// <returns><see langword="false" /> when the file ended first.</returns>
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

    /// <summary>Peeks at the start of the window without advancing.</summary>
    /// <param name="count">Bytes wanted; never more than is available.</param>
    public ReadOnlySpan<byte> Peek(int count) =>
        _buffer.AsSpan(_start, Math.Min(count, Available));

    /// <summary>Advances the position over bytes already read.</summary>
    public void Advance(int count)
    {
        int step = Math.Min(count, Available);
        _start += step;
        _consumed += step;
    }

    /// <summary>
    /// Skips an arbitrary number of bytes, seeking when the stream allows.
    /// </summary>
    /// <returns><see langword="false" /> when the file ended first.</returns>
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

        // Non-seekable stream; read through manually.
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

    /// <summary>Makes room: moves the remainder to the front and grows the buffer if needed.</summary>
    private void MakeRoom(int count)
    {
        if (_buffer.Length >= count)
        {
            // Moving the remainder to the front frees the whole buffer, not
            // just its tail. Comparing against the tail instead would allocate
            // a new buffer of the same size for every frame that did not fit —
            // hundreds of them on a large file.
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
