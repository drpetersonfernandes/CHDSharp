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

        var nbitsNeeded = 8 * 16 + (12 + Math.Max(Math.Max(lengthBits + 16, selfBits), parentBits)) * (int)hunkCount;

        // chdman's compress_v5_map allocates exactly nbits_needed/8 + 1 bytes INCLUDING the
        // 16-byte map header and bitstreams over the tail. That estimate under-sizes the
        // payload for small hunk counts (the RLE tree alone needs up to ~72 bits while only
        // the per-entry budget remains past the header), so MAME's bitstream_out silently
        // drops whole bytes beyond its buffer while flush() keeps counting them; the clipped
        // positions read back as zeroes in the appended map. We replicate the allocation and
        // clipping first: when nothing was dropped (or only zero bits fell past the end),
        // the result is byte-identical to chdman's. When real data bits would be lost, the
        // clipped map's CRC-16 no longer matches its header and even chdman cannot re-open
        // its own file (upstream bug, hit e.g. by single-hunk images at hunk sizes
        // 18816/19584/65536); in that case we fall back to the full bitstream so the output
        // stays verifiable — a deliberate, documented divergence from a corrupt reference.
        var mapAllocation = nbitsNeeded / 8 + 1;
        var payloadBytes = mapAllocation - 16;

        // pass 1: chdman-sized fixed buffer (drops overflow, counts it)
        var compressed = new byte[mapAllocation];
        var bs = new BitStreamOut(compressed, 16, payloadBytes);
        var firstOffset = WriteMapPayload(bs);
        var compressedDataLen = bs.Flush();

        if (compressedDataLen > payloadBytes)
        {
            // pass 2: clipping would corrupt the map — emit the full bitstream instead
            bs = new BitStreamOut(nbitsNeeded / 8 + 1 + 256);
            firstOffset = WriteMapPayload(bs);
            compressedDataLen = bs.Flush();
            compressed = new byte[16 + compressedDataLen];
            Array.Copy(bs.ToArray(), 0, compressed, 16, compressedDataLen);
        }

        var rawMap = new byte[hunkCount * 12];
        for (uint i = 0; i < hunkCount; i++)
            MapEntry.WriteRawMapEntry(rawMap, (int)i, entries[i]);
        var mapCrc = Crc16.Compute(rawMap);

        // map header: complen, firstoffs, mapcrc, lengthbits/selfbits/parentbits/reserved
        var headerW = new BigEndianWriter(16);
        headerW.WriteU32((uint)compressedDataLen);
        headerW.WriteU48(firstOffset);
        headerW.WriteU16(mapCrc);
        headerW.WriteU8(lengthBits);
        headerW.WriteU8(selfBits);
        headerW.WriteU8(parentBits);
        headerW.WriteU8(0);

        Array.Copy(headerW.ToArray(), 0, compressed, 0, 16);
        return compressed.AsSpan(0, Math.Min(16 + compressedDataLen, compressed.Length)).ToArray();

        // writes tree + RLE symbols + per-entry auxiliary data; returns the first nonzero
        // stored offset for the map header
        ulong WriteMapPayload(BitStreamOut stream)
        {
            huff.ExportTreeRle(stream);

            foreach (var sym in rleList)
                huff.Encode(stream, sym);

            // iterate the RLE-decoded types in lockstep with the raw entries, writing the
            // auxiliary data for each hunk (SELF_0/SELF_1 pseudo-types encode nothing)
            ulong first = 0;
            var rleIndex = 0;
            byte lastComp = 0;
            var repCount = 0;
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
                        stream.Write(entry.CompLength, lengthBits);
                        stream.Write(entry.Crc16, 16);
                        if (first == 0)
                        {
                            first = entry.Offset;
                        }

                        break;
                    case MapEntry.CompressionNone:
                        stream.Write(entry.Crc16, 16);
                        if (first == 0)
                        {
                            first = entry.Offset;
                        }

                        break;
                    case MapEntry.CompressionSelf:
                        // writes the source hunk index with selfBits; guaranteed to fit because
                        // maxSelf covers every non-promoted SELF reference
                        stream.Write((uint)entry.Offset, selfBits);
                        break;
                    case MapEntry.CompressionParent:
                        // writes the parent unit index with parentBits; guaranteed to fit because
                        // maxParent covers every non-promoted PARENT reference
                        stream.Write((uint)entry.Offset, parentBits);
                        break;
                    case CompressionSelf0:
                    case CompressionSelf1:
                    case CompressionParentSelf:
                    case CompressionParent0:
                    case CompressionParent1:
                        break;
                }
            }

            return first;
        }
    }

    /// <summary>
    /// RLE-encodes the compression types, promoting SELF references to the compact
    /// SELF_0/SELF_1 forms and PARENT references to the compact PARENT_SELF/PARENT_0/PARENT_1
    /// forms, and tracking the maximum referenced source hunk and parent unit indices.
    /// Mirrors MAME's <c>compress_v5_map</c> RLE loop exactly: the run type is only written
    /// when it changes (the decoder starts with <c>lastcomp = 0</c>), and the RLE count is the
    /// full run length, so an all-<c>COMPRESSION_TYPE_0</c> image encodes as
    /// <c>[RLE_LARGE, hi, lo]</c> with no leading type symbol.
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

            // track repeats
            if (curcomp == lastcomp)
            {
                count++;
            }

            // if no repeat, or we're at the end, flush it
            if (curcomp != lastcomp || hunknum == hunkCount - 1)
            {
                while (count != 0)
                {
                    switch (count)
                    {
                        case < 3:
                            rleList.Add(lastcomp);
                            count--;
                            break;
                        case <= 3 + 15:
                            rleList.Add(CompressionRleSmall);
                            rleList.Add((byte)(count - 3));
                            count = 0;
                            break;
                        default:
                        {
                            var thisCount = Math.Min(count, 3 + 16 + 255);
                            rleList.Add(CompressionRleLarge);
                            rleList.Add((byte)((thisCount - 3 - 16) >> 4));
                            rleList.Add((byte)((thisCount - 3 - 16) & 15));
                            count -= thisCount;
                            break;
                        }
                    }
                }

                if (curcomp != lastcomp)
                {
                    lastcomp = curcomp;
                    rleList.Add(lastcomp);
                }
            }
        }

        return rleList;
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