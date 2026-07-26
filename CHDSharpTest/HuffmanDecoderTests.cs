using CHDSharp.Models.Utils;
using CHDSharp.Utils;

namespace CHDSharp.Tests;

public class HuffmanDecoderTests
{
    [Fact]
    public void Constructor_maxbits_over_24_throws()
    {
        var bs = new BitStream([0xFF], 0, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => new HuffmanDecoder(256, 25, bs));
    }

    [Fact]
    public void Constructor_maxbits_24_does_not_throw()
    {
        var bs = new BitStream([0xFF, 0xFF, 0xFF, 0xFF], 0, 4);
        var decoder = new HuffmanDecoder(256, 24, bs);
        Assert.NotNull(decoder);
    }

    [Fact]
    public void AssignBitStream_replaces_stream()
    {
        var bs1 = new BitStream([0xAA, 0xBB, 0xCC, 0xDD], 0, 4);
        var decoder = new HuffmanDecoder(256, 8, bs1);

        var bs2 = new BitStream([0x11, 0x22, 0x33, 0x44], 0, 4);
        decoder.AssignBitStream(bs2);
        // No exception means success
        Assert.NotNull(decoder);
    }

    [Fact]
    public void ImportTreeRle_simple_flat_tree()
    {
        // Build a simple RLE-encoded tree: 4 codes, all with 2 bits each
        // Each code needs 'numbits' bits (3 bits for maxbits < 8)
        // Code 0: 2 bits, Code 1: 2 bits, Code 2: 2 bits, Code 3: 2 bits
        // Encoded as: 2, 2, 2, 2 (no RLE needed)
        var writer = new BitStreamWrite();
        writer.Write(2, 3); // code 0 = 2 bits
        writer.Write(2, 3); // code 1 = 2 bits
        writer.Write(2, 3); // code 2 = 2 bits
        writer.Write(2, 3); // code 3 = 2 bits

        var data = writer.ToArray();
        var bs = new BitStream(data, 0, data.Length);
        var decoder = new HuffmanDecoder(4, 4, bs);
        var err = decoder.ImportTreeRle();
        Assert.Equal(HuffmanError.HufferrNone, err);
    }

    [Fact]
    public void ImportTreeRle_overflow_returns_error()
    {
        // Create a bitstream that's too short for the expected tree
        var data = new byte[1]; // very short
        var bs = new BitStream(data, 0, data.Length);
        var decoder = new HuffmanDecoder(256, 16, bs);
        var err = decoder.ImportTreeRle();
        Assert.Equal(HuffmanError.HufferrInputBufferTooSmall, err);
    }

    private class BitStreamWrite
    {
        private readonly List<byte> _data = new();
        private uint _buffer;
        private int _bits;

        public void Write(uint value, int numbits)
        {
            _buffer |= value << (32 - _bits - numbits);
            _bits += numbits;
            while (_bits >= 8)
            {
                _data.Add((byte)(_buffer >> 24));
                _buffer <<= 8;
                _bits -= 8;
            }
        }

        public byte[] ToArray()
        {
            if (_bits > 0)
                _data.Add((byte)(_buffer >> 24));
            return _data.ToArray();
        }
    }
}
