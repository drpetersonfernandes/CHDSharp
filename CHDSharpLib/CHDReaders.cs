using System.IO.Compression;
using CHDSharp.LZMA;
using CHDSharp.Models;
using CHDSharp.Models.Utils;
using CHDSharp.Utils;

namespace CHDSharp;

internal delegate chdError ChdReader(byte[] buffIn, int buffInLength, byte[] buffOut, int buffOutLength, CHDCodec codec);

internal static partial class ChdReaders
{
    internal static chdError Zlib(byte[] buffIn, int buffInLength, byte[] buffOut, int buffOutLength, CHDCodec codec)
    {
        return Zlib(buffIn, 0, buffInLength, buffOut, buffOutLength);
    }

    private static chdError Zlib(byte[] buffIn, int buffInStart, int buffInLength, byte[] buffOut, int buffOutLength)
    {
        using var memStream = new MemoryStream(buffIn, buffInStart, buffInLength, false);
        using var compStream = new DeflateStream(memStream, CompressionMode.Decompress, true);
        var bytesRead = 0;
        while (bytesRead < buffOutLength)
        {
            var bytes = compStream.Read(buffOut, bytesRead, buffOutLength - bytesRead);
            if (bytes == 0)
                return chdError.CHDERRINVALIDDATA;

            bytesRead += bytes;
        }

        return chdError.CHDERRNONE;
    }

    internal static chdError Zstd(byte[] buffIn, int buffInLength, byte[] buffOut, int buffOutLength, CHDCodec codec)
    {
        return Zstd(buffIn, 0, buffInLength, buffOut, 0, buffOutLength, codec);
    }

    private static chdError Zstd(byte[] buffIn, int buffInStart, int buffInLength, byte[] buffOut, int buffOutStart, int buffOutLength, CHDCodec codec)
    {
        try
        {
            var written = codec.bZstd.Unwrap(
                new ReadOnlySpan<byte>(buffIn, buffInStart, buffInLength),
                new Span<byte>(buffOut, buffOutStart, buffOutLength));
            if (written != buffOutLength)
                return chdError.CHDERRDECOMPRESSIONERROR;
        }
        catch
        {
            return chdError.CHDERRDECOMPRESSIONERROR;
        }

        return chdError.CHDERRNONE;
    }

    internal static chdError Lzma(byte[] buffIn, int buffInLength, byte[] buffOut, int buffOutLength, CHDCodec codec)
    {
        return Lzma(buffIn, 0, buffInLength, buffOut, buffOutLength, codec);
    }

    private static chdError Lzma(byte[] buffIn, int buffInStart, int compsize, byte[] buffOut, int buffOutLength, CHDCodec codec)
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

        if (codec.blzma == null)
        {
            codec.blzma = new byte[buffOutLength];
        }

        using var memStream = new MemoryStream(buffIn, buffInStart, compsize, false);
        using Stream compStream = new LzmaStream(properties, memStream, -1, -1, null!, false, codec.blzma);
        var bytesRead = 0;
        while (bytesRead < buffOutLength)
        {
            var bytes = compStream.Read(buffOut, bytesRead, buffOutLength - bytesRead);
            if (bytes == 0)
                return chdError.CHDERRINVALIDDATA;

            bytesRead += bytes;
        }

        return chdError.CHDERRNONE;
    }

    internal static chdError Huffman(byte[] buffIn, int buffInLength, byte[] buffOut, int buffOutLength, CHDCodec codec)
    {
        if (codec.bHuffman == null)
        {
            codec.bHuffman = new ushort[1 << 16];
        }

        var bitbuf = new BitStream(buffIn, 0, buffInLength);
        var hd = new HuffmanDecoder(256, 16, bitbuf, codec.bHuffman);

        if (hd.ImportTreeHuffman() != huffman_error.HUFFERR_NONE)
            return chdError.CHDERRINVALIDDATA;

        for (var j = 0; j < buffOutLength; j++)
        {
            buffOut[j] = (byte)hd.DecodeOne();
        }

        return chdError.CHDERRNONE;
    }

    internal static chdError Flac(byte[] buffIn, int buffInLength, byte[] buffOut, int buffOutLength, CHDCodec codec)
    {
        var endianType = buffIn[0];
        //CHD adds a leading char to indicate endian. Not part of the flac format.
        var swapEndian = (endianType == 'B'); //'L'ittle / 'B'ig
        return Flac(buffIn, 1, buffInLength, buffOut, buffOutLength, swapEndian, codec, out _);
    }

    private static chdError Flac(byte[] buffIn, int buffInStart, int buffInLength, byte[] buffOut, int buffOutLength, bool swapEndian, CHDCodec codec, out int srcPos)
    {
        // CHD FLAC data is HEADERLESS - it is a bare sequence of FLAC frames with
        // NO fLaC stream marker and NO STREAMINFO metadata block. There is nothing
        // to read the sample rate / channels / bit-depth from; that information is
        // implicit in the CHD format itself.
        //
        // libchdr does the same thing: flac_codec_decompress() hardcodes
        // flac_decoder_reset(..., 44100, 2, ...). The 44100 is arbitrary - sample
        // rate does NOT affect FLAC sample-value decoding (AudioDecoder only
        // validates that the per-frame rate code is a standard one and otherwise
        // ignores it). What matters for correct decoding is:
        //   - bits-per-sample = 16  (always true for CHD FLAC)
        //   - channel count   = 2   (CD/raw FLAC hunks are 16-bit stereo samples)
        // Both are fixed by the CHD format and validated against each frame header
        // inside DecodeFrame(); the actual per-frame block size is also read from
        // the frame header, so no block-size hint is required here.

        srcPos = buffInStart;
        var dstPos = 0;
        //this may require some error handling. Hopefully the while condition is reliable
        while (dstPos < buffOutLength)
        {
            var read = codec.FLAC_audioDecoder.DecodeFrame(buffIn, srcPos, buffInLength - srcPos);
            codec.FLAC_audioDecoder.Read(codec.FLAC_audioBuffer, (int)codec.FLAC_audioDecoder.Remaining);
            Array.Copy(codec.FLAC_audioBuffer.Bytes, 0, buffOut, dstPos, codec.FLAC_audioBuffer.ByteLength);
            dstPos += codec.FLAC_audioBuffer.ByteLength;
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

        return chdError.CHDERRNONE;
    }

    /******************* CD decoders **************************/

    private const int CD_MAX_SECTOR_DATA = 2352;
    private const int CD_MAX_SUBCODE_DATA = 96;
    private const int cdFrameSize = CD_MAX_SECTOR_DATA + CD_MAX_SUBCODE_DATA;

    private static readonly byte[] SCdSyncHeader = [0x00, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x00];

    internal static chdError Cdzlib(byte[] buffIn, int buffInLength, byte[] buffOut, int buffOutLength, CHDCodec codec)
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

        var err = Zlib(buffIn, (int)headerBytes, complenBase, codec.bSector, frames * CD_MAX_SECTOR_DATA);
        if (err != chdError.CHDERRNONE)
            return err;

        err = Zlib(buffIn, headerBytes + complenBase, buffInLength - headerBytes - complenBase, codec.bSubcode, frames * CD_MAX_SUBCODE_DATA);
        if (err != chdError.CHDERRNONE)
            return err;

        /* reassemble the data */
        for (var framenum = 0; framenum < frames; framenum++)
        {
            Array.Copy(codec.bSector, framenum * CD_MAX_SECTOR_DATA, buffOut, framenum * cdFrameSize, CD_MAX_SECTOR_DATA);
            Array.Copy(codec.bSubcode, framenum * CD_MAX_SUBCODE_DATA, buffOut, framenum * cdFrameSize + CD_MAX_SECTOR_DATA, CD_MAX_SUBCODE_DATA);

            // reconstitute the ECC data and sync header
            var sectorStart = framenum * cdFrameSize;
            if ((buffIn[framenum / 8] & (1 << (framenum % 8))) != 0)
            {
                Array.Copy(SCdSyncHeader, 0, buffOut, sectorStart, SCdSyncHeader.Length);
                CdRom.EccGenerate(buffOut, sectorStart);
            }
        }

        return chdError.CHDERRNONE;
    }

    internal static chdError Cdlzma(byte[] buffIn, int buffInLength, byte[] buffOut, int buffOutLength, CHDCodec codec)
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

        var err = Lzma(buffIn, headerBytes, complenBase, codec.bSector, frames * CD_MAX_SECTOR_DATA, codec);
        if (err != chdError.CHDERRNONE)
            return err;

        err = Zlib(buffIn, headerBytes + complenBase, buffInLength - headerBytes - complenBase, codec.bSubcode, frames * CD_MAX_SUBCODE_DATA);
        if (err != chdError.CHDERRNONE)
            return err;

        /* reassemble the data */
        for (var framenum = 0; framenum < frames; framenum++)
        {
            Array.Copy(codec.bSector, framenum * CD_MAX_SECTOR_DATA, buffOut, framenum * cdFrameSize, CD_MAX_SECTOR_DATA);
            Array.Copy(codec.bSubcode, framenum * CD_MAX_SUBCODE_DATA, buffOut, framenum * cdFrameSize + CD_MAX_SECTOR_DATA, CD_MAX_SUBCODE_DATA);

            // reconstitute the ECC data and sync header
            var sectorStart = framenum * cdFrameSize;
            if ((buffIn[framenum / 8] & (1 << (framenum % 8))) != 0)
            {
                Array.Copy(SCdSyncHeader, 0, buffOut, sectorStart, SCdSyncHeader.Length);
                CdRom.EccGenerate(buffOut, sectorStart);
            }
        }

        return chdError.CHDERRNONE;
    }

    internal static chdError Cdflac(byte[] buffIn, int buffInLength, byte[] buffOut, int buffOutLength, CHDCodec codec)
    {
        var frames = buffOutLength / cdFrameSize;

        var err = Flac(buffIn, 0, buffInLength, codec.bSector, frames * CD_MAX_SECTOR_DATA, true, codec, out var pos);
        if (err != chdError.CHDERRNONE)
            return err;

        err = Zlib(buffIn, pos, buffInLength - pos, codec.bSubcode, frames * CD_MAX_SUBCODE_DATA);
        if (err != chdError.CHDERRNONE)
            return err;

        /* reassemble the data */
        for (var framenum = 0; framenum < frames; framenum++)
        {
            Array.Copy(codec.bSector, framenum * CD_MAX_SECTOR_DATA, buffOut, framenum * cdFrameSize, CD_MAX_SECTOR_DATA);
            Array.Copy(codec.bSubcode, framenum * CD_MAX_SUBCODE_DATA, buffOut, framenum * cdFrameSize + CD_MAX_SECTOR_DATA, CD_MAX_SUBCODE_DATA);
        }

        return chdError.CHDERRNONE;
    }


    internal static chdError Cdzstd(byte[] buffIn, int buffInLength, byte[] buffOut, int buffOutLength, CHDCodec codec)
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

        var err = Zstd(buffIn, headerBytes, complenBase, codec.bSector, 0, frames * CD_MAX_SECTOR_DATA, codec);
        if (err != chdError.CHDERRNONE)
            return err;

        err = Zstd(buffIn, headerBytes + complenBase, buffInLength - headerBytes - complenBase, codec.bSubcode, 0, frames * CD_MAX_SUBCODE_DATA, codec);
        if (err != chdError.CHDERRNONE)
            return err;

        /* reassemble the data */
        for (var framenum = 0; framenum < frames; framenum++)
        {
            Array.Copy(codec.bSector, framenum * CD_MAX_SECTOR_DATA, buffOut, framenum * cdFrameSize, CD_MAX_SECTOR_DATA);
            Array.Copy(codec.bSubcode, framenum * CD_MAX_SUBCODE_DATA, buffOut, framenum * cdFrameSize + CD_MAX_SECTOR_DATA, CD_MAX_SUBCODE_DATA);

            // reconstitute the ECC data and sync header
            var sectorStart = framenum * cdFrameSize;
            if ((buffIn[framenum / 8] & (1 << (framenum % 8))) != 0)
            {
                Array.Copy(SCdSyncHeader, 0, buffOut, sectorStart, SCdSyncHeader.Length);
                CdRom.EccGenerate(buffOut, sectorStart);
            }
        }

        return chdError.CHDERRNONE;
    }
}