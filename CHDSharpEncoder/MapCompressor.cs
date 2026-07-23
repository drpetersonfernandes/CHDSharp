namespace CHDSharpEncoder;

public static class MapCompressor
{
    private const byte COMPRESSION_RLE_SMALL = 7;
    private const byte COMPRESSION_RLE_LARGE = 8;

    public static byte[] Compress(MapEntry[] entries, uint hunkCount, uint hunkBytes, uint unitBytes)
    {
        var rleList = RleEncode(entries, hunkCount);

        uint maxCompLen = 0;
        for (uint i = 0; i < hunkCount; i++)
        {
            if (entries[i].Compression <= MapEntry.COMPRESSION_TYPE_3)
            {
                maxCompLen = Math.Max(maxCompLen, entries[i].CompLength);
            }
        }
        var lengthBits = BitsForValue(maxCompLen);

        var huff = new Huffman16_8();
        foreach (var sym in rleList)
            huff.CountSymbol(sym);
        huff.BuildTree();

        var nbitsNeeded = (8 * 16) + (12 + Math.Max(lengthBits + 16, 0)) * (int)hunkCount;
        var bs = new BitStreamOut(nbitsNeeded / 8 + 1 + 256);

        huff.ExportTreeRle(bs);

        foreach (var sym in rleList)
            huff.Encode(bs, sym);

        ulong firstOffset = 0;
        for (uint i = 0; i < hunkCount; i++)
        {
            var entry = entries[i];
            switch (entry.Compression)
            {
                case MapEntry.COMPRESSION_TYPE_0:
                case MapEntry.COMPRESSION_TYPE_1:
                case MapEntry.COMPRESSION_TYPE_2:
                case MapEntry.COMPRESSION_TYPE_3:
                    bs.Write(entry.CompLength, lengthBits);
                    bs.Write(entry.Crc16, 16);
                    if (firstOffset == 0)
                    {
                        firstOffset = entry.Offset;
                    }

                    break;
                case MapEntry.COMPRESSION_NONE:
                    bs.Write(entry.Crc16, 16);
                    if (firstOffset == 0)
                    {
                        firstOffset = entry.Offset;
                    }

                    break;
            }
        }

        var compressedDataLen = bs.Flush();

        var rawMap = new byte[hunkCount * 12];
        for (uint i = 0; i < hunkCount; i++)
            MapEntry.WriteRawMapEntry(rawMap, (int)i, entries[i]);
        var mapCrc = Crc16.Compute(rawMap);

        var headerW = new BigEndianWriter(16);
        headerW.WriteU32((uint)compressedDataLen);
        headerW.WriteU48(firstOffset);
        headerW.WriteU16(mapCrc);
        headerW.WriteU8(lengthBits);
        headerW.WriteU8(0);
        headerW.WriteU8(0);
        headerW.WriteU8(0);

        var header = headerW.ToArray();
        var compressedData = bs.ToArray();
        var result = new byte[header.Length + compressedData.Length];
        Array.Copy(header, 0, result, 0, header.Length);
        Array.Copy(compressedData, 0, result, header.Length, compressedData.Length);
        return result;
    }

    private static List<byte> RleEncode(MapEntry[] entries, uint hunkCount)
    {
        var rleList = new List<byte>((int)hunkCount + 4);
        byte lastcomp = 0;
        var count = 0;

        for (uint i = 0; i < hunkCount; i++)
        {
            var curcomp = entries[i].Compression;
            if (curcomp == lastcomp)
            {
                count++;
            }
            else
            {
                Flush(count);
                lastcomp = curcomp;
                count = 1;
            }
        }
        Flush(count);

        return rleList;

        void Flush(int totalCount)
        {
            if (totalCount == 0)
                return;

            rleList.Add(lastcomp);

            var repCount = totalCount - 1;
            while (repCount > 0)
            {
                if (repCount < 3)
                {
                    rleList.Add(lastcomp);
                    repCount--;
                }
                else if (repCount <= 3 + 15)
                {
                    rleList.Add(COMPRESSION_RLE_SMALL);
                    rleList.Add((byte)(repCount - 3));
                    repCount = 0;
                }
                else
                {
                    var n = Math.Min(repCount, 3 + 16 + 255);
                    rleList.Add(COMPRESSION_RLE_LARGE);
                    rleList.Add((byte)((n - 3 - 16) >> 4));
                    rleList.Add((byte)((n - 3 - 16) & 15));
                    repCount -= n;
                }
            }
        }
    }

    private static byte BitsForValue(uint value)
    {
        byte result = 0;
        while (value != 0) { value >>= 1; result++; }
        return result;
    }
}
