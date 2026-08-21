using System.Runtime.InteropServices;

namespace CHDSharpEncoder;

/// <summary>
/// Static Huffman encoder matching MAME's <c>huffman_context_base</c> / <c>huffman_encoder</c>
/// (src/lib/util/huffman.cpp): canonical codes over a histogram with weight-scaled tree
/// building (binary search over the weight multiplier so all codes fit in
/// <c>maxBits</c>), and the Huffman-encoded tree export used by the 8-bit
/// <c>huff</c> codec (MAME's <c>export_tree_huffman</c>).
/// </summary>
internal sealed class HuffmanEncoder
{
    private readonly int _numCodes;
    private readonly int _maxBits;
    private readonly int[] _histogram;
    private readonly HuffmanNode[] _nodes;

    /// <summary>Creates a Huffman encoder over a fixed-size symbol alphabet.</summary>
    /// <param name="numCodes">Alphabet size (e.g. 256 for the huff codec, 24 for the small tree).</param>
    /// <param name="maxBits">Maximum code length in bits.</param>
    public HuffmanEncoder(int numCodes, int maxBits)
    {
        if (maxBits > 24)
            throw new ArgumentOutOfRangeException(nameof(maxBits));

        _numCodes = numCodes;
        _maxBits = maxBits;
        _histogram = new int[numCodes];
        _nodes = new HuffmanNode[numCodes * 2];
        NumBits = new int[numCodes];
        Codes = new uint[numCodes];
    }

    /// <summary>Gets the canonical Huffman code for each symbol (valid after <see cref="BuildTree()"/>).</summary>
    public uint[] Codes { get; }

    /// <summary>Gets the number of bits of each symbol's canonical code (valid after <see cref="BuildTree()"/>).</summary>
    public int[] NumBits { get; }

    /// <summary>Resets the symbol frequency histogram.</summary>
    public void ResetHistogram()
    {
        Array.Clear(_histogram);
    }

    /// <summary>Increments the frequency count of <paramref name="symbol"/>.</summary>
    public void CountSymbol(uint symbol)
    {
        if (symbol < _numCodes)
        {
            _histogram[symbol]++;
        }
    }

    /// <summary>
    /// Computes an optimal tree from the histogram and assigns canonical codes
    /// (MAME's <c>compute_tree_from_histo</c>). The tree state left by the final
    /// <c>build_tree</c> call is used directly, exactly like MAME.
    /// </summary>
    public void BuildTree()
    {
        var totalData = 0;
        for (var i = 0; i < _numCodes; i++)
        {
            totalData += _histogram[i];
        }

        if (totalData == 0)
        {
            Array.Clear(NumBits);
            return;
        }

        // binary search for the largest weight multiplier whose tree fits in _maxBits
        var lowerWeight = 0;
        var upperWeight = totalData * 2;
        while (true)
        {
            var curWeight = (upperWeight + lowerWeight) / 2;
            var curMaxBits = BuildTree(totalData, curWeight);

            if (curMaxBits <= _maxBits)
            {
                lowerWeight = curWeight;
                if (curWeight == totalData || upperWeight - lowerWeight <= 1)
                    break;
            }
            else
            {
                upperWeight = curWeight;
            }
        }

        AssignCanonicalCodes();
    }

    /// <summary>
    /// Writes the tree to <paramref name="bs"/> Huffman-encoded with a 24-symbol/6-bit
    /// small tree (MAME's <c>export_tree_huffman</c>), which the decoder reconstructs via
    /// <c>import_tree_huffman</c>. The 8-bit huff codec uses this form.
    /// </summary>
    public void ExportTreeHuffman(BitStreamOut bs)
    {
        // RLE-compress the code lengths: single occurrences as (length + 1),
        // runs as an RLE token (0) followed by (run - 2)
        var rleData = new List<byte>(_numCodes);
        var rleLengths = new List<int>();
        var last = -1;
        var repCount = 0;

        for (var curCode = 0; curCode < _numCodes; curCode++)
        {
            var newVal = NumBits[curCode];
            if (newVal != last && repCount > 0)
            {
                if (repCount == 1)
                {
                    rleData.Add((byte)(last + 1));
                }
                else
                {
                    rleData.Add(0);
                    rleLengths.Add(repCount - 2);
                }
            }

            if (newVal == last)
            {
                repCount++;
            }
            else
            {
                rleData.Add((byte)(newVal + 1));
                last = newVal;
                repCount = 0;
            }
        }

        if (repCount > 0)
        {
            if (repCount == 1)
            {
                rleData.Add((byte)(last + 1));
            }
            else
            {
                rleData.Add(0);
                rleLengths.Add(repCount - 2);
            }
        }

        // compute an optimal tree for the small tree
        var smallHuff = new HuffmanEncoder(24, 6);
        foreach (var data in rleData)
            smallHuff.CountSymbol(data);
        smallHuff.BuildTree();

        // determine the first and last non-zero nodes
        int firstNonZero = 31, lastNonZero = 0;
        for (var index = 1; index < 24; index++)
        {
            if (smallHuff.NumBits[index] != 0)
            {
                if (firstNonZero == 31)
                {
                    firstNonZero = index;
                }

                lastNonZero = index;
            }
        }

        // clamp first non-zero to be 8 at a maximum
        firstNonZero = Math.Min(firstNonZero, 8);

        // output the small tree: node 0's length, first non-zero, the lengths of the
        // following nodes, terminated by a 7
        bs.Write((uint)smallHuff.NumBits[0], 3);
        bs.Write((uint)(firstNonZero - 1), 3);
        for (var index = firstNonZero; index <= lastNonZero; index++)
            bs.Write((uint)smallHuff.NumBits[index], 3);
        bs.Write(7, 3);

        // the maximum length of an RLE count
        var rleFullBits = 0;
        for (var temp = _numCodes - 9; temp != 0; temp >>= 1)
        {
            rleFullBits++;
        }

        // encode the RLE data
        var lengthIndex = 0;
        foreach (var data in rleData)
        {
            smallHuff.Encode(bs, data);

            // an RLE token is followed by the run length
            if (data == 0)
            {
                var count = rleLengths[lengthIndex++];
                if (count < 7)
                {
                    bs.Write((uint)count, 3);
                }
                else
                {
                    bs.Write(7, 3);
                    bs.Write((uint)(count - 7), rleFullBits);
                }
            }
        }
    }

    /// <summary>
    /// Writes the tree to <paramref name="bs"/> RLE-encoded (MAME's <c>export_tree_rle</c>,
    /// src/lib/util/huffman.cpp): code lengths are emitted with a repeat escape; the number
    /// of bits per entry depends on <c>maxBits</c> (5 for ≥16). Used by the avhu codec's
    /// delta-RLE video/audio trees.
    /// </summary>
    public void ExportTreeRle(BitStreamOut bs)
    {
        // bits per entry depends on the maxbits (huffman.cpp:207-213)
        var numBits = _maxBits >= 16 ? 5 : _maxBits >= 8 ? 4 : 3;

        // RLE encode the lengths (huffman.cpp:216-236)
        var lastVal = ~0;
        var repCount = 0;
        for (var curCode = 0; curCode < _numCodes; curCode++)
        {
            var newVal = NumBits[curCode];
            if (newVal == lastVal)
            {
                repCount++;
            }
            else
            {
                if (repCount != 0)
                {
                    WriteRleTreeBits(bs, lastVal, repCount, numBits);
                }

                lastVal = newVal;
                repCount = 1;
            }
        }

        WriteRleTreeBits(bs, lastVal, repCount, numBits);
    }

    /// <summary>Writes one run of equal code lengths (MAME's <c>write_rle_tree_bits</c>, huffman.cpp:468).</summary>
    private static void WriteRleTreeBits(BitStreamOut bs, int value, int repCount, int numBits)
    {
        while (repCount > 0)
        {
            // a 1 is written twice as it is the escape code
            if (value == 1)
            {
                bs.Write(1, numBits);
                bs.Write(1, numBits);
                repCount--;
            }
            else if (repCount <= 2)
            {
                // two or fewer in a row are written raw
                bs.Write((uint)value, numBits);
                repCount--;
            }
            else
            {
                // longer runs use an escape triple: 1, value, extra reps beyond 3
                var curReps = Math.Min(repCount - 3, (1 << numBits) - 1);
                bs.Write(1, numBits);
                bs.Write((uint)value, numBits);
                bs.Write((uint)curReps, numBits);
                repCount -= curReps + 3;
            }
        }
    }

    /// <summary>Writes the canonical code of <paramref name="symbol"/> to <paramref name="bs"/>.</summary>
    public void Encode(BitStreamOut bs, uint symbol)
    {
        if (symbol >= _numCodes)
            return;

        if (NumBits[symbol] > 0)
            bs.Write(Codes[symbol], NumBits[symbol]);
    }

    /// <summary>Builds a Huffman tree with the given total weight; returns the maximum code length.</summary>
    private int BuildTree(int totalData, int totalWeight)
    {
        // reset all nodes
        Array.Clear(_nodes);
        var list = new List<int>(_numCodes * 2);

        for (var curCode = 0; curCode < _numCodes; curCode++)
        {
            if (_histogram[curCode] != 0)
            {
                list.Add(curCode);
                _nodes[curCode].Count = _histogram[curCode];
                _nodes[curCode].Code = curCode;
                _nodes[curCode].Parent = -1;

                // scale the weight by the current effective length, ensuring it does not go to 0
                var weight = (long)_histogram[curCode] * totalWeight / totalData;
                _nodes[curCode].Weight = weight == 0 ? 1 : (int)weight;
            }
        }

        // sort by weight descending, then by code index ascending (MAME's tree_node_compare)
        list.Sort((a, b) =>
        {
            var weightCompare = _nodes[b].Weight.CompareTo(_nodes[a].Weight);
            return weightCompare != 0 ? weightCompare : a.CompareTo(b);
        });

        // build the tree by merging the two lowest-weight nodes
        var nextAlloc = _numCodes;
        while (list.Count > 1)
        {
            var node1 = list[^1];
            list.RemoveAt(list.Count - 1);
            var node0 = list[^1];
            list.RemoveAt(list.Count - 1);

            var newNode = nextAlloc++;
            _nodes[newNode].Parent = -1;
            _nodes[newNode].Weight = _nodes[node0].Weight + _nodes[node1].Weight;
            _nodes[node0].Parent = newNode;
            _nodes[node1].Parent = newNode;

            // insert before the first item with strictly smaller weight (equal weights stay first)
            var insertPos = 0;
            while (insertPos < list.Count && _nodes[newNode].Weight <= _nodes[list[insertPos]].Weight)
            {
                insertPos++;
            }

            list.Insert(insertPos, newNode);
        }

        // compute the number of bits in each code
        var maxBits = 0;
        Array.Clear(NumBits);
        for (var curCode = 0; curCode < _numCodes; curCode++)
        {
            if (_histogram[curCode] != 0)
            {
                var numbits = 0;
                for (var node = curCode; _nodes[node].Parent >= 0; node = _nodes[node].Parent)
                {
                    numbits++;
                }

                if (numbits == 0)
                {
                    numbits = 1;
                }

                NumBits[curCode] = numbits;
                maxBits = Math.Max(maxBits, numbits);
            }
        }

        return maxBits;
    }

    /// <summary>Assigns canonical codes from the code lengths (MAME's <c>assign_canonical_codes</c>).</summary>
    private void AssignCanonicalCodes()
    {
        var bitHisto = new int[33];
        for (var curCode = 0; curCode < _numCodes; curCode++)
        {
            var numbits = NumBits[curCode];
            if (numbits is > 0 and <= 32)
            {
                bitHisto[numbits]++;
            }
        }

        uint curStart = 0;
        for (var codeLen = 32; codeLen > 0; codeLen--)
        {
            var nextStart = (curStart + (uint)bitHisto[codeLen]) >> 1;
            bitHisto[codeLen] = (int)curStart;
            curStart = nextStart;
        }

        for (var curCode = 0; curCode < _numCodes; curCode++)
        {
            if (NumBits[curCode] > 0)
            {
                Codes[curCode] = (uint)bitHisto[NumBits[curCode]]++;
            }
        }
    }

    [StructLayout(LayoutKind.Auto)]
    private struct HuffmanNode
    {
        public int Weight;
        public int Count;
        public int Code;
        public int Parent;
    }
}