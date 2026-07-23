namespace CHDSharpEncoder;

public class BitStreamOut
{
    private byte[] _buffer;
    private int _bytePos;
    private uint _bitBuf;
    private int _bitsInBuf;

    public BitStreamOut(int capacityBytes)
    {
        _buffer = new byte[capacityBytes];
        _bytePos = 0;
        _bitBuf = 0;
        _bitsInBuf = 0;
    }

    public int ByteLength => _bytePos;

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
                _buffer[_bytePos++] = (byte)(_bitBuf >> 24);
                _bitBuf <<= 8;
                _bitsInBuf -= 8;
            }

            if (_bitsInBuf + numBits >= 32)
            {
                int rem = Math.Min(32 - _bitsInBuf, numBits);
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
            _buffer[_bytePos++] = (byte)(_bitBuf >> 24);
            _bitBuf <<= 8;
            _bitsInBuf -= 8;
        }
        _bitBuf = 0;
        return _bytePos;
    }

    public byte[] ToArray()
    {
        byte[] result = new byte[_bytePos];
        Array.Copy(_buffer, result, _bytePos);
        return result;
    }

    private void EnsureByte()
    {
        if (_bytePos < _buffer.Length)
            return;

        int newSize = _buffer.Length * 2;
        if (newSize < _buffer.Length + 256)
            newSize = _buffer.Length + 256;
        Array.Resize(ref _buffer, newSize);
    }
}
