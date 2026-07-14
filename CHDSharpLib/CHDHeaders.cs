using System.Text;
using CHDSharp.Models;
using CHDSharp.Models.Utils;
using CHDSharp.Utils;

namespace CHDSharp;

internal static class CHDHeaders
{
    public static chd_error ReadHeaderV1(Stream file, out ChdHeader chd)
    {
        chd = new ChdHeader();

        using var br = new BinaryReader(file, Encoding.UTF8, true);

        chd.Compression = [chd_codec.CHD_CODEC_ZLIB];
        var flags = br.ReadUInt32BE();
        var compression = br.ReadUInt32BE();
        chd.Blocksize = br.ReadUInt32BE();
        chd.Totalblocks = br.ReadUInt32BE();
        var cylinders = br.ReadUInt32BE();
        var heads = br.ReadUInt32BE();
        var sectors = br.ReadUInt32BE();
        chd.Md5 = br.ReadBytes(16);
        chd.Parentmd5 = br.ReadBytes(16);

        const int HARD_DISK_SECTOR_SIZE = 512;
        chd.Totalbytes = cylinders * heads * sectors * HARD_DISK_SECTOR_SIZE;
        chd.Blocksize = chd.Blocksize * HARD_DISK_SECTOR_SIZE;
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
                chd.Map[i].Comptype = compression_type.COMPRESSION_SELF;
                continue;
            }

            mapBack.Add(tmpu, i);

            chd.Map[i].Offset = tmpu & 0xfffffffffff;
            chd.Map[i].Length = (uint)(tmpu >> 44);
            chd.Map[i].Comptype = (chd.Map[i].Length == chd.Blocksize)
                           ? compression_type.COMPRESSION_NONE
                           : compression_type.COMPRESSION_TYPE_0;
        }

        return chd_error.CHDERR_NONE;
    }

    public static chd_error ReadHeaderV2(Stream file, out ChdHeader chd)
    {
        chd = new ChdHeader();

        using var br = new BinaryReader(file, Encoding.UTF8, true);

        chd.Compression = [chd_codec.CHD_CODEC_ZLIB];
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

        const int HARD_DISK_SECTOR_SIZE = 512;
        chd.Totalbytes = cylinders * heads * sectors * HARD_DISK_SECTOR_SIZE;
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
                chd.Map[i].Comptype = compression_type.COMPRESSION_SELF;
                continue;
            }

            mapBack.Add(tmpu, i);

            chd.Map[i].Offset = tmpu & 0xfffffffffff;
            chd.Map[i].Length = (uint)(tmpu >> 44);
            chd.Map[i].Comptype = (chd.Map[i].Length == chd.Blocksize)
                           ? compression_type.COMPRESSION_NONE
                           : compression_type.COMPRESSION_TYPE_0;
        }


        return chd_error.CHDERR_NONE;
    }

    public static chd_error ReadHeaderV3(Stream file, out ChdHeader chd)
    {
        chd = new ChdHeader();
        using var br = new BinaryReader(file, Encoding.UTF8, true);

        var flags = br.ReadUInt32BE();

        chd.Compression = [CHDCommon.compTypeConv(br.ReadUInt32BE())];
        chd.Totalblocks = br.ReadUInt32BE(); // total number of CHD Blocks

        chd.Totalbytes = br.ReadUInt64BE();  // total byte size of the image
        chd.Metaoffset = br.ReadUInt64BE();

        chd.Md5 = br.ReadBytes(16);
        chd.Parentmd5 = br.ReadBytes(16);
        chd.Blocksize = br.ReadUInt32BE();    // length of a CHD Block
        chd.Unitbytes = chd.Blocksize;
        chd.Rawsha1 = br.ReadBytes(20);
        chd.Parentsha1 = br.ReadBytes(20);

        chd.Map = new MapEntry[chd.Totalblocks];

        for (var i = 0; i < chd.Totalblocks; i++)
        {
            chd.Map[i] = new MapEntry();
            chd.Map[i].Offset = br.ReadUInt64BE();
            chd.Map[i].Crc = br.ReadUInt32BE();
            chd.Map[i].Length = (uint)((br.ReadByte() << 8) | (br.ReadByte() << 0) | (br.ReadByte() << 16));
            var mapflag = (mapFlags)br.ReadByte();
            chd.Map[i].Comptype = CHDCommon.ConvMapFlagstoCompressionType(mapflag);
            if ((mapflag & mapFlags.MAP_ENTRY_FLAG_NO_CRC) != 0)
            {
                chd.Map[i].Crc = null;
            }
        }
        return chd_error.CHDERR_NONE;
    }

    public static chd_error ReadHeaderV4(Stream file, out ChdHeader chd)
    {
        chd = new ChdHeader();
        using var br = new BinaryReader(file, Encoding.UTF8, true);

        var flags = br.ReadUInt32BE();

        chd.Compression = [CHDCommon.compTypeConv(br.ReadUInt32BE())];
        chd.Totalblocks = br.ReadUInt32BE(); // total number of CHD Blocks

        chd.Totalbytes = br.ReadUInt64BE();  // total byte size of the image
        chd.Metaoffset = br.ReadUInt64BE();

        chd.Blocksize = br.ReadUInt32BE();    // length of a CHD Block
        chd.Unitbytes = chd.Blocksize;
        chd.Sha1 = br.ReadBytes(20);
        chd.Parentsha1 = br.ReadBytes(20);
        chd.Rawsha1 = br.ReadBytes(20);

        chd.Map = new MapEntry[chd.Totalblocks];

        for (var i = 0; i < chd.Totalblocks; i++)
        {
            chd.Map[i] = new MapEntry();
            chd.Map[i].Offset = br.ReadUInt64BE();
            chd.Map[i].Crc = br.ReadUInt32BE();
            chd.Map[i].Length = (uint)((br.ReadUInt16BE()) | (br.ReadByte() << 16));
            var mapflag = (mapFlags)br.ReadByte();
            chd.Map[i].Comptype = CHDCommon.ConvMapFlagstoCompressionType(mapflag);
            chd.Map[i].Crc = null;
        }
        return chd_error.CHDERR_NONE;
    }


    public static chd_error ReadHeaderV5(Stream file, out ChdHeader chd)
    {
        chd = new ChdHeader();
        using var br = new BinaryReader(file, Encoding.UTF8, true);

        chd.Compression = new chd_codec[4];
        for (var i = 0; i < 4; i++)
        {
            chd.Compression[i] = (chd_codec)br.ReadUInt32BE();
        }

        chd.Totalbytes = br.ReadUInt64BE();  // total byte size of the image
        var mapoffset = br.ReadUInt64BE();
        chd.Metaoffset = br.ReadUInt64BE();

        chd.Blocksize = br.ReadUInt32BE();    // length of a CHD Hunk (Block)
        var unitbytes = br.ReadUInt32BE();
        chd.Unitbytes = unitbytes;
        chd.Rawsha1 = br.ReadBytes(20);
        chd.Sha1 = br.ReadBytes(20);
        chd.Parentsha1 = br.ReadBytes(20);

        chd.Totalblocks = (uint)((chd.Totalbytes + chd.Blocksize - 1) / chd.Blocksize);

        var chdCompressed = chd.Compression[0] != chd_codec.CHD_CODEC_NONE;
        chd.UncompressedMap = !chdCompressed;

        var err = chdCompressed ?
                compressed_v5_map(br, mapoffset, chd.Totalblocks, chd.Blocksize, unitbytes, out chd.Map) :
                uncompressed_v5_map(br, mapoffset, chd.Totalblocks, chd.Blocksize, out chd.Map);

        return err;
    }


    private static chd_error uncompressed_v5_map(BinaryReader br, ulong mapoffset, uint totalblocks, uint blocksize, out MapEntry[] map)
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
                map[blockIndex].Comptype = compression_type.COMPRESSION_PARENT;
                map[blockIndex].Length = blocksize;
                map[blockIndex].Offset = (ulong)blockIndex; // direct parent hunk index
            }
            else
            {
                map[blockIndex].Comptype = compression_type.COMPRESSION_NONE;
                map[blockIndex].Length = blocksize;
                map[blockIndex].Offset = (ulong)offsetWord * blocksize;
            }
        }
        return chd_error.CHDERR_NONE;
    }

    private static chd_error compressed_v5_map(BinaryReader br, ulong mapoffset, uint totalBlocks, uint blocksize, uint unitbytes, out MapEntry[] map)
    {
        map = new MapEntry[totalBlocks];

        /* read the reader */
        br.BaseStream.Seek((long)mapoffset, SeekOrigin.Begin);
        var mapbytes = br.ReadUInt32BE();   //0
        var firstoffs = br.ReadUInt48BE(); //4
        var mapcrc = br.ReadUInt16BE();   //10
        var lengthbits = br.ReadByte();     //12
        var selfbits = br.ReadByte();       //13
        var parentbits = br.ReadByte();     //14
        br.ReadByte();                       //15 not used

        var compressed_arr = new byte[mapbytes];
        br.BaseStream.ReadExactly(compressed_arr, 0, (int)mapbytes);

        var bitbuf = new BitStream(compressed_arr, 0, (int)mapbytes);

        /* first decode the compression types */
        var decoder = new HuffmanDecoder(16, 8, bitbuf);
        if (decoder == null)
        {
            return chd_error.CHDERR_OUT_OF_MEMORY;
        }

        var err = decoder.ImportTreeRLE();
        if (err != huffman_error.HUFFERR_NONE)
        {
            return chd_error.CHDERR_DECOMPRESSION_ERROR;
        }

        var repcount = 0;
        compression_type lastcomp = 0;
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
                var val = (compression_type)decoder.DecodeOne();
                if (val == compression_type.COMPRESSION_RLE_SMALL)
                {
                    map[blockIndex].Comptype = lastcomp;
                    repcount = 2 + (int)decoder.DecodeOne();
                }
                else if (val == compression_type.COMPRESSION_RLE_LARGE)
                {
                    map[blockIndex].Comptype = lastcomp;
                    repcount = 2 + 16 + ((int)decoder.DecodeOne() << 4);
                    repcount += (int)decoder.DecodeOne();
                }
                else
                {
                    map[blockIndex].Comptype = lastcomp = val;
                }
            }
        }

        /* then iterate through the hunks and extract the needed data */
        uint last_self = 0;
        ulong last_parent = 0;
        var curoffset = firstoffs;
        for (uint blockIndex = 0; blockIndex < totalBlocks; blockIndex++)
        {
            var offset = curoffset;
            uint length = 0;
            ushort crc16 = 0;
            switch (map[blockIndex].Comptype)
            {
                /* base types */
                case compression_type.COMPRESSION_TYPE_0:
                case compression_type.COMPRESSION_TYPE_1:
                case compression_type.COMPRESSION_TYPE_2:
                case compression_type.COMPRESSION_TYPE_3:
                    curoffset += length = bitbuf.read(lengthbits);
                    crc16 = (ushort)bitbuf.read(16);
                    break;

                case compression_type.COMPRESSION_NONE:
                    curoffset += length = blocksize;
                    crc16 = (ushort)bitbuf.read(16);
                    break;

                case compression_type.COMPRESSION_SELF:
                    last_self = (uint)(offset = bitbuf.read(selfbits));
                    break;

                /* pseudo-types; convert into base types */
                case compression_type.COMPRESSION_SELF_1:
                    last_self++;
                    goto case compression_type.COMPRESSION_SELF_0;

                case compression_type.COMPRESSION_SELF_0:
                    map[blockIndex].Comptype = compression_type.COMPRESSION_SELF;
                    offset = last_self;
                    break;

                case compression_type.COMPRESSION_PARENT_SELF:
                    map[blockIndex].Comptype = compression_type.COMPRESSION_PARENT;
                    last_parent = offset = (((ulong)blockIndex) * ((ulong)blocksize)) / unitbytes;
                    break;

                case compression_type.COMPRESSION_PARENT:
                    offset = bitbuf.read(parentbits);
                    last_parent = offset;
                    break;

                case compression_type.COMPRESSION_PARENT_1:
                    last_parent += blocksize / unitbytes;
                    goto case compression_type.COMPRESSION_PARENT_0;
                case compression_type.COMPRESSION_PARENT_0:
                    map[blockIndex].Comptype = compression_type.COMPRESSION_PARENT;
                    offset = last_parent;
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
            return chd_error.CHDERR_DECOMPRESSION_ERROR;

        return chd_error.CHDERR_NONE;
    }

}
