namespace CHDSharpEncoder;

public class Huffman16_8
{
    public const int NUM_CODES = 16;
    public const int MAX_BITS = 8;

    private readonly int[] _histogram = new int[NUM_CODES];

    public int[] NumBits { get; } = new int[NUM_CODES];

    public uint[] Codes { get; } = new uint[NUM_CODES];

    public void ResetHistogram()
    {
        Array.Clear(_histogram);
    }

    public void CountSymbol(uint symbol)
    {
        if (symbol < NUM_CODES)
        {
            _histogram[symbol]++;
        }
    }

    public void BuildTree()
    {
        var totalData = 0;
        for (var i = 0; i < NUM_CODES; i++)
        {
            totalData += _histogram[i];
        }

        Array.Clear(Codes);

        if (totalData == 0)
        {
            Array.Clear(NumBits);
            return;
        }

        Array.Clear(NumBits);

        var lower = 0;
        var upper = totalData * 2;
        var bestWeight = totalData;

        for (var iter = 0; iter < 32; iter++)
        {
            var curWeight = (lower + upper) / 2;
            var maxbits = BuildWeightedTree(curWeight, totalData);

            if (maxbits <= MAX_BITS)
            {
                bestWeight = curWeight;
                lower = curWeight;
            }
            else
            {
                upper = curWeight;
            }

            if (curWeight == totalData && maxbits <= MAX_BITS)
                break;
            if (upper - lower <= 1)
                break;
        }

        BuildWeightedTree(bestWeight, totalData);
        AssignCanonicalCodes();
    }

    public void ExportTreeRle(BitStreamOut bs)
    {
        var numbits = MAX_BITS >= 16 ? 5 : MAX_BITS >= 8 ? 4 : 3;

        var lastVal = -1;
        var repCount = 0;

        void Flush(int val)
        {
            if (repCount == 0) return;
            WriteRleTreeBits(bs, val, repCount, numbits);
            repCount = 0;
        }

        for (var i = 0; i < NUM_CODES; i++)
        {
            var val = NumBits[i];
            if (val == lastVal)
            {
                repCount++;
            }
            else
            {
                Flush(lastVal);
                lastVal = val;
                repCount = 1;
            }
        }
        Flush(lastVal);
    }

    public void Encode(BitStreamOut bs, uint symbol)
    {
        if (symbol >= NUM_CODES)
            return;
        if (NumBits[symbol] > 0)
            bs.Write(Codes[symbol], NumBits[symbol]);
    }

    private int BuildWeightedTree(int totalWeight, int totalData)
    {
        var nodes = new TreeNode[32];
        var activeIndices = new List<int>(16);

        for (var i = 0; i < NUM_CODES; i++)
        {
            if (_histogram[i] != 0)
            {
                var w = (int)((long)_histogram[i] * (long)totalWeight / (long)totalData);
                if (w == 0)
                {
                    w = 1;
                }

                nodes[i].Weight = w;
                nodes[i].Parent = -1;
                activeIndices.Add(i);
            }
            else
            {
                NumBits[i] = 0;
            }
        }

        SortByWeight(nodes, activeIndices);

        var nextAlloc = NUM_CODES;
        while (activeIndices.Count > 1)
        {
            var idx0 = activeIndices[activeIndices.Count - 1];
            activeIndices.RemoveAt(activeIndices.Count - 1);
            var idx1 = activeIndices[activeIndices.Count - 1];
            activeIndices.RemoveAt(activeIndices.Count - 1);

            var newIdx = nextAlloc++;
            nodes[newIdx].Weight = nodes[idx0].Weight + nodes[idx1].Weight;
            nodes[newIdx].Parent = -1;
            nodes[idx0].Parent = newIdx;
            nodes[idx1].Parent = newIdx;

            var insertPos = 0;
            while (insertPos < activeIndices.Count &&
                   nodes[newIdx].Weight <= nodes[activeIndices[insertPos]].Weight)
            {
                insertPos++;
            }

            activeIndices.Insert(insertPos, newIdx);
        }

        var maxBits = 0;
        for (var i = 0; i < NUM_CODES; i++)
        {
            if (_histogram[i] != 0)
            {
                var depth = 0;
                var current = i;
                while (nodes[current].Parent >= 0)
                {
                    depth++;
                    current = nodes[current].Parent;
                }
                NumBits[i] = depth == 0 ? 1 : depth;
                if (NumBits[i] > maxBits)
                {
                    maxBits = NumBits[i];
                }
            }
        }

        return maxBits;
    }

    private static void SortByWeight(TreeNode[] nodes, List<int> indices)
    {
        indices.Sort((a, b) => nodes[b].Weight.CompareTo(nodes[a].Weight));
    }

    private void AssignCanonicalCodes()
    {
        var bithisto = new int[33];
        for (var i = 0; i < NUM_CODES; i++)
        {
            var nb = NumBits[i];
            if (nb > 0 && nb <= 32)
            {
                bithisto[nb]++;
            }
        }

        uint curstart = 0;
        for (var codelen = 32; codelen > 0; codelen--)
        {
            var nextstart = (uint)((curstart + bithisto[codelen]) >> 1);
            bithisto[codelen] = (int)curstart;
            curstart = nextstart;
        }

        for (var i = 0; i < NUM_CODES; i++)
        {
            if (NumBits[i] > 0)
            {
                Codes[i] = (uint)bithisto[NumBits[i]]++;
            }
        }
    }

    private static void WriteRleTreeBits(BitStreamOut bs, int value, int repCount, int numbits)
    {
        while (repCount > 0)
        {
            if (value == 1)
            {
                bs.Write(1, numbits);
                bs.Write(1, numbits);
                repCount--;
            }
            else if (repCount <= 2)
            {
                bs.Write((uint)value, numbits);
                repCount--;
            }
            else
            {
                var reps = Math.Min(repCount - 3, (1 << numbits) - 1);
                bs.Write(1, numbits);
                bs.Write((uint)value, numbits);
                bs.Write((uint)reps, numbits);
                repCount -= reps + 3;
            }
        }
    }

    private struct TreeNode
    {
        public int Weight;
        public int Parent;
    }
}
