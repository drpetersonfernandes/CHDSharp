namespace CHDSharpTestGen;

/// <summary>Deterministic pseudo-random generator (xorshift64*) so the corpus is reproducible.</summary>
internal sealed class DetRng
{
    private ulong _state;

    public DetRng(ulong seed)
    {
        _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
    }

    public ulong NextU64()
    {
        var x = _state;
        x ^= x >> 12;
        x ^= x << 25;
        x ^= x >> 27;
        _state = x;
        return x * 0x2545F4914F6CDD1DUL;
    }

    public byte NextByte()
    {
        return (byte)(NextU64() >> 56);
    }

    public int Next(int maxExclusive)
    {
        return (int)(NextU64() % (uint)maxExclusive);
    }

    public void Fill(Span<byte> buffer)
    {
        var i = 0;
        while (i + 8 <= buffer.Length)
        {
            var v = NextU64();
            for (var b = 0; b < 8; b++)
            {
                buffer[i++] = (byte)(v >> (b * 8));
            }
        }
        while (i < buffer.Length)
        {
            buffer[i++] = NextByte();
        }
    }
}
