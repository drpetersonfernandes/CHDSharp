using CHDSharp.Flac;
using CHDSharp.Models.Flac.FlacDeps;
using ZstdSharp;

namespace CHDSharp.Models;

internal class CHDCodec
{
    internal AudioPcmConfig FLAC_settings = null!;
    internal AudioDecoder FLAC_audioDecoder = null!;
    internal AudioBuffer FLAC_audioBuffer = null!;


    internal AudioPcmConfig AVHUFF_settings = null!;
    internal AudioDecoder AVHUFF_audioDecoder = null!;


    internal byte[] bSector = null!;
    internal byte[] bSubcode = null!;

    internal byte[] blzma = null!;

    internal Decompressor bZstd = null!;

    internal ushort[] bHuffman = null!;
    internal ushort[] bHuffmanHi = null!;
    internal ushort[] bHuffmanLo = null!;

    internal ushort[] bHuffmanY = null!;
    internal ushort[] bHuffmanCB = null!;
    internal ushort[] bHuffmanCR = null!;
}
