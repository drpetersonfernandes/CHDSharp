using CHDSharp.Flac;
using CHDSharp.Models.Flac.FlacDeps;
using ZstdSharp;

namespace CHDSharp.Models;

internal class CHDCodec
{
    internal AudioPcmConfig? FlacSettings;
    internal AudioDecoder? FlacAudioDecoder;
    internal AudioBuffer? FlacAudioBuffer;

    internal AudioPcmConfig? AvhuffSettings;
    internal AudioDecoder? AvhuffAudioDecoder;

    internal byte[]? BSector;
    internal byte[]? BSubcode;

    internal byte[]? Blzma;

    internal Decompressor? BZstd;

    internal ushort[]? BHuffman;
    internal ushort[]? BHuffmanHi;
    internal ushort[]? BHuffmanLo;

    internal ushort[]? BHuffmanY;
    internal ushort[]? BHuffmanCb;
    internal ushort[]? BHuffmanCr;
}
