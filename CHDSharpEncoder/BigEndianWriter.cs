namespace CHDSharpEncoder;

internal class BigEndianWriter
{
    private byte[] _buffer;

    internal BigEndianWriter(int capacity = 256)
    {
        _buffer = new byte[capacity];
        Position = 0;
    }

    internal int Position { get; private set; }

    internal void WriteU8(byte v)
    {
        EnsureCapacity(1);
        _buffer[Position++] = v;
    }

    internal void WriteU16(ushort v)
    {
        EnsureCapacity(2);
        _buffer[Position] = (byte)(v >> 8);
        _buffer[Position + 1] = (byte)v;
        Position += 2;
    }

    internal void WriteU24(uint v)
    {
        EnsureCapacity(3);
        _buffer[Position] = (byte)(v >> 16);
        _buffer[Position + 1] = (byte)(v >> 8);
        _buffer[Position + 2] = (byte)v;
        Position += 3;
    }

    internal void WriteU32(uint v)
    {
        EnsureCapacity(4);
        _buffer[Position] = (byte)(v >> 24);
        _buffer[Position + 1] = (byte)(v >> 16);
        _buffer[Position + 2] = (byte)(v >> 8);
        _buffer[Position + 3] = (byte)v;
        Position += 4;
    }

    internal void WriteU48(ulong v)
    {
        EnsureCapacity(6);
        _buffer[Position] = (byte)(v >> 40);
        _buffer[Position + 1] = (byte)(v >> 32);
        _buffer[Position + 2] = (byte)(v >> 24);
        _buffer[Position + 3] = (byte)(v >> 16);
        _buffer[Position + 4] = (byte)(v >> 8);
        _buffer[Position + 5] = (byte)v;
        Position += 6;
    }

    internal void WriteU64(ulong v)
    {
        EnsureCapacity(8);
        _buffer[Position] = (byte)(v >> 56);
        _buffer[Position + 1] = (byte)(v >> 48);
        _buffer[Position + 2] = (byte)(v >> 40);
        _buffer[Position + 3] = (byte)(v >> 32);
        _buffer[Position + 4] = (byte)(v >> 24);
        _buffer[Position + 5] = (byte)(v >> 16);
        _buffer[Position + 6] = (byte)(v >> 8);
        _buffer[Position + 7] = (byte)v;
        Position += 8;
    }

    internal void WriteBytes(ReadOnlySpan<byte> data)
    {
        EnsureCapacity(data.Length);
        data.CopyTo(_buffer.AsSpan(Position));
        Position += data.Length;
    }

    internal void WriteZeroes(int count)
    {
        EnsureCapacity(count);
        Array.Clear(_buffer, Position, count);
        Position += count;
    }

    internal byte[] ToArray()
    {
        var result = new byte[Position];
        Array.Copy(_buffer, result, Position);
        return result;
    }

    internal Span<byte> AsSpan()
    {
        return _buffer.AsSpan(0, Position);
    }

    private void EnsureCapacity(int bytes)
    {
        var needed = Position + bytes;
        if (needed <= _buffer.Length)
            return;

        var newSize = _buffer.Length * 2;
        while (newSize < needed)
        {
            newSize *= 2;
        }

        Array.Resize(ref _buffer, newSize);
    }
}
