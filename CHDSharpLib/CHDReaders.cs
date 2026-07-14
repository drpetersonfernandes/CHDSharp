using System.IO.Compression;
using CHDSharp.Flac;
using CHDSharp.LZMA;
using CHDSharp.Models;
using CHDSharp.Models.Flac.FlacDeps;
using CHDSharp.Models.Utils;
using CHDSharp.Utils;

namespace CHDSharp;

internal delegate ChdError ChdReader(byte[] buffIn, int buffInLength, byte[] buffOut, int buffOutLength, CHDCodec codec);

internal static partial class ChdReaders
{
    internal static ChdError Zlib(byte[] buffIn, int buffInLength, byte[] buffOut, int buffOutLength, CHDCodec codec)
    {
        return Zlib(buffIn, 0, buffInLength, buffOut, buffOutLength);
    }

    private static ChdError Zlib(byte[] buffIn, int buffInStart, int buffInLength, byte[] buffOut, int buffOutLength)
    {
        using var memStream = new MemoryStream(buffIn, buffInStart, buffInLength, false);
        using var compStream = new DeflateStream(memStream, CompressionMode.Decompress, true);
        var bytesRead = 0;
        while (bytesRead < buffOutLength)
        {
            var bytes = compStream.Read(buffOut, bytesRead, buffOutLength - bytesRead);
            if (bytes == 0)
                return ChdError.Chderrinvaliddata;

            bytesRead += bytes;
        }

        return ChdError.Chderrnone;
    }

    internal static ChdError Zstd(byte[] buffIn, int buffInLength, byte[] buffOut, int buffOutLength, CHDCodec codec)
    {
        return Zstd(buffIn, 0, buffInLength, buffOut, 0, buffOutLength, codec);
    }

    private static ChdError Zstd(byte[] buffIn, int buffInStart, int buffInLength, byte[] buffOut, int buffOutStart, int buffOutLength, CHDCodec codec)
    {
        codec.BZstd ??= new ZstdSharp.Decompressor();

        try
        {
            var written = codec.BZstd.Unwrap(
                new ReadOnlySpan<byte>(buffIn, buffInStart, buffInLength),
                new Span<byte>(buffOut, buffOutStart, buffOutLength));
            if (written != buffOutLength)
                return ChdError.Chderrdecompressionerror;
        }
        catch
        {
            return ChdError.Chderrdecompressionerror;
        }

        return ChdError.Chderrnone;
    }

    internal static ChdError Lzma(byte[] buffIn, int buffInLength, byte[] buffOut, int buffOutLength, CHDCodec codec)
    {
        return Lzma(buffIn, 0, buffInLength, buffOut, buffOutLength, codec);
    }

    private static ChdError Lzma(byte[] buffIn, int buffInStart, int compsize, byte[] buffOut, int buffOutLength, CHDCodec codec)
    {
        // CHD LZMA hunks are RAW, headerless LZMA payloads. There is no 5-byte
        // LZMA properties header stored in the stream (unlike a .lzma file).
        // Both MAME's chdman (encoder) and libchdr (decoder) use FIXED settings
        // and synthesise the properties rather than reading them:
        //   lc=3, lp=0, pb=2  =>  properties[0] = (pb*5 + lp)*9 + lc = 93  (== libchdr decoder_props[0])
        // The dictionary size only has to be >= the maximum back-reference
        // distance. Each hunk is compressed independently, so that distance is
        // always < hunkbytes; using buffOutLength (= hunkbytes) is therefore
        // always sufficient and keeps the reusable dictionary buffer small.
        // Do NOT try to read properties from the first bytes of buffIn - those
        // bytes are already compressed data and skipping them corrupts the hunk.
        var properties = new byte[5];
        const int posStateBits = 2;
        const int numLiteralPosStateBits = 0;
        const int numLiteralContextBits = 3;
        properties[0] = (byte)((posStateBits * 5 + numLiteralPosStateBits) * 9 + numLiteralContextBits);
        for (var j = 0; j < 4; j++)
        {
            properties[1 + j] = (byte)((buffOutLength >> (8 * j)) & 0xFF);
        }

        if (codec.Blzma == null)
        {
            codec.Blzma = new byte[buffOutLength];
        }

        using var memStream = new MemoryStream(buffIn, buffInStart, compsize, false);
        using Stream compStream = new LzmaStream(properties, memStream, -1, -1, null!, false, codec.Blzma);
        var bytesRead = 0;
        while (bytesRead < buffOutLength)
        {
            var bytes = compStream.Read(buffOut, bytesRead, buffOutLength - bytesRead);
            if (bytes == 0)
                return ChdError.Chderrinvaliddata;

            bytesRead += bytes;
        }

        return ChdError.Chderrnone;
    }

    internal static ChdError Huffman(byte[] buffIn, int buffInLength, byte[] buffOut, int buffOutLength, CHDCodec codec)
    {
        if (codec.BHuffman == null)
        {
            codec.BHuffman = new ushort[1 << 16];
        }

        var bitbuf = new BitStream(buffIn, 0, buffInLength);
        var hd = new HuffmanDecoder(256, 16, bitbuf, codec.BHuffman);

        if (hd.ImportTreeHuffman() != HuffmanError.HufferrNone)
            return ChdError.Chderrinvaliddata;

        for (var j = 0; j < buffOutLength; j++)
        {
            buffOut[j] = (byte)hd.DecodeOne();
        }

        return ChdError.Chderrnone;
    }

    internal static ChdError Flac(byte[] buffIn, int buffInLength, byte[] buffOut, int buffOutLength, CHDCodec codec)
    {
        var endianType = buffIn[0];
        //CHD adds a leading char to indicate endian. Not part of the flac format.
        var swapEndian = (endianType == 'B'); //'L'ittle / 'B'ig
        return Flac(buffIn, 1, buffInLength, buffOut, buffOutLength, swapEndian, codec, out _);
    }

    private static ChdError Flac(byte[] buffIn, int buffInStart, int buffInLength, byte[] buffOut, int buffOutLength, bool swapEndian, CHDCodec codec, out int srcPos)
    {
        codec.FlacSettings ??= new AudioPcmConfig(16, 2, 44100);
        codec.FlacAudioDecoder ??= new AudioDecoder(codec.FlacSettings);
        codec.FlacAudioBuffer ??= new AudioBuffer(codec.FlacSettings, buffOutLength);

        srcPos = buffInStart;
        var dstPos = 0;
        //this may require some error handling. Hopefully the while condition is reliable
        while (dstPos < buffOutLength)
        {
            var read = codec.FlacAudioDecoder.DecodeFrame(buffIn, srcPos, buffInLength - srcPos);
            codec.FlacAudioDecoder.Read(codec.FlacAudioBuffer, (int)codec.FlacAudioDecoder.Remaining);
            Array.Copy(codec.FlacAudioBuffer.Bytes, 0, buffOut, dstPos, codec.FlacAudioBuffer.ByteLength);
            dstPos += codec.FlacAudioBuffer.ByteLength;
            srcPos += read;
        }

        //Nanook - hack to support 16bit byte flipping - tested passes hunk CRC test
        if (swapEndian)
        {
            for (var i = 0; i < buffOutLength; i += 2)
            {
                (buffOut[i], buffOut[i + 1]) = (buffOut[i + 1], buffOut[i]);
            }
        }

        return ChdError.Chderrnone;
    }

    /******************* CD decoders **************************/

    private const int CD_MAX_SECTOR_DATA = 2352;
    private const int CD_MAX_SUBCODE_DATA = 96;
    private const int cdFrameSize = CD_MAX_SECTOR_DATA + CD_MAX_SUBCODE_DATA;

    private static readonly byte[] SCdSyncHeader = [0x00, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x00];

    internal static ChdError Cdzlib(byte[] buffIn, int buffInLength, byte[] buffOut, int buffOutLength, CHDCodec codec)
    {
        /* determine header bytes */
        var frames = buffOutLength / cdFrameSize;
        var complenBytes = (buffOutLength < 65536) ? 2 : 3;
        var eccBytes = (frames + 7) / 8;
        var headerBytes = eccBytes + complenBytes;

        /* extract compressed length of base */
        var complenBase = (buffIn[eccBytes + 0] << 8) | buffIn[eccBytes + 1];
        if (complenBytes > 2)
        {
            complenBase = (complenBase << 8) | buffIn[eccBytes + 2];
        }

        codec.BSector ??= new byte[frames * CD_MAX_SECTOR_DATA];
        codec.BSubcode ??= new byte[frames * CD_MAX_SUBCODE_DATA];

        var err = Zlib(buffIn, (int)headerBytes, complenBase, codec.BSector, frames * CD_MAX_SECTOR_DATA);
        if (err != ChdError.Chderrnone)
            return err;

        err = Zlib(buffIn, headerBytes + complenBase, buffInLength - headerBytes - complenBase, codec.BSubcode, frames * CD_MAX_SUBCODE_DATA);
        if (err != ChdError.Chderrnone)
            return err;

        /* reassemble the data */
        for (var framenum = 0; framenum < frames; framenum++)
        {
            Array.Copy(codec.BSector, framenum * CD_MAX_SECTOR_DATA, buffOut, framenum * cdFrameSize, CD_MAX_SECTOR_DATA);
            Array.Copy(codec.BSubcode, framenum * CD_MAX_SUBCODE_DATA, buffOut, framenum * cdFrameSize + CD_MAX_SECTOR_DATA, CD_MAX_SUBCODE_DATA);

            // reconstitute the ECC data and sync header
            var sectorStart = framenum * cdFrameSize;
            if ((buffIn[framenum / 8] & (1 << (framenum % 8))) != 0)
            {
                Array.Copy(SCdSyncHeader, 0, buffOut, sectorStart, SCdSyncHeader.Length);
                CdRom.EccGenerate(buffOut, sectorStart);
            }
        }

        return ChdError.Chderrnone;
    }

    internal static ChdError Cdlzma(byte[] buffIn, int buffInLength, byte[] buffOut, int buffOutLength, CHDCodec codec)
    {
        /* determine header bytes */
        var frames = buffOutLength / cdFrameSize;
        var complenBytes = (buffOutLength < 65536) ? 2 : 3;
        var eccBytes = (frames + 7) / 8;
        var headerBytes = eccBytes + complenBytes;

        /* extract compressed length of base */
        var complenBase = ((buffIn[eccBytes + 0] << 8) | buffIn[eccBytes + 1]);
        if (complenBytes > 2)
        {
            complenBase = (complenBase << 8) | buffIn[eccBytes + 2];
        }

        codec.BSector ??= new byte[frames * CD_MAX_SECTOR_DATA];
        codec.BSubcode ??= new byte[frames * CD_MAX_SUBCODE_DATA];

        var err = Lzma(buffIn, headerBytes, complenBase, codec.BSector, frames * CD_MAX_SECTOR_DATA, codec);
        if (err != ChdError.Chderrnone)
            return err;

        err = Zlib(buffIn, headerBytes + complenBase, buffInLength - headerBytes - complenBase, codec.BSubcode, frames * CD_MAX_SUBCODE_DATA);
        if (err != ChdError.Chderrnone)
            return err;

        /* reassemble the data */
        for (var framenum = 0; framenum < frames; framenum++)
        {
            Array.Copy(codec.BSector, framenum * CD_MAX_SECTOR_DATA, buffOut, framenum * cdFrameSize, CD_MAX_SECTOR_DATA);
            Array.Copy(codec.BSubcode, framenum * CD_MAX_SUBCODE_DATA, buffOut, framenum * cdFrameSize + CD_MAX_SECTOR_DATA, CD_MAX_SUBCODE_DATA);

            // reconstitute the ECC data and sync header
            var sectorStart = framenum * cdFrameSize;
            if ((buffIn[framenum / 8] & (1 << (framenum % 8))) != 0)
            {
                Array.Copy(SCdSyncHeader, 0, buffOut, sectorStart, SCdSyncHeader.Length);
                CdRom.EccGenerate(buffOut, sectorStart);
            }
        }

        return ChdError.Chderrnone;
    }

    internal static ChdError Cdflac(byte[] buffIn, int buffInLength, byte[] buffOut, int buffOutLength, CHDCodec codec)
    {
        var frames = buffOutLength / cdFrameSize;

        codec.BSector ??= new byte[frames * CD_MAX_SECTOR_DATA];
        codec.BSubcode ??= new byte[frames * CD_MAX_SUBCODE_DATA];

        var err = Flac(buffIn, 0, buffInLength, codec.BSector, frames * CD_MAX_SECTOR_DATA, true, codec, out var pos);
        if (err != ChdError.Chderrnone)
            return err;

        err = Zlib(buffIn, pos, buffInLength - pos, codec.BSubcode, frames * CD_MAX_SUBCODE_DATA);
        if (err != ChdError.Chderrnone)
            return err;

        /* reassemble the data */
        for (var framenum = 0; framenum < frames; framenum++)
        {
            Array.Copy(codec.BSector, framenum * CD_MAX_SECTOR_DATA, buffOut, framenum * cdFrameSize, CD_MAX_SECTOR_DATA);
            Array.Copy(codec.BSubcode, framenum * CD_MAX_SUBCODE_DATA, buffOut, framenum * cdFrameSize + CD_MAX_SECTOR_DATA, CD_MAX_SUBCODE_DATA);
        }

        return ChdError.Chderrnone;
    }


    internal static ChdError Cdzstd(byte[] buffIn, int buffInLength, byte[] buffOut, int buffOutLength, CHDCodec codec)
    {
        /* determine header bytes */
        var frames = buffOutLength / cdFrameSize;
        var complenBytes = (buffOutLength < 65536) ? 2 : 3;
        var eccBytes = (frames + 7) / 8;
        var headerBytes = eccBytes + complenBytes;

        /* extract compressed length of base */
        var complenBase = (buffIn[eccBytes + 0] << 8) | buffIn[eccBytes + 1];
        if (complenBytes > 2)
        {
            complenBase = (complenBase << 8) | buffIn[eccBytes + 2];
        }

        codec.BSector ??= new byte[frames * CD_MAX_SECTOR_DATA];
        codec.BSubcode ??= new byte[frames * CD_MAX_SUBCODE_DATA];
        codec.BZstd ??= new ZstdSharp.Decompressor();

        var err = Zstd(buffIn, headerBytes, complenBase, codec.BSector, 0, frames * CD_MAX_SECTOR_DATA, codec);
        if (err != ChdError.Chderrnone)
            return err;

        err = Zstd(buffIn, headerBytes + complenBase, buffInLength - headerBytes - complenBase, codec.BSubcode, 0, frames * CD_MAX_SUBCODE_DATA, codec);
        if (err != ChdError.Chderrnone)
            return err;

        /* reassemble the data */
        for (var framenum = 0; framenum < frames; framenum++)
        {
            Array.Copy(codec.BSector, framenum * CD_MAX_SECTOR_DATA, buffOut, framenum * cdFrameSize, CD_MAX_SECTOR_DATA);
            Array.Copy(codec.BSubcode, framenum * CD_MAX_SUBCODE_DATA, buffOut, framenum * cdFrameSize + CD_MAX_SECTOR_DATA, CD_MAX_SUBCODE_DATA);

            // reconstitute the ECC data and sync header
            var sectorStart = framenum * cdFrameSize;
            if ((buffIn[framenum / 8] & (1 << (framenum % 8))) != 0)
            {
                Array.Copy(SCdSyncHeader, 0, buffOut, sectorStart, SCdSyncHeader.Length);
                CdRom.EccGenerate(buffOut, sectorStart);
            }
        }

        return ChdError.Chderrnone;
    }
}