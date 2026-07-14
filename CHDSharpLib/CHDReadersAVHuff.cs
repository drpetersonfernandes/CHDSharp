using CHDSharp.Models;
using CHDSharp.Models.Flac.FlacDeps;
using CHDSharp.Models.Utils;
using CHDSharp.Utils;

namespace CHDSharp;

internal static partial class ChdReaders
{
    /*
     Source input buffer structure:

     Header:
     00     =  Size of the Meta Data to be put into the output buffer right after the header.
     01     =  Number of Audio Channel.
     02,03  =  Number of Audio sampled values per chunk.
     04,05  =  width in pixels of image.
     06,07  =  height in pixels of image.
     08,09  =  Size of the source data for the audio channels huffman trees. (set to 0xffff is using FLAC.)

     10,11  =  size of compressed audio channel 1
     12,13  =  size of compressed audio channel 2
     .
     .         (Max audio channels coded to 16)
     Total Header size = 10 + 2 * Number of Audio Channels.


     Meta Data: (Size from header 00)

     Audio Huffman Tree: (Size from header 08,09)

     Audio Compressed Data Channels: (Repeated for each Audio Channel, Size from Header starting at 10,11)

     Video Compressed Data:   Rest of Input Chuck.

    */

    internal static chdError AvHuff(byte[] buffIn, int buffInLength, byte[] buffOut, int buffOutLength, CHDCodec codec)
    {
        // extract info from the header
        if (buffInLength < 8)
            return chdError.CHDERRINVALIDDATA;

        uint metaDataLength = buffIn[0];
        uint audioChannels = buffIn[1];
        uint audioSamplesPerBlock = buffIn.ReadUInt16BE(2);
        uint videoWidth = buffIn.ReadUInt16BE(4);
        uint videoHeight = buffIn.ReadUInt16BE(6);

        var sourceTotalSize = 10 + 2 * audioChannels;
        // validate that the sizes make sense
        if (buffInLength < sourceTotalSize)
            return chdError.CHDERRINVALIDDATA;

        sourceTotalSize += metaDataLength;

        uint audioHuffmanTreeSize = buffIn.ReadUInt16BE(8);
        if (audioHuffmanTreeSize != 0xffff)
        {
            sourceTotalSize += audioHuffmanTreeSize;
        }

        var audioChannelCompressedSize = new uint?[16];
        for (var chnum = 0; chnum < audioChannels; chnum++)
        {
            audioChannelCompressedSize[chnum] = buffIn.ReadUInt16BE(10 + 2 * chnum);
            sourceTotalSize += audioChannelCompressedSize[chnum]!.Value;
        }

        if (sourceTotalSize >= buffInLength)
            return chdError.CHDERRINVALIDDATA;

        // starting offsets of source data
        var buffInIndex = 10 + 2 * audioChannels;


        uint destOffset = 0;
        // create a header
        buffOut[0] = (byte)'c';
        buffOut[1] = (byte)'h';
        buffOut[2] = (byte)'a';
        buffOut[3] = (byte)'v';
        buffOut[4] = (byte)metaDataLength;
        buffOut[5] = (byte)audioChannels;
        buffOut[6] = (byte)(audioSamplesPerBlock >> 8);
        buffOut[7] = (byte)audioSamplesPerBlock;
        buffOut[8] = (byte)(videoWidth >> 8);
        buffOut[9] = (byte)videoWidth;
        buffOut[10] = (byte)(videoHeight >> 8);
        buffOut[11] = (byte)videoHeight;
        destOffset += 12;

        var metaDestStart = destOffset;
        if (metaDataLength > 0)
        {
            Array.Copy(buffIn, (int)buffInIndex, buffOut, (int)metaDestStart, (int)metaDataLength);
            buffInIndex += metaDataLength;
            destOffset += metaDataLength;
        }

        var audioChannelDestStart = new uint?[16];
        for (var chnum = 0; chnum < audioChannels; chnum++)
        {
            audioChannelDestStart[chnum] = destOffset;
            destOffset += 2 * audioSamplesPerBlock;
        }

        var videoDestStart = destOffset;


        // decode the audio channels
        if (audioChannels > 0)
        {
            // decode the audio
            var err = DecodeAudio(audioChannels, audioSamplesPerBlock, buffIn, buffInIndex, audioHuffmanTreeSize, audioChannelCompressedSize, buffOut, audioChannelDestStart, codec);
            if (err != chdError.CHDERRNONE)
                return err;

            // advance the pointers past the data
            if (audioHuffmanTreeSize != 0xffff)
            {
                buffInIndex += audioHuffmanTreeSize;
            }

            for (var chnum = 0; chnum < audioChannels; chnum++)
            {
                buffInIndex += audioChannelCompressedSize[chnum]!.Value;
            }
        }

        // decode the video data
        if (videoWidth > 0 && videoHeight > 0)
        {
            var videostride = 2 * videoWidth;
            // decode the video
            var err = DecodeVideo(videoWidth, videoHeight, buffIn, buffInIndex, (uint)buffInLength - buffInIndex, buffOut, videoDestStart, videostride, codec);
            if (err != chdError.CHDERRNONE)
                return err;
        }

        var videoEnd = videoDestStart + videoWidth * videoHeight * 2;
        for (var index = videoEnd; index < buffOutLength; index++)
        {
            buffOut[index] = 0;
        }

        return chdError.CHDERRNONE;
    }


    private static chdError DecodeAudio(uint channels, uint samples, byte[] buffIn, uint buffInOffset, uint treesize, uint?[] audioChannelCompressedSize, byte[] buffOut, uint?[] audioChannelDestStart, CHDCodec codec)
    {
        // if the tree size is 0xffff, the streams are FLAC-encoded
        if (treesize == 0xffff)
        {
            var blockSize = (int)samples * 2;

            // loop over channels
            for (var channelNumber = 0; channelNumber < channels; channelNumber++)
            {
                // extract the size of this channel
                var sourceSize = audioChannelCompressedSize[channelNumber] ?? 0;

                var curdest = audioChannelDestStart[channelNumber];
                if (curdest != null)
                {
                    // AVHuff FLAC streams are headerless (no STREAMINFO), one FLAC
                    // stream PER audio channel, so channel count = 1 (mono). Sample
                    // rate is irrelevant to decoding (see notes in CHDReaders.flac);
                    // bit-depth is fixed at 16. These match how MAME encodes AVHuff
                    // audio, so the values are correct for every AVHuff CHD.
                    var audioBuffer = new AudioBuffer(codec.AVHUFF_settings, blockSize); //audio buffer to take decoded samples and read them to bytes.
                    var inPos = (int)buffInOffset;
                    var outPos = (int)audioChannelDestStart[channelNumber]!.Value;

                    while (outPos < blockSize + audioChannelDestStart[channelNumber])
                    {
                        int read;
                        if ((read = codec.AVHUFF_audioDecoder.DecodeFrame(buffIn, inPos, (int)sourceSize)) == 0)
                            break;

                        if (codec.AVHUFF_audioDecoder.Remaining != 0)
                        {
                            codec.AVHUFF_audioDecoder.Read(audioBuffer, (int)codec.AVHUFF_audioDecoder.Remaining);
                            Array.Copy(audioBuffer.Bytes, 0, buffOut, outPos, audioBuffer.ByteLength);
                            outPos += audioBuffer.ByteLength;
                        }

                        inPos += read;
                    }

                    for (var i = (int)audioChannelDestStart[channelNumber]!.Value; i < blockSize + audioChannelDestStart[channelNumber]!.Value; i += 2)
                    {
                        (buffOut[i], buffOut[i + 1]) = (buffOut[i + 1], buffOut[i]);
                    }
                }

                // advance to the next channel's data
                buffInOffset += sourceSize;
            }

            return chdError.CHDERRNONE;
        }

        // if we have a non-zero tree size, extract the trees
        HuffmanDecoder? mAudiohiDecoder = null;
        HuffmanDecoder? mAudioloDecoder = null;
        if (treesize != 0)
        {
            var bitbuf = new BitStream(buffIn, (int)buffInOffset, (int)treesize);

            if (codec.bHuffmanHi == null)
            {
                codec.bHuffmanHi = new ushort[1 << 16];
            }

            if (codec.bHuffmanLo == null)
            {
                codec.bHuffmanLo = new ushort[1 << 16];
            }

            mAudiohiDecoder = new HuffmanDecoder(256, 16, bitbuf, codec.bHuffmanHi);
            mAudioloDecoder = new HuffmanDecoder(256, 16, bitbuf, codec.bHuffmanLo);

            var hufferr = mAudiohiDecoder.ImportTreeRLE();
            if (hufferr != huffman_error.HUFFERR_NONE)
                return chdError.CHDERRINVALIDDATA;

            bitbuf.flush();
            hufferr = mAudioloDecoder.ImportTreeRLE();
            if (hufferr != huffman_error.HUFFERR_NONE || bitbuf.flush() != treesize)
                return chdError.CHDERRINVALIDDATA;

            buffInOffset += treesize;
        }

        // loop over channels
        for (var chnum = 0; chnum < channels; chnum++)
        {
            // only process if the data is requested
            var curdest = audioChannelDestStart[chnum];
            if (curdest != null)
            {
                var prevsample = 0;

                // if no huffman length, just copy the data
                if (treesize == 0)
                {
                    var cursource = buffInOffset;
                    for (var sampnum = 0; sampnum < samples; sampnum++)
                    {
                        var delta = (buffIn[cursource + 0] << 8) | buffIn[cursource + 1];
                        cursource += 2;

                        var newsample = prevsample + delta;
                        prevsample = newsample;

                        buffOut[(uint)curdest + 0] = (byte)(newsample >> 8);
                        buffOut[(uint)curdest + 1] = (byte)newsample;
                        curdest += 2;
                    }
                }

                // otherwise, Huffman-decode the data
                else
                {
                    var bitbuf = new BitStream(buffIn, (int)buffInOffset, (int)audioChannelCompressedSize[chnum]!.Value);
                    mAudiohiDecoder!.AssignBitStream(bitbuf);
                    mAudioloDecoder!.AssignBitStream(bitbuf);
                    for (var sampnum = 0; sampnum < samples; sampnum++)
                    {
                        var delta = (short)(mAudiohiDecoder.DecodeOne() << 8);
                        delta |= (short)mAudioloDecoder.DecodeOne();

                        var newsample = prevsample + delta;
                        prevsample = newsample;

                        buffOut[(uint)curdest + 0] = (byte)(newsample >> 8);
                        buffOut[(uint)curdest + 1] = (byte)newsample;
                        curdest += 2;
                    }

                    if (bitbuf.overflow())
                        return chdError.CHDERRINVALIDDATA;
                }
            }

            // advance to the next channel's data
            buffInOffset += audioChannelCompressedSize[chnum]!.Value;
        }

        return chdError.CHDERRNONE;
    }

    private static chdError DecodeVideo(uint width, uint height, byte[] buffIn, uint buffInOffset, uint buffInLength, byte[] buffOut, uint buffOutOffset, uint dstride, CHDCodec codec)
    {
        // The first video byte is MAME AVHuff's video-encoding marker. The high
        // bit (0x80) signals that the video stream is Huffman(+RLE) encoded, which
        // is the ONLY video encoding AVHuff produces. It is NOT a lossy/lossless
        // selector - AVHuff video is always this Huffman delta-RLE form, decoded
        // below. Any other value means an encoding we don't recognise.
        // (Note: libchdr 0.3.0 does not implement AVHuff at all, so there is no
        // additional "lossy" path to port - this is already the complete decode.)
        if ((buffIn[buffInOffset] & 0x80) == 0)
            return chdError.CHDERRINVALIDDATA;

        // skip the first byte
        var bitbuf = new BitStream(buffIn, (int)buffInOffset, (int)buffInLength);
        bitbuf.read(8);

        if (codec.bHuffmanY == null)
        {
            codec.bHuffmanY = new ushort[1 << 16];
        }

        if (codec.bHuffmanCB == null)
        {
            codec.bHuffmanCB = new ushort[1 << 16];
        }

        if (codec.bHuffmanCR == null)
        {
            codec.bHuffmanCR = new ushort[1 << 16];
        }

        var mYcontext = new HuffmanDecoderRLE(256 + 16, 16, bitbuf, codec.bHuffmanY);
        var mCbcontext = new HuffmanDecoderRLE(256 + 16, 16, bitbuf, codec.bHuffmanCB);
        var mCrcontext = new HuffmanDecoderRLE(256 + 16, 16, bitbuf, codec.bHuffmanCR);

        // import the tables
        var hufferr = mYcontext.ImportTreeRLE();
        if (hufferr != huffman_error.HUFFERR_NONE)
            return chdError.CHDERRINVALIDDATA;

        bitbuf.flush();
        hufferr = mCbcontext.ImportTreeRLE();
        if (hufferr != huffman_error.HUFFERR_NONE)
            return chdError.CHDERRINVALIDDATA;

        bitbuf.flush();
        hufferr = mCrcontext.ImportTreeRLE();
        if (hufferr != huffman_error.HUFFERR_NONE)
            return chdError.CHDERRINVALIDDATA;

        bitbuf.flush();

        // decode to the destination
        mYcontext.Reset();
        mCbcontext.Reset();
        mCrcontext.Reset();

        for (var dy = 0; dy < height; dy++)
        {
            var row = buffOutOffset + (uint)dy * dstride;
            for (var dx = 0; dx < width / 2; dx++)
            {
                buffOut[row + 0] = (byte)mYcontext.DecodeOne();
                buffOut[row + 1] = (byte)mCbcontext.DecodeOne();
                buffOut[row + 2] = (byte)mYcontext.DecodeOne();
                buffOut[row + 3] = (byte)mCrcontext.DecodeOne();
                row += 4;
            }

            mYcontext.FlushRLE();
            mCbcontext.FlushRLE();
            mCrcontext.FlushRLE();
        }

        // check for errors if we overflowed or decoded too little data
        if (bitbuf.overflow() || bitbuf.flush() != buffInLength)
            return chdError.CHDERRINVALIDDATA;

        return chdError.CHDERRNONE;
    }
}

