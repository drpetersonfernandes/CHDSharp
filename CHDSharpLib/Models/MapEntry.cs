namespace CHDSharp.Models;

internal class MapEntry
{
    public CompressionType Comptype;
    public uint Length; // length of compressed data
    public ulong Offset; // offset of compressed data in file. Also index of source block for COMPRESSION_SELF
    public uint? Crc; // V3 & V4
    public ushort? Crc16; // V5

    public MapEntry SelfMapEntry = null!; // link to self MapEntry data used in COMPRESSION_SELF (replaces offset index)

    //Used to optimmize block reading so that any block in only decompressed once.
    public int UseCount;

    public byte[] BuffIn = null!;
    public byte[] BuffOutCache = null!;
    public byte[] BuffOut = null!;

    // Used in Parallel decompress to keep the blocks in order when hashing.
    public bool Processed;


    // Used to calculate which blocks should have buffered copies kept.
    public int UsageWeight;
    public bool KeepBufferCopy;
}
