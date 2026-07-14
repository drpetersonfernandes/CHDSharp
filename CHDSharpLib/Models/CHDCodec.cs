using CHDSharp.Flac;
using CHDSharp.Models.Flac.FlacDeps;
using ZstdSharp;

namespace CHDSharp.Models;

internal class CHDCodec
{
    internal AudioPcmConfig FlacSettings = null!;
    internal readonly AudioDecoder FlacAudioDecoder = null!;
    internal readonly AudioBuffer FlacAudioBuffer = null!;

    internal readonly AudioPcmConfig AvhuffSettings = null!;
    internal readonly AudioDecoder AvhuffAudioDecoder = null!;

    internal readonly byte[] BSector = null!;
    internal readonly byte[] BSubcode = null!;

    internal byte[] Blzma = null!;

    internal readonly Decompressor BZstd = null!;

    internal ushort[] BHuffman = null!;
    internal ushort[] BHuffmanHi = null!;
    internal ushort[] BHuffmanLo = null!;

    internal ushort[] BHuffmanY = null!;
    internal ushort[] BHuffmanCb = null!;
    internal ushort[] BHuffmanCr = null!;
}
