namespace CHDSharpEncoder;

public class Sha1
{
    private uint _h0 = 0x67452301;
    private uint _h1 = 0xEFCDAB89;
    private uint _h2 = 0x98BADCFE;
    private uint _h3 = 0x10325476;
    private uint _h4 = 0xC3D2E1F0;

    private ulong _totalBits;
    private readonly byte[] _block = new byte[64];
    private int _blockPos;

    public void Reset()
    {
        _h0 = 0x67452301;
        _h1 = 0xEFCDAB89;
        _h2 = 0x98BADCFE;
        _h3 = 0x10325476;
        _h4 = 0xC3D2E1F0;
        _totalBits = 0;
        _blockPos = 0;
    }

    public void Append(byte[] data, int offset, int length)
    {
        _totalBits += (ulong)length * 8;

        for (int i = 0; i < length; i++)
        {
            _block[_blockPos++] = data[offset + i];
            if (_blockPos == 64)
            {
                ProcessBlock(_block);
                _blockPos = 0;
            }
        }
    }

    public byte[] Finish()
    {
        byte[] digest = new byte[20];

        _block[_blockPos++] = 0x80;
        if (_blockPos > 56)
        {
            while (_blockPos < 64)
                _block[_blockPos++] = 0;
            ProcessBlock(_block);
            _blockPos = 0;
        }

        while (_blockPos < 56)
            _block[_blockPos++] = 0;

        WriteU64BE(_block, 56, _totalBits);
        ProcessBlock(_block);

        WriteU32BE(digest, 0, _h0);
        WriteU32BE(digest, 4, _h1);
        WriteU32BE(digest, 8, _h2);
        WriteU32BE(digest, 12, _h3);
        WriteU32BE(digest, 16, _h4);

        return digest;
    }

    public static byte[] Compute(byte[] data)
    {
        var sha = new Sha1();
        sha.Append(data, 0, data.Length);
        return sha.Finish();
    }

    private void ProcessBlock(byte[] block)
    {
        uint[] w = new uint[80];

        for (int t = 0; t < 16; t++)
        {
            w[t] = ((uint)block[t * 4] << 24) |
                   ((uint)block[t * 4 + 1] << 16) |
                   ((uint)block[t * 4 + 2] << 8) |
                   (uint)block[t * 4 + 3];
        }

        for (int t = 16; t < 80; t++)
        {
            uint temp = w[t - 3] ^ w[t - 8] ^ w[t - 14] ^ w[t - 16];
            w[t] = (temp << 1) | (temp >> 31);
        }

        uint a = _h0, b = _h1, c = _h2, d = _h3, e = _h4;

        for (int t = 0; t < 80; t++)
        {
            uint k, f;
            if (t < 20)
            {
                f = (b & c) | (~b & d);
                k = 0x5A827999;
            }
            else if (t < 40)
            {
                f = b ^ c ^ d;
                k = 0x6ED9EBA1;
            }
            else if (t < 60)
            {
                f = (b & c) | (b & d) | (c & d);
                k = 0x8F1BBCDC;
            }
            else
            {
                f = b ^ c ^ d;
                k = 0xCA62C1D6;
            }

            uint temp = ((a << 5) | (a >> 27)) + f + e + k + w[t];
            e = d;
            d = c;
            c = (b << 30) | (b >> 2);
            b = a;
            a = temp;
        }

        _h0 += a;
        _h1 += b;
        _h2 += c;
        _h3 += d;
        _h4 += e;
    }

    private static void WriteU32BE(byte[] buffer, int offset, uint value)
    {
        buffer[offset]     = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static void WriteU64BE(byte[] buffer, int offset, ulong value)
    {
        buffer[offset]     = (byte)(value >> 56);
        buffer[offset + 1] = (byte)(value >> 48);
        buffer[offset + 2] = (byte)(value >> 40);
        buffer[offset + 3] = (byte)(value >> 32);
        buffer[offset + 4] = (byte)(value >> 24);
        buffer[offset + 5] = (byte)(value >> 16);
        buffer[offset + 6] = (byte)(value >> 8);
        buffer[offset + 7] = (byte)value;
    }
}
