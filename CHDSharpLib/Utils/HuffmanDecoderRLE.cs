namespace CHDSharp.Utils;

/// <summary>Extends <see cref="HuffmanDecoder"/> with run-length encoding support for repeated symbol sequences.</summary>
internal class HuffmanDecoderRle : HuffmanDecoder
{
    private int _rlecount;
    private uint _prevdata;

    /// <summary>Initializes a new instance of the <see cref="HuffmanDecoderRle"/> class.</summary>
    public HuffmanDecoderRle(uint numcodes, byte maxbits, BitStream bitbuf, ushort[] buffLookup) : base(numcodes, maxbits, bitbuf, buffLookup)
    { }

    /// <summary>Resets the RLE state, clearing any pending run.</summary>
    public void Reset()
    {
        _rlecount = 0;
        _prevdata = 0;
    }
    /// <summary>Flushes any pending RLE repeat count, resetting the run to zero.</summary>
    public void FlushRle()
    {
        _rlecount = 0;
    }

    /// <summary>Decodes the next Huffman symbol, handling RLE expansion if a run is in progress.</summary>
    /// <returns>The decoded symbol value.</returns>
    public new uint DecodeOne()
    {
        // return RLE data if we still have some
        if (_rlecount != 0)
        {
            _rlecount--;
            return _prevdata;
        }

        // fetch the data and process
        var data = base.DecodeOne();
        if (data < 0x100)
        {
            _prevdata += data;
            return _prevdata;
        }
        else
        {
            _rlecount = CodeToRleCount((int)data);
            _rlecount--;
            return _prevdata;
        }
    }

    /// <summary>Converts a Huffman symbol to its corresponding RLE repeat count.</summary>
    /// <param name="code">The Huffman symbol value.</param>
    /// <returns>The number of times the symbol should be repeated.</returns>
    public int CodeToRleCount(int code)
    {
        if (code == 0x00)
            return 1;
        if (code <= 0x107)
            return 8 + (code - 0x100);

        return 16 << (code - 0x108);
    }
}
