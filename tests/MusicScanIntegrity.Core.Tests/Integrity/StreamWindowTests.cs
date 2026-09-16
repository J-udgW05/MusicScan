using MusicScanIntegrity.Core.Integrity;
using Xunit;

namespace MusicScanIntegrity.Core.Tests.Integrity;

/// <summary>The sliding window every validator reads through.</summary>
/// <remarks>
/// A bug here does not look like a window bug; it looks like a damaged file.
/// Hence the window itself is tested, not only the verdicts built on it.
/// </remarks>
public sealed class StreamWindowTests
{
    /// <summary>Smallest buffer the window allows itself.</summary>
    private const int MinimumBuffer = 4096;

    [Fact]
    public void Position_counts_consumed_bytes()
    {
        StreamWindow window = Window(1000);

        Assert.Equal(0, window.Position);
        Assert.True(window.Ensure(100));

        window.Advance(40);
        Assert.Equal(40, window.Position);

        window.Advance(60);
        Assert.Equal(100, window.Position);
    }

    [Fact]
    public void Peek_does_not_advance()
    {
        StreamWindow window = Window(1000);
        window.Ensure(10);

        Assert.Equal(Pattern(0, 10), window.Peek(10).ToArray());
        Assert.Equal(Pattern(0, 10), window.Peek(10).ToArray());
        Assert.Equal(0, window.Position);
    }

    [Fact]
    public void End_of_stream_shows_as_failed_fill()
    {
        StreamWindow window = Window(100);

        Assert.True(window.Ensure(100));
        Assert.False(window.Ensure(101));

        window.Advance(100);
        Assert.True(window.AtEnd);
    }

    /// <summary>A frame longer than the buffer is routine for high-bitrate files.</summary>
    [Fact]
    public void Buffer_grows_for_larger_request()
    {
        StreamWindow window = Window(MinimumBuffer * 5);
        int wanted = MinimumBuffer * 3;

        Assert.True(window.Ensure(wanted));
        Assert.Equal(Pattern(0, wanted), window.Peek(wanted).ToArray());
    }

    /// <summary>The key property of compaction: bytes after it are unchanged.</summary>
    /// <remarks>
    /// When the buffer tail is full, the window moves the unread remainder to the
    /// front. The step size makes this happen many times per pass, since that is
    /// exactly where bytes get lost if the lengths are miscalculated.
    /// </remarks>
    [Fact]
    public void Compaction_loses_no_bytes()
    {
        const int Length = MinimumBuffer * 8;
        const int Step = 300;
        const int Lookahead = 1200;

        StreamWindow window = Window(Length);

        for (int offset = 0; offset + Lookahead <= Length; offset += Step)
        {
            Assert.True(window.Ensure(Lookahead));
            Assert.Equal(offset, window.Position);
            Assert.Equal(Pattern(offset, Lookahead), window.Peek(Lookahead).ToArray());
            window.Advance(Step);
        }
    }

    [Fact]
    public void Skip_moves_past_buffered_data()
    {
        StreamWindow window = Window(MinimumBuffer * 4);

        Assert.True(window.Skip(MinimumBuffer * 2));
        Assert.Equal(MinimumBuffer * 2, window.Position);

        Assert.True(window.Ensure(16));
        Assert.Equal(Pattern(MinimumBuffer * 2, 16), window.Peek(16).ToArray());
    }

    [Fact]
    public void Skip_within_buffer_needs_no_seek()
    {
        StreamWindow window = Window(1000);
        window.Ensure(500);

        Assert.True(window.Skip(200));
        Assert.Equal(200, window.Position);
        Assert.Equal(Pattern(200, 8), window.Peek(8).ToArray());
    }

    [Fact]
    public void Skip_past_end_fails()
    {
        StreamWindow window = Window(500);

        Assert.False(window.Skip(600));
        Assert.Equal(500, window.Position);
        Assert.True(window.AtEnd);
    }

    [Fact]
    public void Skip_of_zero_or_negative_changes_nothing()
    {
        StreamWindow window = Window(100);

        Assert.True(window.Skip(0));
        Assert.True(window.Skip(-5));
        Assert.Equal(0, window.Position);
    }

    /// <summary>
    /// Non-seekable streams are real: copies of locked files and anything not
    /// coming from disk are read this way.
    /// </summary>
    [Fact]
    public void Non_seekable_stream_skips_by_reading()
    {
        using MemoryStream inner = new(Pattern(0, MinimumBuffer * 3), writable: false);
        using ForwardOnlyStream stream = new(inner);
        StreamWindow window = new(stream);

        Assert.True(window.Skip(MinimumBuffer * 2));
        Assert.Equal(MinimumBuffer * 2, window.Position);
        Assert.True(window.Ensure(16));
        Assert.Equal(Pattern(MinimumBuffer * 2, 16), window.Peek(16).ToArray());
    }

    private static StreamWindow Window(int length) =>
        new(new MemoryStream(Pattern(0, length), writable: false));

    /// <summary>Bytes that reveal exactly where they were read from.</summary>
    private static byte[] Pattern(int offset, int count)
    {
        byte[] bytes = new byte[count];
        for (int i = 0; i < count; i++)
        {
            bytes[i] = (byte)((offset + i) * 7);
        }

        return bytes;
    }

    /// <summary>A stream that can only read forward.</summary>
    private sealed class ForwardOnlyStream(Stream inner) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override void Flush() => inner.Flush();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
