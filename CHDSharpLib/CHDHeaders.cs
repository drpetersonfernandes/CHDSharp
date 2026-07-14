using System.Text;
using CHDSharp.Models;
using CHDSharp.Models.Utils;
using CHDSharp.Utils;

namespace CHDSharp;

internal static class ChdHeaders
{
    public static chdError ReadHeaderV1(Stream file, out ChdHeader chd)
    {
        chd = new ChdHeader();

        using var br = new BinaryReader(file, Encoding.UTF8, true);

        chd.Compression = [chdCodec.CHDCODECZLIB];
        var flags = br.ReadUInt32BE();
        var compression = br.ReadUInt32BE();
        chd.Blocksize = br.ReadUInt32BE();
        chd.Totalblocks = br.ReadUInt32BE();
        var cylinders = br.ReadUInt32BE();
        var heads = br.ReadUInt32BE();
        var sectors = br.ReadUInt32BE();
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
            var tmpu = br.ReadUInt64BE();
            chd.Map[i] = new MapEntry();


            if (mapBack.TryGetValue(tmpu, out var v))
            {
                chd.Map[i].Offset = (uint)v;
                chd.Map[i].Length = 0;
                chd.Map[i].Comptype = compressionType.COMPRESSIONSELF;
                continue;
            }

            mapBack.Add(tmpu, i);

            chd.Map[i].Offset = tmpu & 0xfffffffffff;
            chd.Map[i].Length = (uint)(tmpu >> 44);
            chd.Map[i].Comptype = (chd.Map[i].Length == chd.Blocksize)
                           ? compressionType.COMPRESSIONNONE
                           : compressionType.COMPRESSIONTYPE0;
        }

        return chdError.CHDERRNONE;
    }

    public static chdError ReadHeaderV2(Stream file, out ChdHeader chd)
    {
        chd = new ChdHeader();

        using var br = new BinaryReader(file, Encoding.UTF8, true);

        chd.Compression = [chdCodec.CHDCODECZLIB];
        var flags = br.ReadUInt32BE();
        var compression = br.ReadUInt32BE();
        var blocksizeOld = br.ReadUInt32BE(); // this is now unused
        chd.Totalblocks = br.ReadUInt32BE();
        var cylinders = br.ReadUInt32BE();
        var heads = br.ReadUInt32BE();
        var sectors = br.ReadUInt32BE();
        chd.Md5 = br.ReadBytes(16);
        chd.Parentmd5 = br.ReadBytes(16);
        chd.Blocksize = br.ReadUInt32BE(); // blocksize added to header in V2

        const int hardDiskSectorSize = 512;
        chd.Totalbytes = cylinders * heads * sectors * hardDiskSectorSize;
        chd.Unitbytes = chd.Blocksize;

        chd.Map = new MapEntry[chd.Totalblocks];

        var mapBack = new Dictionary<ulong, int>();

        for (var i = 0; i < chd.Totalblocks; i++)
        {
            var tmpu = br.ReadUInt64BE();
            chd.Map[i] = new MapEntry();


            if (mapBack.TryGetValue(tmpu, out var v))
            {
                chd.Map[i].Offset = (uint)v;
                chd.Map[i].Length = 0;
                chd.Map[i].Comptype = compressionType.COMPRESSIONSELF;
                continue;
            }

            mapBack.Add(tmpu, i);

            chd.Map[i].Offset = tmpu & 0xfffffffffff;
            chd.Map[i].Length = (uint)(tmpu >> 44);
            chd.Map[i].Comptype = (chd.Map[i].Length == chd.Blocksize)
                           ? compressionType.COMPRESSIONNONE
                           : compressionType.COMPRESSIONTYPE0;
        }


        return chdError.CHDERRNONE;
    }

    public static chdError ReadHeaderV3(Stream file, out ChdHeader chd)
    {
        chd = new ChdHeader();
        using var br = new BinaryReader(file, Encoding.UTF8, true);

        var flags = br.ReadUInt32BE();

        chd.Compression = [ChdCommon.CompTypeConv(br.ReadUInt32BE())];
        chd.Totalblocks = br.ReadUInt32BE(); // total number of CHD Blocks

        chd.Totalbytes = br.ReadUInt64BE(); // total byte size of the image
        chd.Metaoffset = br.ReadUInt64BE();

        chd.Md5 = br.ReadBytes(16);
        chd.Parentmd5 = br.ReadBytes(16);
        chd.Blocksize = br.ReadUInt32BE(); // length of a CHD Block
        chd.Unitbytes = chd.Blocksize;
        chd.Rawsha1 = br.ReadBytes(20);
        chd.Parentsha1 = br.ReadBytes(20);

        chd.Map = new MapEntry[chd.Totalblocks];

        for (var i = 0; i < chd.Totalblocks; i++)
        {
            chd.Map[i] = new MapEntry
            {
                Offset = br.ReadUInt64BE(),
                Crc = br.ReadUInt32BE(),
                Length = (uint)((br.ReadByte() << 8) | (br.ReadByte() << 0) | (br.ReadByte() << 16))
            };
            var mapflag = (mapFlags)br.ReadByte();
            chd.Map[i].Comptype = ChdCommon.ConvMapFlagstoCompressionType(mapflag);
            if ((mapflag & mapFlags.MAPENTRYFLAGNOCRC) != 0)
            {
                chd.Map[i].Crc = null;
            }
        }

        return chdError.CHDERRNONE;
    }

    public static chdError ReadHeaderV4(Stream file, out ChdHeader chd)
    {
        chd = new ChdHeader();
        using var br = new BinaryReader(file, Encoding.UTF8, true);

        var flags = br.ReadUInt32BE();

        chd.Compression = [ChdCommon.CompTypeConv(br.ReadUInt32BE())];
        chd.Totalblocks = br.ReadUInt32BE(); // total number of CHD Blocks

        chd.Totalbytes = br.ReadUInt64BE(); // total byte size of the image
        chd.Metaoffset = br.ReadUInt64BE();

        chd.Blocksize = br.ReadUInt32BE(); // length of a CHD Block
        chd.Unitbytes = chd.Blocksize;
        chd.Sha1 = br.ReadBytes(20);
        chd.Parentsha1 = br.ReadBytes(20);
        chd.Rawsha1 = br.ReadBytes(20);

        chd.Map = new MapEntry[chd.Totalblocks];

        for (var i = 0; i < chd.Totalblocks; i++)
        {
            chd.Map[i] = new MapEntry
            {
                Offset = br.ReadUInt64BE(),
                Crc = br.ReadUInt32BE(),
                Length = (uint)((br.ReadUInt16BE()) | (br.ReadByte() << 16))
            };
            var mapflag = (mapFlags)br.ReadByte();
            chd.Map[i].Comptype = ChdCommon.ConvMapFlagstoCompressionType(mapflag);
            chd.Map[i].Crc = null;
        }

        return chdError.CHDERRNONE;
    }


    public static chdError ReadHeaderV5(Stream file, out ChdHeader chd)
    {
        chd = new ChdHeader();
        using var br = new BinaryReader(file, Encoding.UTF8, true);

        chd.Compression = new chdCodec[4];
        for (var i = 0; i < 4; i++)
        {
            chd.Compression[i] = (chdCodec)br.ReadUInt32BE();
        }

        chd.Totalbytes = br.ReadUInt64BE(); // total byte size of the image
        var mapoffset = br.ReadUInt64BE();
        chd.Metaoffset = br.ReadUInt64BE();

        chd.Blocksize = br.ReadUInt32BE(); // length of a CHD Hunk (Block)
        var unitbytes = br.ReadUInt32BE();
        chd.Unitbytes = unitbytes;
        chd.Rawsha1 = br.ReadBytes(20);
        chd.Sha1 = br.ReadBytes(20);
        chd.Parentsha1 = br.ReadBytes(20);

        chd.Totalblocks = (uint)((chd.Totalbytes + chd.Blocksize - 1) / chd.Blocksize);

        var chdCompressed = chd.Compression[0] != chdCodec.CHDCODECNONE;
        chd.UncompressedMap = !chdCompressed;

        var err = chdCompressed ? compressed_v5_map(br, mapoffset, chd.Totalblocks, chd.Blocksize, unitbytes, out chd.Map) : uncompressed_v5_map(br, mapoffset, chd.Totalblocks, chd.Blocksize, out chd.Map);

        return err;
    }


    private static chdError uncompressed_v5_map(BinaryReader br, ulong mapoffset, uint totalblocks, uint blocksize, out MapEntry[] map)
    {
        br.BaseStream.Seek((long)mapoffset, SeekOrigin.Begin);

        map = new MapEntry[totalblocks];
        for (var blockIndex = 0; blockIndex < totalblocks; blockIndex++)
        {
            map[blockIndex] = new MapEntry();
            var offsetWord = br.ReadUInt32BE();
            if (offsetWord == 0)
            {
                // Offset word 0 in an uncompressed V5 map means: take this hunk
                // from the parent (same hunk index), or zero-fill if no parent.
                // Mark as PARENT; the read path resolves same-hunk from parent.
                map[blockIndex].Comptype = compressionType.COMPRESSIONPARENT;
                map[blockIndex].Length = blocksize;
                map[blockIndex].Offset = (ulong)blockIndex; // direct parent hunk index
            }
            else
            {
                map[blockIndex].Comptype = compressionType.COMPRESSIONNONE;
                map[blockIndex].Length = blocksize;
                map[blockIndex].Offset = (ulong)offsetWord * blocksize;
            }
        }

        return chdError.CHDERRNONE;
    }

    private static chdError compressed_v5_map(BinaryReader br, ulong mapoffset, uint totalBlocks, uint blocksize, uint unitbytes, out MapEntry[] map)
    {
        map = new MapEntry[totalBlocks];

        /* read the reader */
        br.BaseStream.Seek((long)mapoffset, SeekOrigin.Begin);
        var mapbytes = br.ReadUInt32BE(); //0
        var firstoffs = br.ReadUInt48BE(); //4
        var mapcrc = br.ReadUInt16BE(); //10
        var lengthbits = br.ReadByte(); //12
        var selfbits = br.ReadByte(); //13
        var parentbits = br.ReadByte(); //14
        br.ReadByte(); //15 not used

        var compressedArr = new byte[mapbytes];
        br.BaseStream.ReadExactly(compressedArr, 0, (int)mapbytes);

        var bitbuf = new BitStream(compressedArr, 0, (int)mapbytes);

        /* first decode the compression types */
        var decoder = new HuffmanDecoder(16, 8, bitbuf);

        var err = decoder.ImportTreeRLE();
        if (err != huffman_error.HUFFERR_NONE)
        {
            return chdError.CHDERRDECOMPRESSIONERROR;
        }

        var repcount = 0;
        compressionType lastcomp = 0;
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
                var val = (compressionType)decoder.DecodeOne();
                switch (val)
                {
                    case compressionType.COMPRESSIONRLESMALL:
                        map[blockIndex].Comptype = lastcomp;
                        repcount = 2 + (int)decoder.DecodeOne();
                        break;
                    case compressionType.COMPRESSIONRLELARGE:
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
                case compressionType.COMPRESSIONTYPE0:
                case compressionType.COMPRESSIONTYPE1:
                case compressionType.COMPRESSIONTYPE2:
                case compressionType.COMPRESSIONTYPE3:
                    curoffset += length = bitbuf.read(lengthbits);
                    crc16 = (ushort)bitbuf.read(16);
                    break;

                case compressionType.COMPRESSIONNONE:
                    curoffset += length = blocksize;
                    crc16 = (ushort)bitbuf.read(16);
                    break;

                case compressionType.COMPRESSIONSELF:
                    lastSelf = (uint)(offset = bitbuf.read(selfbits));
                    break;

                /* pseudo-types; convert into base types */
                case compressionType.COMPRESSIONSELF1:
                    lastSelf++;
                    goto case compressionType.COMPRESSIONSELF0;

                case compressionType.COMPRESSIONSELF0:
                    map[blockIndex].Comptype = compressionType.COMPRESSIONSELF;
                    offset = lastSelf;
                    break;

                case compressionType.COMPRESSIONPARENTSELF:
                    map[blockIndex].Comptype = compressionType.COMPRESSIONPARENT;
                    lastParent = offset = (((ulong)blockIndex) * ((ulong)blocksize)) / unitbytes;
                    break;

                case compressionType.COMPRESSIONPARENT:
                    offset = bitbuf.read(parentbits);
                    lastParent = offset;
                    break;

                case compressionType.COMPRESSIONPARENT1:
                    lastParent += blocksize / unitbytes;
                    goto case compressionType.COMPRESSIONPARENT0;
                case compressionType.COMPRESSIONPARENT0:
                    map[blockIndex].Comptype = compressionType.COMPRESSIONPARENT;
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
            rawmap.PutUInt24BE(rawmapIndex + 1, map[blockIndex].Length);
            rawmap.PutUInt48BE(rawmapIndex + 4, map[blockIndex].Offset);
            rawmap.PutUInt16BE(rawmapIndex + 10, (uint)map[blockIndex].Crc16!.Value);
        }

        if (CRC16.calc(rawmap, (int)totalBlocks * 12) != mapcrc)
            return chdError.CHDERRDECOMPRESSIONERROR;

        return chdError.CHDERRNONE;
    }
}
