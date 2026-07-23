namespace CHDSharpEncoder;

public class BigEndianWriter
{
    private byte[] _buffer;
    private int _pos;

    public BigEndianWriter(int capacity = 256)
    {
        _buffer = new byte[capacity];
        _pos = 0;
    }

    public int Position => _pos;

    public void WriteU8(byte v)
    {
        EnsureCapacity(1);
        _buffer[_pos++] = v;
    }

    public void WriteU16(ushort v)
    {
        EnsureCapacity(2);
        _buffer[_pos]     = (byte)(v >> 8);
        _buffer[_pos + 1] = (byte)v;
        _pos += 2;
    }

    public void WriteU24(uint v)
    {
        EnsureCapacity(3);
        _buffer[_pos]     = (byte)(v >> 16);
        _buffer[_pos + 1] = (byte)(v >> 8);
        _buffer[_pos + 2] = (byte)v;
        _pos += 3;
    }

    public void WriteU32(uint v)
    {
        EnsureCapacity(4);
        _buffer[_pos]     = (byte)(v >> 24);
        _buffer[_pos + 1] = (byte)(v >> 16);
        _buffer[_pos + 2] = (byte)(v >> 8);
        _buffer[_pos + 3] = (byte)v;
        _pos += 4;
    }

    public void WriteU48(ulong v)
    {
        EnsureCapacity(6);
        _buffer[_pos]     = (byte)(v >> 40);
        _buffer[_pos + 1] = (byte)(v >> 32);
        _buffer[_pos + 2] = (byte)(v >> 24);
        _buffer[_pos + 3] = (byte)(v >> 16);
        _buffer[_pos + 4] = (byte)(v >> 8);
        _buffer[_pos + 5] = (byte)v;
        _pos += 6;
    }

    public void WriteU64(ulong v)
    {
        EnsureCapacity(8);
        _buffer[_pos]     = (byte)(v >> 56);
        _buffer[_pos + 1] = (byte)(v >> 48);
        _buffer[_pos + 2] = (byte)(v >> 40);
        _buffer[_pos + 3] = (byte)(v >> 32);
        _buffer[_pos + 4] = (byte)(v >> 24);
        _buffer[_pos + 5] = (byte)(v >> 16);
        _buffer[_pos + 6] = (byte)(v >> 8);
        _buffer[_pos + 7] = (byte)v;
        _pos += 8;
    }

    public void WriteBytes(ReadOnlySpan<byte> data)
    {
        EnsureCapacity(data.Length);
        data.CopyTo(_buffer.AsSpan(_pos));
        _pos += data.Length;
    }

    public void WriteZeroes(int count)
    {
        EnsureCapacity(count);
        Array.Clear(_buffer, _pos, count);
        _pos += count;
    }

    public byte[] ToArray()
    {
        var result = new byte[_pos];
        Array.Copy(_buffer, result, _pos);
        return result;
    }

    public Span<byte> AsSpan() => _buffer.AsSpan(0, _pos);

    private void EnsureCapacity(int bytes)
    {
        int needed = _pos + bytes;
        if (needed <= _buffer.Length)
            return;

        int newSize = _buffer.Length * 2;
        while (newSize < needed)
            newSize *= 2;
        Array.Resize(ref _buffer, newSize);
    }
}
