namespace CHDSharpEncoder;

public class BitStreamOut
{
    private byte[] _buffer;
    private uint _bitBuf;
    private int _bitsInBuf;

    public BitStreamOut(int capacityBytes)
    {
        _buffer = new byte[capacityBytes];
        ByteLength = 0;
        _bitBuf = 0;
        _bitsInBuf = 0;
    }

    public int ByteLength { get; private set; }

    public void Write(uint value, int numBits)
    {
        if (numBits == 0)
            return;

        value <<= 32 - numBits;

        while (_bitsInBuf + numBits >= 32 && numBits > 0)
        {
            while (_bitsInBuf >= 8)
            {
                EnsureByte();
                _buffer[ByteLength++] = (byte)(_bitBuf >> 24);
                _bitBuf <<= 8;
                _bitsInBuf -= 8;
            }

            if (_bitsInBuf + numBits >= 32)
            {
                var rem = Math.Min(32 - _bitsInBuf, numBits);
                _bitBuf |= value >> _bitsInBuf;
                _bitsInBuf += rem;
                value <<= rem;
                numBits -= rem;
            }
        }

        if (numBits <= 0)
            return;

        _bitBuf |= value >> _bitsInBuf;
        _bitsInBuf += numBits;
    }

    public int Flush()
    {
        while (_bitsInBuf > 0)
        {
            EnsureByte();
            _buffer[ByteLength++] = (byte)(_bitBuf >> 24);
            _bitBuf <<= 8;
            _bitsInBuf -= 8;
        }
        _bitBuf = 0;
        return ByteLength;
    }

    public byte[] ToArray()
    {
        var result = new byte[ByteLength];
        Array.Copy(_buffer, result, ByteLength);
        return result;
    }

    private void EnsureByte()
    {
        if (ByteLength < _buffer.Length)
            return;

        var newSize = _buffer.Length * 2;
        if (newSize < _buffer.Length + 256)
        {
            newSize = _buffer.Length + 256;
        }

        Array.Resize(ref _buffer, newSize);
    }
}
