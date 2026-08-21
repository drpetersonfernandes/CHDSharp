using System.Text;
using CHDSharp.Models;
using CHDSharp.Models.Utils;
using CHDSharp.Utils;

namespace CHDSharp;

/// <summary>Parses and validates CHD V1-V5 file headers, reading compression configuration, block maps, checksums, and metadata pointers from the stream.</summary>
internal static class ChdHeaders
{
    private const uint MaxHunkBytes = 128 * 1024 * 1024;
    private const ulong MaxLogicalBytes = 1024UL * 1024 * 1024 * 1024;

    /// <summary>Dispatches to the version-specific header reader (the stream must be positioned
    /// right after the magic + length + version preamble, as left by <see cref="Chd.CheckHeader"/>).</summary>
    internal static ChdError ReadHeaderByVersion(Stream file, uint version, out ChdHeader chd)
    {
        switch (version)
        {
            case 1: return ReadHeaderV1(file, out chd);
            case 2: return ReadHeaderV2(file, out chd);
            case 3: return ReadHeaderV3(file, out chd);
            case 4: return ReadHeaderV4(file, out chd);
            case 5: return ReadHeaderV5(file, out chd);
            default:
                chd = new ChdHeader();
                return ChdError.Chderrunsupportedversion;
        }
    }

    /// <summary>
    /// Default multiple of <see cref="ChdHeader.Blocksize"/> used to bound a single compressed
    /// hunk's on-disk length when no explicit cap is set. A valid CHD created at a low compression
    /// level can legitimately have a compressed hunk slightly larger than the uncompressed hunk
    /// (codec headers/footers), so the default is 2x the hunk size; a malicious hunk map entry that
    /// claims more than this is rejected before any allocation.
    /// </summary>
    internal const uint DefaultMaxCompressedMultiple = 2;

    /// <summary>Bits 2-31 of the V1-V4 header flags field are undefined; chd-rs rejects any
    /// file with them set (<c>header.rs:557</c>, <c>Flags::Undefined = 0xfffffffc</c>).</summary>
    private const uint LegacyUndefinedFlagBits = 0xFFFFFFFC;

    /// <summary>Valid V5 hunk-map compression type values. Anything outside 0..13 is
    /// an invalid map entry (chd-rs <c>CompressionTypeV5::from_u8</c> rejects them).</summary>
    private const byte MaxValidV5MapType = 13;

    /// <summary>Rejects V1-V4 headers whose flags field carries undefined bits
    /// (chd-rs <c>header.rs:537-592</c> parity). V5 is exempt (chd-rs does not validate it).</summary>
    private static ChdError ValidateLegacyFlags(uint flags)
    {
        return (flags & LegacyUndefinedFlagBits) != 0 ? ChdError.Chderrinvaliddata : ChdError.Chderrnone;
    }

    /// <summary>Checks that every stored (compressed/uncompressed) hunk's byte range
    /// <c>[offset, offset + length)</c> lies within the file (libchdr <c>chd.c</c> maxoffset
    /// check, chd-rs <c>map.rs:420-422</c>). Entries without on-disk data (SELF/PARENT/MINI)
    /// are skipped; their offsets are indexes, not file offsets. Called from
    /// <see cref="ChdFile.Open(Stream, bool, ChdFile, out ChdFile, System.Threading.CancellationToken)"/>
    /// where the real file length is known — header-only reads stay lenient, matching
    /// libchdr/chd-rs (which validate the map only on open).</summary>
    /// <param name="chd">The parsed header whose map is validated.</param>
    /// <param name="fileLength">The underlying file length in bytes.</param>
    internal static ChdError ValidateMapBounds(ChdHeader chd, ulong fileLength)
    {
        foreach (var me in chd.Map)
        {
            if (me.Length == 0)
                continue;

            switch (me.Comptype)
            {
                case CompressionType.Compressiontype0:
                case CompressionType.Compressiontype1:
                case CompressionType.Compressiontype2:
                case CompressionType.Compressiontype3:
                case CompressionType.Compressionnone:
                case CompressionType.Compressiontype2Nd:
                case CompressionType.Compressionself:
                    // V1-V4 SELF entries store the source hunk's file offset + length;
                    // V5 SELF entries carry the source hunk index with length 0, so they are
                    // already skipped above (and their indexes are validated by LinkSelfBlocks).
                    if (me.Offset + me.Length > fileLength)
                        return ChdError.Chderrinvaliddata;

                    break;
            }
        }

        return ChdError.Chderrnone;
    }

    /// <summary>Validates that the hunk size, logical size, and compressed-hunk cap are within safe limits.</summary>
    /// <param name="chd">The parsed header to validate.</param>
    /// <returns><see cref="ChdError.Chderrnone"/> if sizes are acceptable; otherwise <see cref="ChdError.Chderrinvaliddata"/>.</returns>
    internal static ChdError ValidateSizeLimits(ChdHeader chd)
    {
        if (chd.Blocksize == 0 || chd.Blocksize > MaxHunkBytes || chd.Totalbytes > MaxLogicalBytes)
            return ChdError.Chderrinvaliddata;

        // Default the compressed-hunk cap to 2x the hunk size if not explicitly set.
        if (chd.MaxCompressedBlockCap == 0)
        {
            chd.MaxCompressedBlockCap = checked(chd.Blocksize * DefaultMaxCompressedMultiple);
        }

        // The cap itself must be representable and at least as large as the hunk size.
        if (chd.MaxCompressedBlockCap < chd.Blocksize)
            return ChdError.Chderrinvaliddata;

        return ChdError.Chderrnone;
    }

    /// <summary>Reads and parses a V1 CHD header from the stream.</summary>
    /// <param name="file">The stream positioned immediately after the CHD magic and version fields.</param>
    /// <param name="chd">When this method returns, contains the parsed header data.</param>
    /// <returns><see cref="ChdError.Chderrnone"/> on success.</returns>
    internal static ChdError ReadHeaderV1(Stream file, out ChdHeader chd)
    {
        chd = new ChdHeader();

        using var br = new BinaryReader(file, Encoding.UTF8, true);

        chd.Compression = [ChdCodec.Zlib];
        chd.Flags = br.ReadUInt32Be(); // flags
        if (ValidateLegacyFlags(chd.Flags) != ChdError.Chderrnone)
            return ChdError.Chderrinvaliddata;

        br.ReadUInt32Be(); // compression
        chd.ObsoleteHunksize = br.ReadUInt32Be(); // number of 512-byte sectors per hunk
        chd.Totalblocks = br.ReadUInt32Be();
        chd.ObsoleteCylinders = br.ReadUInt32Be();
        chd.ObsoleteHeads = br.ReadUInt32Be();
        chd.ObsoleteSectors = br.ReadUInt32Be();
        chd.Md5 = br.ReadBytes(16);
        chd.Parentmd5 = br.ReadBytes(16);

        const int hardDiskSectorSize = 512;
        chd.Totalbytes = (ulong)chd.ObsoleteCylinders * chd.ObsoleteHeads * chd.ObsoleteSectors * hardDiskSectorSize;
        chd.Blocksize = chd.ObsoleteHunksize * hardDiskSectorSize;
        chd.Unitbytes = chd.Blocksize;

        if (chd.Blocksize == 0 || chd.Blocksize > MaxHunkBytes || (ulong)chd.Totalblocks * chd.Blocksize > MaxLogicalBytes)
            return ChdError.Chderrinvaliddata;

        // The raw map is stored inline and must physically fit in the file: a corrupted
        // Totalblocks (up to 2^32-1) would otherwise allocate a multi-GB array and loop
        // for minutes (Phase 6.1 hardening; same attack as the V5 compressed map).
        if ((ulong)chd.Totalblocks * 8 > (ulong)(file.Length - file.Position))
            return ChdError.Chderrinvaliddata;

        chd.Map = new MapEntry[chd.Totalblocks];

        var mapBack = new Dictionary<ulong, int>();

        for (var i = 0; i < chd.Totalblocks; i++)
        {
            var tmpu = br.ReadUInt64Be();
            chd.Map[i] = new MapEntry();


            if (mapBack.TryGetValue(tmpu, out var v))
            {
                chd.Map[i].Offset = (uint)v;
                chd.Map[i].Length = 0;
                chd.Map[i].Comptype = CompressionType.Compressionself;
                continue;
            }

            mapBack.Add(tmpu, i);

            chd.Map[i].Offset = tmpu & 0xfffffffffff;
            chd.Map[i].Length = (uint)(tmpu >> 44);
            chd.Map[i].Comptype = chd.Map[i].Length == chd.Blocksize
                ? CompressionType.Compressionnone
                : CompressionType.Compressiontype0;
        }

        return ChdError.Chderrnone;
    }

    /// <summary>Reads and parses a V2 CHD header from the stream.</summary>
    /// <param name="file">The stream positioned immediately after the CHD magic and version fields.</param>
    /// <param name="chd">When this method returns, contains the parsed header data.</param>
    /// <returns><see cref="ChdError.Chderrnone"/> on success.</returns>
    internal static ChdError ReadHeaderV2(Stream file, out ChdHeader chd)
    {
        chd = new ChdHeader();

        using var br = new BinaryReader(file, Encoding.UTF8, true);

        chd.Compression = [ChdCodec.Zlib];
        chd.Flags = br.ReadUInt32Be(); // flags
        if (ValidateLegacyFlags(chd.Flags) != ChdError.Chderrnone)
            return ChdError.Chderrinvaliddata;

        br.ReadUInt32Be(); // compression
        chd.ObsoleteHunksize = br.ReadUInt32Be(); // number of seclen-byte sectors per hunk
        chd.Totalblocks = br.ReadUInt32Be();
        chd.ObsoleteCylinders = br.ReadUInt32Be();
        chd.ObsoleteHeads = br.ReadUInt32Be();
        chd.ObsoleteSectors = br.ReadUInt32Be();
        chd.Md5 = br.ReadBytes(16);
        chd.Parentmd5 = br.ReadBytes(16);
        var seclen = br.ReadUInt32Be(); // bytes per sector (added in V2)

        chd.Totalbytes = (ulong)chd.ObsoleteCylinders * chd.ObsoleteHeads * chd.ObsoleteSectors * seclen;
        chd.Blocksize = chd.ObsoleteHunksize * seclen;
        chd.Unitbytes = chd.Blocksize;

        if (chd.Blocksize == 0 || chd.Blocksize > MaxHunkBytes || (ulong)chd.Totalblocks * chd.Blocksize > MaxLogicalBytes)
            return ChdError.Chderrinvaliddata;

        // The raw map is stored inline and must physically fit in the file: a corrupted
        // Totalblocks (up to 2^32-1) would otherwise allocate a multi-GB array and loop
        // for minutes (Phase 6.1 hardening; same attack as the V5 compressed map).
        if ((ulong)chd.Totalblocks * 8 > (ulong)(file.Length - file.Position))
            return ChdError.Chderrinvaliddata;

        chd.Map = new MapEntry[chd.Totalblocks];

        var mapBack = new Dictionary<ulong, int>();

        for (var i = 0; i < chd.Totalblocks; i++)
        {
            var tmpu = br.ReadUInt64Be();
            chd.Map[i] = new MapEntry();


            if (mapBack.TryGetValue(tmpu, out var v))
            {
                chd.Map[i].Offset = (uint)v;
                chd.Map[i].Length = 0;
                chd.Map[i].Comptype = CompressionType.Compressionself;
                continue;
            }

            mapBack.Add(tmpu, i);

            chd.Map[i].Offset = tmpu & 0xfffffffffff;
            chd.Map[i].Length = (uint)(tmpu >> 44);
            chd.Map[i].Comptype = chd.Map[i].Length == chd.Blocksize
                ? CompressionType.Compressionnone
                : CompressionType.Compressiontype0;
        }

        return ChdError.Chderrnone;
    }

    /// <summary>Reads and parses a V3 CHD header from the stream.</summary>
    /// <param name="file">The stream positioned immediately after the CHD magic and version fields.</param>
    /// <param name="chd">When this method returns, contains the parsed header data.</param>
    /// <returns><see cref="ChdError.Chderrnone"/> on success.</returns>
    internal static ChdError ReadHeaderV3(Stream file, out ChdHeader chd)
    {
        chd = new ChdHeader();
        using var br = new BinaryReader(file, Encoding.UTF8, true);

        chd.Flags = br.ReadUInt32Be(); // flags
        if (ValidateLegacyFlags(chd.Flags) != ChdError.Chderrnone)
            return ChdError.Chderrinvaliddata;

        var compressionType = br.ReadUInt32Be();
        chd.Compression = [ChdCommon.CompTypeConv(compressionType)];
        if (compressionType == 2)
            ChdCommon.InitSecondaryCodec(chd);

        chd.Totalblocks = br.ReadUInt32Be(); // total number of CHD Blocks

        chd.Totalbytes = br.ReadUInt64Be(); // total byte size of the image
        chd.Metaoffset = br.ReadUInt64Be();

        chd.Md5 = br.ReadBytes(16);
        chd.Parentmd5 = br.ReadBytes(16);
        chd.Blocksize = br.ReadUInt32Be(); // length of a CHD Block
        chd.Unitbytes = chd.Blocksize;
        chd.Rawsha1 = br.ReadBytes(20);
        chd.Parentsha1 = br.ReadBytes(20);

        // The raw map is stored inline (16 bytes per entry) and must physically fit in
        // the file; a corrupted Totalblocks would otherwise allocate a multi-GB array
        // and loop for minutes (Phase 6.1 hardening).
        if ((ulong)chd.Totalblocks * 16 > (ulong)(file.Length - file.Position))
            return ChdError.Chderrinvaliddata;

        chd.Map = new MapEntry[chd.Totalblocks];

        for (var i = 0; i < chd.Totalblocks; i++)
        {
            chd.Map[i] = new MapEntry
            {
                Offset = br.ReadUInt64Be(),
                Crc = br.ReadUInt32Be(),
                Length = (uint)((br.ReadByte() << 8) | (br.ReadByte() << 0) | (br.ReadByte() << 16))
            };
            var mapflag = (MapEntryFlag)br.ReadByte();
            chd.Map[i].Comptype = ChdCommon.ConvMapEntryFlagtoCompressionType(mapflag);
            if ((mapflag & MapEntryFlag.Mapentryflagnocrc) != MapEntryFlag.Mapentrytypeinvalid)
            {
                chd.Map[i].Crc = null;
            }
        }

        return ChdError.Chderrnone;
    }

    /// <summary>Reads and parses a V4 CHD header from the stream.</summary>
    /// <param name="file">The stream positioned immediately after the CHD magic and version fields.</param>
    /// <param name="chd">When this method returns, contains the parsed header data.</param>
    /// <returns><see cref="ChdError.Chderrnone"/> on success.</returns>
    internal static ChdError ReadHeaderV4(Stream file, out ChdHeader chd)
    {
        chd = new ChdHeader();
        using var br = new BinaryReader(file, Encoding.UTF8, true);

        chd.Flags = br.ReadUInt32Be(); // flags
        if (ValidateLegacyFlags(chd.Flags) != ChdError.Chderrnone)
            return ChdError.Chderrinvaliddata;

        var compressionType = br.ReadUInt32Be();
        chd.Compression = [ChdCommon.CompTypeConv(compressionType)];
        if (compressionType == 2)
            ChdCommon.InitSecondaryCodec(chd);

        chd.Totalblocks = br.ReadUInt32Be(); // total number of CHD Blocks

        chd.Totalbytes = br.ReadUInt64Be(); // total byte size of the image
        chd.Metaoffset = br.ReadUInt64Be();

        chd.Blocksize = br.ReadUInt32Be(); // length of a CHD Block
        chd.Unitbytes = chd.Blocksize;
        chd.Sha1 = br.ReadBytes(20);
        chd.Parentsha1 = br.ReadBytes(20);
        chd.Rawsha1 = br.ReadBytes(20);

        // The raw map is stored inline (16 bytes per entry) and must physically fit in
        // the file; a corrupted Totalblocks would otherwise allocate a multi-GB array
        // and loop for minutes (Phase 6.1 hardening).
        if ((ulong)chd.Totalblocks * 16 > (ulong)(file.Length - file.Position))
            return ChdError.Chderrinvaliddata;

        chd.Map = new MapEntry[chd.Totalblocks];

        for (var i = 0; i < chd.Totalblocks; i++)
        {
            chd.Map[i] = new MapEntry
            {
                Offset = br.ReadUInt64Be(),
                Crc = br.ReadUInt32Be(),
                Length = (uint)(br.ReadUInt16Be() | (br.ReadByte() << 16))
            };
            var mapflag = (MapEntryFlag)br.ReadByte();
            chd.Map[i].Comptype = ChdCommon.ConvMapEntryFlagtoCompressionType(mapflag);
            if ((mapflag & MapEntryFlag.Mapentryflagnocrc) != MapEntryFlag.Mapentrytypeinvalid)
            {
                chd.Map[i].Crc = null;
            }
        }

        return ChdError.Chderrnone;
    }


    /// <summary>Reads and parses a V5 CHD header from the stream, including the compressed or uncompressed block map.</summary>
    /// <param name="file">The stream positioned immediately after the CHD magic and version fields.</param>
    /// <param name="chd">When this method returns, contains the parsed header data.</param>
    /// <returns><see cref="ChdError.Chderrnone"/> on success; otherwise an error code if the map is corrupt.</returns>
    internal static ChdError ReadHeaderV5(Stream file, out ChdHeader chd)
    {
        chd = new ChdHeader();
        using var br = new BinaryReader(file, Encoding.UTF8, true);

        chd.Compression = new ChdCodec[4];
        for (var i = 0; i < 4; i++)
        {
            var codecValue = br.ReadUInt32Be();
            chd.Compression[i] = (ChdCodec)codecValue;
            if (chd.Compression[i] != ChdCodec.None && !ChdCommon.IsValidCodec(chd.Compression[i]))
                return ChdError.Chderrinvaliddata;
        }

        chd.Totalbytes = br.ReadUInt64Be(); // total byte size of the image
        var mapoffset = br.ReadUInt64Be();
        chd.Mapoffset = mapoffset;
        chd.Metaoffset = br.ReadUInt64Be();

        chd.Blocksize = br.ReadUInt32Be(); // length of a CHD Hunk (Block)
        var unitbytes = br.ReadUInt32Be();
        chd.Unitbytes = unitbytes;
        chd.Rawsha1 = br.ReadBytes(20);
        chd.Sha1 = br.ReadBytes(20);
        chd.Parentsha1 = br.ReadBytes(20);

        chd.Totalblocks = (uint)((chd.Totalbytes + chd.Blocksize - 1) / chd.Blocksize);

        var chdCompressed = chd.Compression[0] != ChdCodec.None;
        chd.UncompressedMap = !chdCompressed;

        var err = chdCompressed ? compressed_v5_map(br, mapoffset, chd.Totalblocks, chd.Blocksize, unitbytes, out chd.Map) : uncompressed_v5_map(br, mapoffset, chd.Totalblocks, chd.Blocksize, !Util.IsAllZeroArray(chd.Parentsha1), out chd.Map);

        return err;
    }


    private static ChdError uncompressed_v5_map(BinaryReader br, ulong mapoffset, uint totalblocks, uint blocksize, bool hasParent, out MapEntry[] map)
    {
        var streamLen = (ulong)br.BaseStream.Length;
        var mapSize = (ulong)totalblocks * 4;

        if (mapoffset + mapSize < mapoffset || mapoffset + mapSize > streamLen)
        {
            map = [];
            return ChdError.Chderrinvaliddata;
        }

        br.BaseStream.Seek((long)mapoffset, SeekOrigin.Begin);

        map = new MapEntry[totalblocks];
        for (var blockIndex = 0; blockIndex < totalblocks; blockIndex++)
        {
            map[blockIndex] = new MapEntry();
            var offsetWord = br.ReadUInt32Be();
            if (offsetWord == 0)
            {
                if (hasParent)
                {
                    // Offset word 0 in an uncompressed V5 map means: take this hunk
                    // from the parent (same hunk index), or zero-fill if no parent.
                    // Mark as PARENT; the read path resolves same-hunk from parent.
                    map[blockIndex].Comptype = CompressionType.Compressionparent;
                    map[blockIndex].Length = blocksize;
                    map[blockIndex].Offset = (ulong)blockIndex; // direct parent hunk index
                }
                else
                {
                    // No parent: an unallocated hunk reads as all zeroes (MAME chd.cpp).
                    map[blockIndex].Comptype = CompressionType.Compressionzero;
                    map[blockIndex].Length = 0;
                    map[blockIndex].Offset = 0;
                }
            }
            else
            {
                map[blockIndex].Comptype = CompressionType.Compressionnone;
                map[blockIndex].Length = blocksize;
                map[blockIndex].Offset = (ulong)offsetWord * blocksize;
            }
        }

        return ChdError.Chderrnone;
    }

    private static ChdError compressed_v5_map(BinaryReader br, ulong mapoffset, uint totalBlocks, uint blocksize, uint unitbytes, out MapEntry[] map)
    {
        var streamLen = (ulong)br.BaseStream.Length;

        map = [];

        br.BaseStream.Seek((long)mapoffset, SeekOrigin.Begin);
        var mapbytes = br.ReadUInt32Be();
        var firstoffs = br.ReadUInt48Be();
        var mapcrc = br.ReadUInt16Be();
        var lengthbits = br.ReadByte();
        var selfbits = br.ReadByte();
        var parentbits = br.ReadByte();
        br.ReadByte(); //15 not used

        if (mapoffset + 16 + mapbytes < mapoffset || mapoffset + 16 + mapbytes > streamLen)
            return ChdError.Chderrinvaliddata;

        // File-corruption guard (Phase 6.1): a corrupted 'totalbytes'/'blocksize' pair in the
        // header can make totalBlocks arbitrarily large (up to ~2^32), which would turn the
        // decode loops below into a multi-billion-iteration hang with a multi-GB map array.
        // Bound it against what the map can actually encode: every entry is either a direct
        // huffman symbol (>= 1 bit) or part of an RLE run, and the widest run is
        // COMPRESSION_RLE_LARGE: 3 huffman symbols for up to 2+16+255... 273 entries, i.e. at
        // most 91 entries per bit (728 per byte). Any valid map must satisfy this; anything
        // above it is provably corrupt, so reject before allocating or looping.
        const ulong maxEntriesPerBit = 91; // 273 entries per 3 huffman symbols (1 bit each)
        if (totalBlocks > (ulong)mapbytes * 8 * maxEntriesPerBit + 1024)
            return ChdError.Chderrinvaliddata;

        // The raw (decompressed) map is reconstructed below as one 12-byte entry per hunk;
        // beyond int.MaxValue entries the raw map cannot be indexed at all.
        if ((ulong)totalBlocks * 12 > int.MaxValue)
            return ChdError.Chderrinvaliddata;

        var compressedArr = new byte[mapbytes];
        br.BaseStream.ReadExactly(compressedArr, 0, (int)mapbytes);

        map = new MapEntry[totalBlocks];

        var bitbuf = new BitStream(compressedArr, 0, (int)mapbytes);

        /* first decode the compression types */
        var decoder = new HuffmanDecoder(16, 8, bitbuf);

        var err = decoder.ImportTreeRle();
        if (err != HuffmanError.HufferrNone)
        {
            return ChdError.Chderrdecompressionerror;
        }

        var repcount = 0;
        var lastcomp = CompressionType.Compressiontype0;
        for (uint blockIndex = 0; blockIndex < totalBlocks; blockIndex++)
        {
            map[blockIndex] = new MapEntry();
            if (repcount > 0)
            {
                map[blockIndex].Comptype = lastcomp;
                repcount--;
            }
            else
            {
                var val = (CompressionType)decoder.DecodeOne();

                // Reject undefined compression types before they reach the offset pass
                // (chd-rs CompressionTypeV5::from_u8 parity); a raw value above 13 would
                // otherwise be silently carried into the map as a corrupt entry.
                if ((byte)val > MaxValidV5MapType)
                    return ChdError.Chderrinvaliddata;

                switch (val)
                {
                    case CompressionType.Compressionrlesmall:
                        map[blockIndex].Comptype = lastcomp;
                        repcount = 2 + (int)decoder.DecodeOne();
                        break;
                    case CompressionType.Compressionrlelarge:
                        map[blockIndex].Comptype = lastcomp;
                        repcount = 2 + 16 + ((int)decoder.DecodeOne() << 4);
                        repcount += (int)decoder.DecodeOne();
                        break;
                    default:
                        map[blockIndex].Comptype = lastcomp = val;
                        break;
                }
            }
        }

        /* then iterate through the hunks and extract the needed data */
        uint lastSelf = 0;
        ulong lastParent = 0;
        var curoffset = firstoffs;
        for (uint blockIndex = 0; blockIndex < totalBlocks; blockIndex++)
        {
            var offset = curoffset;
            uint length = 0;
            ushort crc16 = 0;
            switch (map[blockIndex].Comptype)
            {
                /* base types */
                case CompressionType.Compressiontype0:
                case CompressionType.Compressiontype1:
                case CompressionType.Compressiontype2:
                case CompressionType.Compressiontype3:
                    curoffset += length = bitbuf.Read(lengthbits);
                    crc16 = (ushort)bitbuf.Read(16);
                    break;

                case CompressionType.Compressionnone:
                    curoffset += length = blocksize;
                    crc16 = (ushort)bitbuf.Read(16);
                    break;

                case CompressionType.Compressionself:
                    lastSelf = (uint)(offset = bitbuf.Read(selfbits));
                    break;

                /* pseudo-types; convert into base types */
                case CompressionType.Compressionself1:
                    lastSelf++;
                    goto case CompressionType.Compressionself0;

                case CompressionType.Compressionself0:
                    map[blockIndex].Comptype = CompressionType.Compressionself;
                    offset = lastSelf;
                    break;

                case CompressionType.Compressionparentself:
                    map[blockIndex].Comptype = CompressionType.Compressionparent;
                    lastParent = offset = blockIndex * (ulong)blocksize / unitbytes;
                    break;

                case CompressionType.Compressionparent:
                    offset = bitbuf.Read(parentbits);
                    lastParent = offset;
                    break;

                case CompressionType.Compressionparent1:
                    lastParent += blocksize / unitbytes;
                    goto case CompressionType.Compressionparent0;
                case CompressionType.Compressionparent0:
                    map[blockIndex].Comptype = CompressionType.Compressionparent;
                    offset = lastParent;
                    break;
            }

            map[blockIndex].Length = length;
            map[blockIndex].Offset = offset;
            map[blockIndex].Crc16 = crc16;
        }


        /* verify the final CRC */
        var rawmap = new byte[checked(totalBlocks * 12)];
        for (var blockIndex = 0; blockIndex < totalBlocks; blockIndex++)
        {
            var rawmapIndex = blockIndex * 12;
            rawmap[rawmapIndex] = (byte)map[blockIndex].Comptype;
            rawmap.PutUInt24Be(rawmapIndex + 1, map[blockIndex].Length);
            rawmap.PutUInt48Be(rawmapIndex + 4, map[blockIndex].Offset);
            rawmap.PutUInt16Be(rawmapIndex + 10, map[blockIndex].Crc16!.Value);
        }

        if (Crc16.Calc(rawmap, (int)totalBlocks * 12) != mapcrc)
            return ChdError.Chderrdecompressionerror;

        return ChdError.Chderrnone;
    }
}
