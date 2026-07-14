namespace CHDSharp.Models;

internal class ChdHeader
{
    public chdCodec[] Compression = null!;
    public ChdReader[] ChdReader = null!;

    public ulong Totalbytes;
    public uint Blocksize;
    public uint Totalblocks;

    // V5: size of a "unit" used for parent block address translation.
    // For V1-V4 parent entries the offset is a direct hunk index, so unitbytes
    // is only meaningful for V5 (set to blocksize as a harmless default otherwise).
    public uint Unitbytes;

    // True when the V5 map is the uncompressed variant. In that map an offset
    // word of 0 means "read this hunk from the parent" (or zero-fill if none).
    public bool UncompressedMap;

    public MapEntry[] Map = null!;

    public byte[] Md5 = null!; // just compressed data
    public byte[] Rawsha1 = null!; // just compressed data
    public byte[] Sha1 = null!; // includes the meta data

    public byte[] Parentmd5 = null!;
    public byte[] Parentsha1 = null!;

    public ulong Metaoffset;
}
