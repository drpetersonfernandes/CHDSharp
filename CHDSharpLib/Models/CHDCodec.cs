using CHDSharp.Flac;
using CHDSharp.Models.Flac.FlacDeps;
using ZstdSharp;

namespace CHDSharp.Models;

/// <summary>Holds per-codec state and scratch buffers used across multiple hunk decompressions, avoiding repeated allocations.</summary>
internal class CHDCodec
{
    /// <summary>FLAC audio configuration (16-bit, 2-channel, 44100 Hz).</summary>
    internal AudioPcmConfig? FlacSettings;
    /// <summary>Reusable FLAC audio decoder instance.</summary>
    internal AudioDecoder? FlacAudioDecoder;
    /// <summary>Reusable FLAC audio output buffer.</summary>
    internal AudioBuffer? FlacAudioBuffer;

    /// <summary>AVHuff audio configuration.</summary>
    internal AudioPcmConfig? AvhuffSettings;
    /// <summary>Reusable AVHuff audio decoder instance.</summary>
    internal AudioDecoder? AvhuffAudioDecoder;

    /// <summary>Scratch buffer for CD sector data reassembly.</summary>
    internal byte[]? BSector;
    /// <summary>Scratch buffer for CD subcode data reassembly.</summary>
    internal byte[]? BSubcode;

    /// <summary>Scratch buffer for LZMA decompression (reused across hunks).</summary>
    internal byte[]? Blzma;

    /// <summary>Reusable Zstandard decompressor instance.</summary>
    internal Decompressor? BZstd;

    /// <summary>Huffman lookup table for standard audio/sector Huffman decoding.</summary>
    internal ushort[]? BHuffman;
    /// <summary>Huffman lookup table for AVHuff video high-byte decoding.</summary>
    internal ushort[]? BHuffmanHi;
    /// <summary>Huffman lookup table for AVHuff video low-byte decoding.</summary>
    internal ushort[]? BHuffmanLo;

    /// <summary>Huffman lookup table for AVHuff video Y (luma) channel.</summary>
    internal ushort[]? BHuffmanY;
    /// <summary>Huffman lookup table for AVHuff video Cb (chroma blue) channel.</summary>
    internal ushort[]? BHuffmanCb;
    /// <summary>Huffman lookup table for AVHuff video Cr (chroma red) channel.</summary>
    internal ushort[]? BHuffmanCr;
}
