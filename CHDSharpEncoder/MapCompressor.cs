using CHDSharpEncoder.Models;

namespace CHDSharpEncoder;

/// <summary>Compresses a CHD v5 hunk map using RLE and Huffman encoding.</summary>
public static class MapCompressor
{
    private const byte CompressionRleSmall = 7;
    private const byte CompressionRleLarge = 8;

    /// <summary>Promoted map type: SELF reference to the same source hunk as the previous SELF entry.</summary>
    private const byte CompressionSelf0 = 9;

    /// <summary>Promoted map type: SELF reference to the source hunk after the previous SELF entry.</summary>
    private const byte CompressionSelf1 = 10;

    /// <summary>Promoted map type: PARENT reference to the unit matching this hunk's own unit offset.</summary>
    private const byte CompressionParentSelf = 11;

    /// <summary>Promoted map type: PARENT reference to the same unit as the previous PARENT entry.</summary>
    private const byte CompressionParent0 = 12;

    /// <summary>Promoted map type: PARENT reference to the unit after the previous PARENT entry's unit.</summary>
    private const byte CompressionParent1 = 13;

    /// <summary>Compresses the hunk map entries into a compact binary representation.</summary>
    /// <param name="entries">The array of map entries to compress. SELF entries must carry the source
    /// hunk index in <see cref="MapEntry.Offset"/> with <see cref="MapEntry.CompLength"/> and
    /// <see cref="MapEntry.Crc16"/> set to zero; PARENT entries carry the parent unit index
    /// (0-based, in units) in <see cref="MapEntry.Offset"/>, likewise with zero length and CRC.</param>
    /// <param name="hunkCount">The number of hunks in the image.</param>
    /// <param name="hunkBytes">The size of each hunk in bytes.</param>
    /// <param name="unitBytes">The unit size in bytes.</param>
    /// <returns>A byte array containing the compressed map data.</returns>
    public static byte[] Compress(MapEntry[] entries, uint hunkCount, uint hunkBytes, uint unitBytes)
    {
        var rleList = RleEncode(entries, hunkCount, hunkBytes, unitBytes, out var maxSelf, out var maxParent);

        uint maxCompLen = 0;
        for (uint i = 0; i < hunkCount; i++)
        {
            // MAME tracks the maximum length over every entry that is not a SELF or PARENT
            // reference (compress_v5_map's else branch): COMPRESSION_NONE entries carry the
            // hunk size, compressed entries their stored length, promoted pseudo-types zero.
            if (entries[i].Compression is not (MapEntry.CompressionSelf or MapEntry.CompressionParent))
            {
                maxCompLen = Math.Max(maxCompLen, entries[i].CompLength);
            }
        }

        var lengthBits = BitsForValue(maxCompLen);
        var selfBits = BitsForValue(maxSelf);
        var parentBits = BitsForValue(maxParent);

        var huff = new Huffman168();
        foreach (var sym in rleList)
            huff.CountSymbol(sym);
        huff.BuildTree();

        var nbitsNeeded = (8 * 16) + (12 + Math.Max(Math.Max(lengthBits + 16, selfBits), parentBits)) * (int)hunkCount;
        var bs = new BitStreamOut(nbitsNeeded / 8 + 1 + 256);

        huff.ExportTreeRle(bs);

        foreach (var sym in rleList)
            huff.Encode(bs, sym);

        // iterate the RLE-decoded types in lockstep with the raw entries, writing the
        // auxiliary data for each hunk (SELF_0/SELF_1 pseudo-types encode nothing)
        ulong firstOffset = 0;
        int rleIndex = 0;
        byte lastComp = 0;
        int repCount = 0;
        for (uint i = 0; i < hunkCount; i++)
        {
            byte type;
            if (repCount > 0)
            {
                type = lastComp;
                repCount--;
            }
            else
            {
                var val = rleList[rleIndex++];
                switch (val)
                {
                    case CompressionRleSmall:
                        type = lastComp;
                        repCount = 2 + rleList[rleIndex++];
                        break;
                    case CompressionRleLarge:
                        type = lastComp;
                        repCount = 2 + 16 + (rleList[rleIndex++] << 4);
                        repCount += rleList[rleIndex++];
                        break;
                    default:
                        type = lastComp = val;
                        break;
                }
            }

            var entry = entries[i];
            switch (type)
            {
                case MapEntry.CompressionType0:
                case MapEntry.CompressionType1:
                case MapEntry.CompressionType2:
                case MapEntry.CompressionType3:
                    bs.Write(entry.CompLength, lengthBits);
                    bs.Write(entry.Crc16, 16);
                    if (firstOffset == 0)
                    {
                        firstOffset = entry.Offset;
                    }

                    break;
                case MapEntry.CompressionNone:
                    bs.Write(entry.Crc16, 16);
                    if (firstOffset == 0)
                    {
                        firstOffset = entry.Offset;
                    }

                    break;
                case MapEntry.CompressionSelf:
                    // writes the source hunk index with selfBits; guaranteed to fit because
                    // maxSelf covers every non-promoted SELF reference
                    bs.Write((uint)entry.Offset, selfBits);
                    break;
                case MapEntry.CompressionParent:
                    // writes the parent unit index with parentBits; guaranteed to fit because
                    // maxParent covers every non-promoted PARENT reference
                    bs.Write((uint)entry.Offset, parentBits);
                    break;
                case CompressionSelf0:
                case CompressionSelf1:
                case CompressionParentSelf:
                case CompressionParent0:
                case CompressionParent1:
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
        headerW.WriteU8(selfBits);
        headerW.WriteU8(parentBits);
        headerW.WriteU8(0);

        var header = headerW.ToArray();
        var compressedData = bs.ToArray();
        var result = new byte[header.Length + compressedData.Length];
        Array.Copy(header, 0, result, 0, header.Length);
        Array.Copy(compressedData, 0, result, header.Length, compressedData.Length);
        return result;
    }

    /// <summary>
    /// RLE-encodes the compression types, promoting SELF references to the compact
    /// SELF_0/SELF_1 forms and PARENT references to the compact PARENT_SELF/PARENT_0/PARENT_1
    /// forms, and tracking the maximum referenced source hunk and parent unit indices
    /// (mirrors MAME's compress_v5_map).
    /// </summary>
    private static List<byte> RleEncode(MapEntry[] entries, uint hunkCount, uint hunkBytes, uint unitBytes,
        out uint maxSelf, out uint maxParent)
    {
        var rleList = new List<byte>((int)hunkCount + 4);
        byte lastcomp = 0;
        var count = 0;
        uint lastSelf = 0;
        ulong lastParent = 0;
        maxSelf = 0;
        maxParent = 0;
        var unitsPerHunk = hunkBytes / unitBytes;

        for (uint hunknum = 0; hunknum < hunkCount; hunknum++)
        {
            var curcomp = entries[hunknum].Compression;

            switch (curcomp)
            {
                case MapEntry.CompressionSelf:
                {
                    // promote self references to the previous reference's form
                    var refHunk = (uint)entries[hunknum].Offset;
                    if (refHunk == lastSelf)
                    {
                        curcomp = CompressionSelf0;
                    }
                    else if (refHunk == lastSelf + 1)
                    {
                        curcomp = CompressionSelf1;
                    }
                    else
                    {
                        maxSelf = Math.Max(maxSelf, refHunk);
                    }

                    lastSelf = refHunk;
                    break;
                }
                case MapEntry.CompressionParent:
                {
                    // promote parent references to the previous reference's form; the
                    // reference is a unit index into the parent (like MAME)
                    var refUnit = entries[hunknum].Offset;
                    if (refUnit == (ulong)hunknum * hunkBytes / unitBytes)
                    {
                        curcomp = CompressionParentSelf;
                    }
                    else if (refUnit == lastParent)
                    {
                        curcomp = CompressionParent0;
                    }
                    else if (refUnit == lastParent + unitsPerHunk)
                    {
                        curcomp = CompressionParent1;
                    }
                    else
                    {
                        maxParent = (uint)Math.Max(maxParent, refUnit);
                    }

                    lastParent = refUnit;
                    break;
                }
            }

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
                switch (repCount)
                {
                    case < 3:
                        rleList.Add(lastcomp);
                        repCount--;
                        break;
                    case <= 3 + 15:
                        rleList.Add(CompressionRleSmall);
                        rleList.Add((byte)(repCount - 3));
                        repCount = 0;
                        break;
                    default:
                    {
                        var n = Math.Min(repCount, 3 + 16 + 255);
                        rleList.Add(CompressionRleLarge);
                        rleList.Add((byte)((n - 3 - 16) >> 4));
                        rleList.Add((byte)((n - 3 - 16) & 15));
                        repCount -= n;
                        break;
                    }
                }
            }
        }
    }

    private static byte BitsForValue(uint value)
    {
        byte result = 0;
        while (value != 0)
        {
            value >>= 1;
            result++;
        }

        return result;
    }
}