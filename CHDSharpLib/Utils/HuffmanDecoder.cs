using CHDSharp.Models.Utils;

namespace CHDSharp.Utils;

/// <summary>
/// Represents a single node in a Huffman tree with its encoded bit pattern and bit length.
/// </summary>
internal class NodeT
{
    /// <summary>Bits used to encode the node.</summary>
    internal uint Bits;

    /// <summary>Number of bits needed for this node.</summary>
    internal byte Numbits;
}

/// <summary>
/// Decodes Huffman-encoded bitstreams using a lookup-table-based approach.
/// Supports importing trees from RLE and Huffman-encoded formats.
/// </summary>
internal class HuffmanDecoder
{
    /// <summary>Number of total codes being processed.</summary>
    private readonly uint _numcodes;

    /// <summary>Maximum bits per code.</summary>
    private readonly byte _maxbits;

    /// <summary>Pointer to the lookup table.</summary>
    private readonly ushort[] _lookup;

    /// <summary>Array of nodes.</summary>
    private readonly NodeT[] _huffnode;

    /// <summary>Bit stream used to read encoded data.</summary>
    private BitStream _bitbuf;

    /// <summary>
    /// Packs a code value and its bit count into a single lookup-table entry.
    /// </summary>
    /// <param name="code">The code value to encode.</param>
    /// <param name="bits">The number of bits used by the code.</param>
    /// <returns>A packed lookup-table entry with the code in the upper bits and bit count in the lower 5 bits.</returns>
    private static uint MAKE_LOOKUP(uint code, uint bits)
    {
        return (code << 5) | (bits & 0x1f);
    }

    /// <summary>
    /// Initializes a new Huffman decoder with the specified capacity and bit stream.
    /// </summary>
    /// <param name="numcodes">Number of total codes to process.</param>
    /// <param name="maxbits">Maximum bits per code (must not exceed 24).</param>
    /// <param name="bitbuf">The bit stream to read encoded data from.</param>
    /// <param name="buffLookup">Optional pre-allocated lookup table buffer.</param>
    public HuffmanDecoder(uint numcodes, byte maxbits, BitStream bitbuf, ushort[]? buffLookup = null)
    {
        /* limit to 24 bits */
        if (maxbits > 24)
        {
            _lookup = null!;
            _huffnode = null!;
            _bitbuf = null!;
            return;
        }

        _numcodes = numcodes;
        _maxbits = maxbits;

        _lookup = buffLookup ?? (new ushort[(1 << maxbits)]);

        _huffnode = new NodeT[numcodes];

        for (var i = 0; i < numcodes; i++)
        {
            _huffnode[i] = new NodeT();
        }

        _bitbuf = bitbuf;
    }

    /// <summary>
    /// Assigns a new bit stream to the decoder, replacing any existing one.
    /// </summary>
    /// <param name="bitbufReplace">The replacement bit stream.</param>
    public void AssignBitStream(BitStream bitbufReplace)
    {
        _bitbuf = bitbufReplace;
    }

    /// <summary>
    /// Decodes a single code from the Huffman stream using the lookup table.
    /// </summary>
    /// <returns>The decoded value.</returns>
    public uint DecodeOne()
    {
        /* peek ahead to get maxbits worth of data */
        var bits = _bitbuf.Peek(_maxbits);

        /* look it up, then remove the actual number of bits for this code */
        uint entry = _lookup[bits];
        _bitbuf.Remove((int)(entry & 0x1f));

        /* return the value */
        return (entry >> 5);
    }

    /// <summary>
    /// Imports a Huffman tree from an RLE-encoded bitstream.
    /// </summary>
    /// <returns>
        /// <c>HufferrNone</c> on success;
        /// <c>HufferrInvalidData</c> if the data is malformed;
        /// <c>HufferrInputBufferTooSmall</c> on overflow.
        /// </returns>
        public HuffmanError ImportTreeRle()
    {
        int curnode;

        var numbits = _maxbits switch
        {
            /* bits per entry depends on the maxbits */
            >= 16 => 5,
            >= 8 => 4,
            _ => 3
        };

        /* loop until we read all the nodes */
        for (curnode = 0; curnode < _numcodes;)
        {
            /* a non-one value is just raw */
            var nodebits = (int)_bitbuf.Read(numbits);
            if (nodebits != 1)
            {
                _huffnode[curnode++].Numbits = (byte)nodebits;
            }
            /* a one value is an escape code */
            else
            {
                /* a double 1 is just a single 1 */
                nodebits = (int)_bitbuf.Read(numbits);
                if (nodebits == 1)
                {
                    _huffnode[curnode++].Numbits = (byte)nodebits;
                }
                /* otherwise, we need one for value for the repeat count */
                else
                {
                    var repcount = (int)_bitbuf.Read(numbits) + 3;
                    if (repcount + curnode > _numcodes)
                        return HuffmanError.HufferrInvalidData;

                    while (repcount-- != 0)
                    {
                        _huffnode[curnode++].Numbits = (byte)nodebits;
                    }
                }
            }
        }

        /* make sure we ended up with the right number */
        if (curnode != _numcodes)
            return HuffmanError.HufferrInvalidData;

        /* assign canonical codes for all nodes based on their code lengths */
        var error = AssignCanonicalCodes();
        if (error != HuffmanError.HufferrNone)
            return error;

        /* build the lookup table */
        BuildLookupTable();

        /* determine final input length and report errors */
        return _bitbuf.Overflow() ? HuffmanError.HufferrInputBufferTooSmall : HuffmanError.HufferrNone;
    }

    /// <summary>
    /// Imports a Huffman tree that is itself Huffman-encoded from the bitstream.
    /// </summary>
    /// <returns>
        /// <c>HufferrNone</c> on success;
        /// <c>HufferrInvalidData</c> if the data is malformed;
        /// <c>HufferrInputBufferTooSmall</c> on overflow.
        /// </returns>
        public HuffmanError ImportTreeHuffman()
    {
        var last = 0;
        var count = 0;
        int index;
        uint curcode;
        byte rlefullbits = 0;

        /* start by parsing the lengths for the small tree */
        var smallhuff = new HuffmanDecoder(24, 6, _bitbuf);
        smallhuff._huffnode[0].Numbits = (byte)_bitbuf.Read(3);
        var start = (int)_bitbuf.Read(3) + 1;
        for (index = 1; index < 24; index++)
        {
            if (index < start || count == 7)

            {
                smallhuff._huffnode[index].Numbits = 0;
            }
            else
            {
                count = (int)_bitbuf.Read(3);
                smallhuff._huffnode[index].Numbits = (byte)(count == 7 ? 0 : count);
            }
        }

        /* then regenerate the tree */
        var error = smallhuff.AssignCanonicalCodes();
        if (error != HuffmanError.HufferrNone)
            return error;

        smallhuff.BuildLookupTable();

        /* determine the maximum length of an RLE count */
        var temp = _numcodes - 9;
        while (temp != 0)
        {
            temp >>= 1;
            rlefullbits++;
        }

        /* now process the rest of the data */
        for (curcode = 0; curcode < _numcodes;)
        {
            var value = (int)smallhuff.DecodeOne();
            if (value != 0)
            {
                _huffnode[curcode++].Numbits = (byte)(last = value - 1);
            }
            else
            {
                count = (int)_bitbuf.Read(3) + 2;
                if (count == 7 + 2)
                {
                    count += (int)_bitbuf.Read(rlefullbits);
                }

                for (; count != 0 && curcode < _numcodes; count--)
                {
                    _huffnode[curcode++].Numbits = (byte)last;
                }
            }
        }

        /* make sure we ended up with the right number */
        if (curcode != _numcodes)
            return HuffmanError.HufferrInvalidData;

        /* assign canonical codes for all nodes based on their code lengths */
        error = AssignCanonicalCodes();
        if (error != HuffmanError.HufferrNone)
            return error;

        /* build the lookup table */
        BuildLookupTable();

        /* determine final input length and report errors */
        return _bitbuf.Overflow() ? HuffmanError.HufferrInputBufferTooSmall : HuffmanError.HufferrNone;
    }

    /// <summary>
    /// Assigns canonical Huffman codes to all nodes based on their bit lengths.
    /// </summary>
    /// <returns>
        /// <c>HufferrNone</c> on success;
        /// <c>HufferrInternalInconsistency</c> if the code lengths are inconsistent.
    /// </returns>
    private HuffmanError AssignCanonicalCodes()
    {
        uint curcode;
        int codelen;
        uint curstart = 0;
        /* build up a histogram of bit lengths */
        var bithisto = new uint[33];
        for (curcode = 0; curcode < _numcodes; curcode++)
        {
            var node = _huffnode[curcode];
            if (node.Numbits > _maxbits)
                return HuffmanError.HufferrInternalInconsistency;

            if (node.Numbits <= 32)
            {
                bithisto[node.Numbits]++;
            }
        }

        /* for each code length, determine the starting code number */
        for (codelen = 32; codelen > 0; codelen--)
        {
            var nextstart = (curstart + bithisto[codelen]) >> 1;
            if (codelen != 1 && nextstart * 2 != curstart + bithisto[codelen])
                return HuffmanError.HufferrInternalInconsistency;

            bithisto[codelen] = curstart;
            curstart = nextstart;
        }


        /* now assign canonical codes */
        for (curcode = 0; curcode < _numcodes; curcode++)
        {
            var node = _huffnode[curcode];
            if (node.Numbits > 0)
            {
                node.Bits = bithisto[node.Numbits]++;
            }
        }

        return HuffmanError.HufferrNone;
    }

    /// <summary>
    /// Builds a lookup table mapping bit patterns to decoded values for fast decoding.
    /// </summary>
    private void BuildLookupTable()
    {
        uint curcode;
        /* iterate over all codes */
        for (curcode = 0; curcode < _numcodes; curcode++)
        {
            /* process all nodes which have non-zero bits */
            var node = _huffnode[curcode];
            if (node.Numbits > 0)
            {
                /* set up the entry */
                var value = MAKE_LOOKUP(curcode, node.Numbits);
                /* fill all matching entries */
                var shift = _maxbits - node.Numbits;
                var dest = node.Bits << shift;
                var destend = ((node.Bits + 1) << shift) - 1;
                while (dest <= destend)
                {
                    _lookup[dest++] = (ushort)value;
                }
            }
        }
    }
}
