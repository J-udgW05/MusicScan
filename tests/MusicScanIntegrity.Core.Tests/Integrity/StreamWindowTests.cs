using MusicScanIntegrity.Core.Integrity;
using Xunit;

namespace MusicScanIntegrity.Core.Tests.Integrity;

/// <summary>
/// Скользящее окно над потоком — то, через что читают все разборщики.
/// </summary>
/// <remarks>
/// Ошибка здесь не выглядит ошибкой окна: она выглядит повреждённым файлом.
/// Поэтому проверяется само окно, а не только вердикты поверх него.
/// </remarks>
public sealed class StreamWindowTests
{
    /// <summary>Наименьший буфер, который окно себе позволяет.</summary>
    private const int MinimumBuffer = 4096;

    [Fact]
    public void Позиция_считается_по_пройденным_байтам()
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
    public void Просмотр_вперёд_не_сдвигает_позицию()
    {
        StreamWindow window = Window(1000);
        window.Ensure(10);

        Assert.Equal(Pattern(0, 10), window.Peek(10).ToArray());
        Assert.Equal(Pattern(0, 10), window.Peek(10).ToArray());
        Assert.Equal(0, window.Position);
    }

    [Fact]
    public void Конец_потока_виден_по_отказу_добрать_байты()
    {
        StreamWindow window = Window(100);

        Assert.True(window.Ensure(100));
        Assert.False(window.Ensure(101));

        window.Advance(100);
        Assert.True(window.AtEnd);
    }

    /// <summary>
    /// Кадр длиннее буфера — обычное дело у файлов с высоким битрейтом.
    /// </summary>
    [Fact]
    public void Буфер_растёт_под_запрос_длиннее_себя()
    {
        StreamWindow window = Window(MinimumBuffer * 5);
        int wanted = MinimumBuffer * 3;

        Assert.True(window.Ensure(wanted));
        Assert.Equal(Pattern(0, wanted), window.Peek(wanted).ToArray());
    }

    /// <summary>
    /// Главное свойство сдвига: байты после него — те же самые.
    /// </summary>
    /// <remarks>
    /// Когда свободного места в хвосте буфера не осталось, окно сдвигает
    /// непрочитанный остаток к началу. Шаг подобран так, чтобы за проход
    /// сдвиг случился много раз: именно на нём теряются байты, если сдвиг
    /// считает длины неверно.
    /// </remarks>
    [Fact]
    public void Сдвиг_остатка_к_началу_не_теряет_байты()
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
    public void Пропуск_переносит_позицию_за_прочитанное()
    {
        StreamWindow window = Window(MinimumBuffer * 4);

        Assert.True(window.Skip(MinimumBuffer * 2));
        Assert.Equal(MinimumBuffer * 2, window.Position);

        Assert.True(window.Ensure(16));
        Assert.Equal(Pattern(MinimumBuffer * 2, 16), window.Peek(16).ToArray());
    }

    [Fact]
    public void Пропуск_внутри_уже_прочитанного_обходится_без_перемотки()
    {
        StreamWindow window = Window(1000);
        window.Ensure(500);

        Assert.True(window.Skip(200));
        Assert.Equal(200, window.Position);
        Assert.Equal(Pattern(200, 8), window.Peek(8).ToArray());
    }

    [Fact]
    public void Пропуск_за_конец_файла_не_удаётся()
    {
        StreamWindow window = Window(500);

        Assert.False(window.Skip(600));
        Assert.Equal(500, window.Position);
        Assert.True(window.AtEnd);
    }

    [Fact]
    public void Пропуск_нуля_и_отрицательного_ничего_не_меняет()
    {
        StreamWindow window = Window(100);

        Assert.True(window.Skip(0));
        Assert.True(window.Skip(-5));
        Assert.Equal(0, window.Position);
    }

    /// <summary>
    /// Поток без перемотки — не выдуманный случай: так читаются копии занятых
    /// файлов и всё, что приходит не с диска.
    /// </summary>
    [Fact]
    public void Поток_без_перемотки_пропускает_байты_чтением()
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

    /// <summary>Байты, по которым видно, откуда именно они прочитаны.</summary>
    private static byte[] Pattern(int offset, int count)
    {
        byte[] bytes = new byte[count];
        for (int i = 0; i < count; i++)
        {
            bytes[i] = (byte)((offset + i) * 7);
        }

        return bytes;
    }

    /// <summary>Поток, который умеет только читать вперёд.</summary>
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
