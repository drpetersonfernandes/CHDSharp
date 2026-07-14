namespace CHDSharp.Utils;

/// <summary>Extends <see cref="HuffmanDecoder"/> with run-length encoding support for repeated symbol sequences.</summary>
internal class HuffmanDecoderRLE : HuffmanDecoder
{
    /// <summary>Number of remaining identical symbols in the current RLE run.</summary>
    private int rlecount;
    /// <summary>The data value being repeated in the current RLE run.</summary>
    private uint prevdata;

    /// <summary>Initializes a new instance of the <see cref="HuffmanDecoderRLE"/> class.</summary>
    public HuffmanDecoderRLE(uint numcodes, byte maxbits, BitStream bitbuf, ushort[] buffLookup) : base(numcodes, maxbits, bitbuf, buffLookup)
    { }

    /// <summary>Resets the RLE state, clearing any pending run.</summary>
    public void Reset()
    {
        rlecount = 0;
        prevdata = 0;
    }
    /// <summary>Flushes any pending RLE repeat count, resetting the run to zero.</summary>
    public void FlushRLE()
    {
        rlecount = 0;
    }

    /// <summary>Decodes the next Huffman symbol, handling RLE expansion if a run is in progress.</summary>
    /// <returns>The decoded symbol value.</returns>
    public new uint DecodeOne()
    {
        // return RLE data if we still have some
        if (rlecount != 0)
        {
            rlecount--;
            return prevdata;
        }

        // fetch the data and process
        var data = base.DecodeOne();
        if (data < 0x100)
        {
            prevdata += data;
            return prevdata;
        }
        else
        {
            rlecount = CodeToRLECount((int)data);
            rlecount--;
            return prevdata;
        }
    }

    /// <summary>Converts a Huffman symbol to its corresponding RLE repeat count.</summary>
    /// <param name="code">The Huffman symbol value.</param>
    /// <returns>The number of times the symbol should be repeated.</returns>
    public int CodeToRLECount(int code)
    {
        if (code == 0x00)
            return 1;
        if (code <= 0x107)
            return 8 + (code - 0x100);

        return 16 << (code - 0x108);
    }
}
