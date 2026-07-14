using System.Text;
using CHDSharp.Models;
using CHDSharp.Models.Utils;
using CHDSharp.Utils;

namespace CHDSharp;

/// <summary>Parses and validates CHD V1-V5 file headers, reading compression configuration, block maps, checksums, and metadata pointers from the stream.</summary>
internal static class ChdHeaders
{
    /// <summary>Reads and parses a V1 CHD header from the stream.</summary>
    /// <param name="file">The stream positioned immediately after the CHD magic and version fields.</param>
    /// <param name="chd">When this method returns, contains the parsed header data.</param>
    /// <returns><see cref="ChdError.Chderrnone"/> on success.</returns>
    public static ChdError ReadHeaderV1(Stream file, out ChdHeader chd)
    {
        chd = new ChdHeader();

        using var br = new BinaryReader(file, Encoding.UTF8, true);

        chd.Compression = [ChdCodec.Zlib];
        var flags = br.ReadUInt32Be();
        var compression = br.ReadUInt32Be();
        chd.Blocksize = br.ReadUInt32Be();
        chd.Totalblocks = br.ReadUInt32Be();
        var cylinders = br.ReadUInt32Be();
        var heads = br.ReadUInt32Be();
        var sectors = br.ReadUInt32Be();
        chd.Md5 = br.ReadBytes(16);
        chd.Parentmd5 = br.ReadBytes(16);

        const int hardDiskSectorSize = 512;
        chd.Totalbytes = cylinders * heads * sectors * hardDiskSectorSize;
        chd.Blocksize *= hardDiskSectorSize;
        chd.Unitbytes = chd.Blocksize;

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
            chd.Map[i].Comptype = (chd.Map[i].Length == chd.Blocksize)
                           ? CompressionType.Compressionnone
                           : CompressionType.Compressiontype0;
        }

        return ChdError.Chderrnone;
    }

    /// <summary>Reads and parses a V2 CHD header from the stream.</summary>
    /// <param name="file">The stream positioned immediately after the CHD magic and version fields.</param>
    /// <param name="chd">When this method returns, contains the parsed header data.</param>
    /// <returns><see cref="ChdError.Chderrnone"/> on success.</returns>
    public static ChdError ReadHeaderV2(Stream file, out ChdHeader chd)
    {
        chd = new ChdHeader();

        using var br = new BinaryReader(file, Encoding.UTF8, true);

        chd.Compression = [ChdCodec.Zlib];
        var flags = br.ReadUInt32Be();
        var compression = br.ReadUInt32Be();
        var blocksizeOld = br.ReadUInt32Be(); // this is now unused
        chd.Totalblocks = br.ReadUInt32Be();
        var cylinders = br.ReadUInt32Be();
        var heads = br.ReadUInt32Be();
        var sectors = br.ReadUInt32Be();
        chd.Md5 = br.ReadBytes(16);
        chd.Parentmd5 = br.ReadBytes(16);
        chd.Blocksize = br.ReadUInt32Be(); // blocksize added to header in V2

        const int hardDiskSectorSize = 512;
        chd.Totalbytes = cylinders * heads * sectors * hardDiskSectorSize;
        chd.Unitbytes = chd.Blocksize;

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
            chd.Map[i].Comptype = (chd.Map[i].Length == chd.Blocksize)
                           ? CompressionType.Compressionnone
                           : CompressionType.Compressiontype0;
        }


        return ChdError.Chderrnone;
    }

    /// <summary>Reads and parses a V3 CHD header from the stream.</summary>
    /// <param name="file">The stream positioned immediately after the CHD magic and version fields.</param>
    /// <param name="chd">When this method returns, contains the parsed header data.</param>
    /// <returns><see cref="ChdError.Chderrnone"/> on success.</returns>
    public static ChdError ReadHeaderV3(Stream file, out ChdHeader chd)
    {
        chd = new ChdHeader();
        using var br = new BinaryReader(file, Encoding.UTF8, true);

        var flags = br.ReadUInt32Be();

        chd.Compression = [ChdCommon.CompTypeConv(br.ReadUInt32Be())];
        chd.Totalblocks = br.ReadUInt32Be(); // total number of CHD Blocks

        chd.Totalbytes = br.ReadUInt64Be(); // total byte size of the image
        chd.Metaoffset = br.ReadUInt64Be();

        chd.Md5 = br.ReadBytes(16);
        chd.Parentmd5 = br.ReadBytes(16);
        chd.Blocksize = br.ReadUInt32Be(); // length of a CHD Block
        chd.Unitbytes = chd.Blocksize;
        chd.Rawsha1 = br.ReadBytes(20);
        chd.Parentsha1 = br.ReadBytes(20);

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
            if ((mapflag & MapEntryFlag.Mapentryflagnocrc) != 0)
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
    public static ChdError ReadHeaderV4(Stream file, out ChdHeader chd)
    {
        chd = new ChdHeader();
        using var br = new BinaryReader(file, Encoding.UTF8, true);

        var flags = br.ReadUInt32Be();

        chd.Compression = [ChdCommon.CompTypeConv(br.ReadUInt32Be())];
        chd.Totalblocks = br.ReadUInt32Be(); // total number of CHD Blocks

        chd.Totalbytes = br.ReadUInt64Be(); // total byte size of the image
        chd.Metaoffset = br.ReadUInt64Be();

        chd.Blocksize = br.ReadUInt32Be(); // length of a CHD Block
        chd.Unitbytes = chd.Blocksize;
        chd.Sha1 = br.ReadBytes(20);
        chd.Parentsha1 = br.ReadBytes(20);
        chd.Rawsha1 = br.ReadBytes(20);

        chd.Map = new MapEntry[chd.Totalblocks];

        for (var i = 0; i < chd.Totalblocks; i++)
        {
            chd.Map[i] = new MapEntry
            {
                Offset = br.ReadUInt64Be(),
                Crc = br.ReadUInt32Be(),
                Length = (uint)((br.ReadUInt16Be()) | (br.ReadByte() << 16))
            };
            var mapflag = (MapEntryFlag)br.ReadByte();
            chd.Map[i].Comptype = ChdCommon.ConvMapEntryFlagtoCompressionType(mapflag);
            chd.Map[i].Crc = null;
        }

        return ChdError.Chderrnone;
    }


    /// <summary>Reads and parses a V5 CHD header from the stream, including the compressed or uncompressed block map.</summary>
    /// <param name="file">The stream positioned immediately after the CHD magic and version fields.</param>
    /// <param name="chd">When this method returns, contains the parsed header data.</param>
    /// <returns><see cref="ChdError.Chderrnone"/> on success; otherwise an error code if the map is corrupt.</returns>
    public static ChdError ReadHeaderV5(Stream file, out ChdHeader chd)
    {
        chd = new ChdHeader();
        using var br = new BinaryReader(file, Encoding.UTF8, true);

        chd.Compression = new ChdCodec[4];
        for (var i = 0; i < 4; i++)
        {
            chd.Compression[i] = (ChdCodec)br.ReadUInt32Be();
        }

        chd.Totalbytes = br.ReadUInt64Be(); // total byte size of the image
        var mapoffset = br.ReadUInt64Be();
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

        var err = chdCompressed ? compressed_v5_map(br, mapoffset, chd.Totalblocks, chd.Blocksize, unitbytes, out chd.Map) : uncompressed_v5_map(br, mapoffset, chd.Totalblocks, chd.Blocksize, out chd.Map);

        return err;
    }


    /// <summary>Reads an uncompressed V5 block map where each hunk is either a direct uncompressed block or a parent reference.</summary>
    /// <param name="br">The binary reader positioned at the map offset.</param>
    /// <param name="mapoffset">The file offset where the map data begins.</param>
    /// <param name="totalblocks">The total number of hunks.</param>
    /// <param name="blocksize">The size of each hunk in bytes.</param>
    /// <param name="map">When this method returns, contains the parsed map entries.</param>
    /// <returns><see cref="ChdError.Chderrnone"/> on success.</returns>
    private static ChdError uncompressed_v5_map(BinaryReader br, ulong mapoffset, uint totalblocks, uint blocksize, out MapEntry[] map)
    {
        br.BaseStream.Seek((long)mapoffset, SeekOrigin.Begin);

        map = new MapEntry[totalblocks];
        for (var blockIndex = 0; blockIndex < totalblocks; blockIndex++)
        {
            map[blockIndex] = new MapEntry();
            var offsetWord = br.ReadUInt32Be();
            if (offsetWord == 0)
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
                map[blockIndex].Comptype = CompressionType.Compressionnone;
                map[blockIndex].Length = blocksize;
                map[blockIndex].Offset = (ulong)offsetWord * blocksize;
            }
        }

        return ChdError.Chderrnone;
    }

    /// <summary>Reads a Huffman-compressed V5 block map, decoding compression types, lengths, and offsets for each hunk.</summary>
    /// <param name="br">The binary reader positioned at the map offset.</param>
    /// <param name="mapoffset">The file offset where the compressed map data begins.</param>
    /// <param name="totalBlocks">The total number of hunks.</param>
    /// <param name="blocksize">The size of each hunk in bytes.</param>
    /// <param name="unitbytes">The unit size for parent block address translation.</param>
    /// <param name="map">When this method returns, contains the parsed map entries.</param>
    /// <returns><see cref="ChdError.Chderrnone"/> on success; otherwise <see cref="ChdError.Chderrdecompressionerror"/> if CRC validation fails.</returns>
    private static ChdError compressed_v5_map(BinaryReader br, ulong mapoffset, uint totalBlocks, uint blocksize, uint unitbytes, out MapEntry[] map)
    {
        map = new MapEntry[totalBlocks];

        /* read the reader */
        br.BaseStream.Seek((long)mapoffset, SeekOrigin.Begin);
        var mapbytes = br.ReadUInt32Be(); //0
        var firstoffs = br.ReadUInt48Be(); //4
        var mapcrc = br.ReadUInt16Be(); //10
        var lengthbits = br.ReadByte(); //12
        var selfbits = br.ReadByte(); //13
        var parentbits = br.ReadByte(); //14
        br.ReadByte(); //15 not used

        var compressedArr = new byte[mapbytes];
        br.BaseStream.ReadExactly(compressedArr, 0, (int)mapbytes);

        var bitbuf = new BitStream(compressedArr, 0, (int)mapbytes);

        /* first decode the compression types */
        var decoder = new HuffmanDecoder(16, 8, bitbuf);

        var err = decoder.ImportTreeRle();
        if (err != HuffmanError.HufferrNone)
        {
            return ChdError.Chderrdecompressionerror;
        }

        var repcount = 0;
        CompressionType lastcomp = 0;
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
                    curoffset += length = bitbuf.read(lengthbits);
                    crc16 = (ushort)bitbuf.read(16);
                    break;

                case CompressionType.Compressionnone:
                    curoffset += length = blocksize;
                    crc16 = (ushort)bitbuf.read(16);
                    break;

                case CompressionType.Compressionself:
                    lastSelf = (uint)(offset = bitbuf.read(selfbits));
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
                    lastParent = offset = (((ulong)blockIndex) * ((ulong)blocksize)) / unitbytes;
                    break;

                case CompressionType.Compressionparent:
                    offset = bitbuf.read(parentbits);
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
        var rawmap = new byte[totalBlocks * 12];
        for (var blockIndex = 0; blockIndex < totalBlocks; blockIndex++)
        {
            var rawmapIndex = blockIndex * 12;
            rawmap[rawmapIndex] = (byte)map[blockIndex].Comptype;
            rawmap.PutUInt24Be(rawmapIndex + 1, map[blockIndex].Length);
            rawmap.PutUInt48Be(rawmapIndex + 4, map[blockIndex].Offset);
            rawmap.PutUInt16Be(rawmapIndex + 10, (uint)map[blockIndex].Crc16!.Value);
        }

        if (CRC16.calc(rawmap, (int)totalBlocks * 12) != mapcrc)
            return ChdError.Chderrdecompressionerror;

        return ChdError.Chderrnone;
    }
}
