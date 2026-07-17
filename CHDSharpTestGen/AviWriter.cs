namespace CHDSharpTestGen;

/// <summary>Writes a minimal AVI file (uncompressed YUY2 video + 16-bit PCM audio) accepted by
/// MAME's aviio reader, for use with chdman -createav / createld (AVHuff codec).</summary>
internal static class AviWriter
{
    public static void Write(string path, int width, int height, int fps, int frames, int sampleRate)
    {
        var frameBytes = width * height * 2;
        var samplesPerFrame = sampleRate / fps; // must divide exactly (44100 / 60 = 735)
        var totalSamples = samplesPerFrame * frames;

        var videoFrames = BuildVideoFrames(width, height, frames);
        var audio = BuildAudio(totalSamples, sampleRate);

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        // RIFF header (sizes patched later)
        w.Write("RIFF"u8);
        var riffSizePos = ms.Position;
        w.Write(0);
        w.Write("AVI "u8);

        // LIST hdrl
        var hdrlSizePos = BeginList(w, "hdrl");

        // avih
        Chunk(w, "avih", bw =>
        {
            bw.Write((uint)(1_000_000 / fps));           // dwMicroSecPerFrame
            bw.Write((uint)(frameBytes * fps));          // dwMaxBytesPerSec
            bw.Write(0u);                                // dwPaddingGranularity
            bw.Write(0x10u);                             // dwFlags = AVIF_HASINDEX
            bw.Write((uint)frames);                      // dwTotalFrames
            bw.Write(0u);                                // dwInitialFrames
            bw.Write(2u);                                // dwStreams
            bw.Write((uint)frameBytes);                  // dwSuggestedBufferSize
            bw.Write((uint)width);
            bw.Write((uint)height);
            bw.Write(0u);
            bw.Write(0u);
            bw.Write(0u);
            bw.Write(0u);
        });

        // video stream
        var strlVSizePos = BeginList(w, "strl");
        Chunk(w, "strh", bw =>
        {
            bw.Write("vids"u8);                          // fccType
            bw.Write("YUY2"u8);                          // fccHandler
            bw.Write(0u);                                // dwFlags
            bw.Write((ushort)0);
            bw.Write((ushort)0);     // priority, language
            bw.Write(0u);                                // dwInitialFrames
            bw.Write(1u);                                // dwScale
            bw.Write((uint)fps);                         // dwRate
            bw.Write(0u);                                // dwStart
            bw.Write((uint)frames);                      // dwLength
            bw.Write((uint)frameBytes);                  // dwSuggestedBufferSize
            bw.Write(0xFFFFFFFFu);                       // dwQuality
            bw.Write(0u);                                // dwSampleSize
            bw.Write((short)0);
            bw.Write((short)0);       // rcFrame
            bw.Write((short)width);
            bw.Write((short)height);
        });
        Chunk(w, "strf", bw =>
        {
            bw.Write(40u);                               // biSize
            bw.Write(width);
            bw.Write(height);
            bw.Write((ushort)1);                         // biPlanes
            bw.Write((ushort)16);                        // biBitCount
            bw.Write("YUY2"u8);                          // biCompression
            bw.Write((uint)frameBytes);                  // biSizeImage
            bw.Write(0);
            bw.Write(0);
            bw.Write(0u);
            bw.Write(0u);
        });
        EndList(w, strlVSizePos);

        // audio stream
        var strlASizePos = BeginList(w, "strl");
        Chunk(w, "strh", bw =>
        {
            bw.Write("auds"u8);                          // fccType
            bw.Write(0u);                                // fccHandler
            bw.Write(0u);                                // dwFlags
            bw.Write((ushort)0);
            bw.Write((ushort)0);
            bw.Write(0u);                                // dwInitialFrames
            bw.Write(1u);                                // dwScale
            bw.Write((uint)sampleRate);                  // dwRate
            bw.Write(0u);                                // dwStart
            bw.Write((uint)totalSamples);                // dwLength
            bw.Write((uint)(samplesPerFrame * 2));       // dwSuggestedBufferSize
            bw.Write(0xFFFFFFFFu);                       // dwQuality
            bw.Write(2u);                                // dwSampleSize (block align)
            bw.Write(0L);                                // rcFrame
        });
        Chunk(w, "strf", bw =>
        {
            bw.Write((ushort)1);                         // wFormatTag = PCM
            bw.Write((ushort)1);                         // nChannels
            bw.Write((uint)sampleRate);
            bw.Write((uint)(sampleRate * 2));            // nAvgBytesPerSec
            bw.Write((ushort)2);                         // nBlockAlign
            bw.Write((ushort)16);                        // wBitsPerSample
        });
        EndList(w, strlASizePos);

        EndList(w, hdrlSizePos);

        // LIST movi
        var moviSizePos = BeginList(w, "movi");
        var moviStart = moviSizePos + 4; // offset base for idx1 (points at 'movi' fourcc)
        var index = new List<(byte[] id, uint offset, uint length)>();

        for (var f = 0; f < frames; f++)
        {
            // aviio expects '00dc' (compressed) chunks for any stream with a non-zero biCompression
            index.Add(("00dc"u8.ToArray(), (uint)(ms.Position - moviStart), (uint)frameBytes));
            w.Write("00dc"u8);
            w.Write((uint)frameBytes);
            w.Write(videoFrames[f]);

            var audioBytes = samplesPerFrame * 2;
            index.Add(("01wb"u8.ToArray(), (uint)(ms.Position - moviStart), (uint)audioBytes));
            w.Write("01wb"u8);
            w.Write((uint)audioBytes);
            w.Write(audio, f * audioBytes, audioBytes);
        }
        EndList(w, moviSizePos);

        // idx1
        Chunk(w, "idx1", bw =>
        {
            foreach (var (id, offset, length) in index)
            {
                bw.Write(id);
                bw.Write(0x10u); // AVIIF_KEYFRAME
                bw.Write(offset);
                bw.Write(length);
            }
        });

        PatchSize(ms, riffSizePos);
        File.WriteAllBytes(path, ms.ToArray());
    }

    private static byte[][] BuildVideoFrames(int width, int height, int frames)
    {
        var result = new byte[frames][];
        for (var f = 0; f < frames; f++)
        {
            var frame = new byte[width * height * 2];
            var barX = (f * 3) % width;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var inBar = x >= barX && x < barX + 6;
                    var luma = inBar ? (byte)235 : (byte)(16 + (x * 3 + y * 2 + f) % 200);
                    var chroma = inBar ? (byte)64 : (byte)128;
                    frame[(y * width + x) * 2] = luma;
                    frame[(y * width + x) * 2 + 1] = chroma;
                }
            }
            result[f] = frame;
        }
        return result;
    }

    private static byte[] BuildAudio(int totalSamples, int sampleRate)
    {
        var audio = new byte[totalSamples * 2];
        double phase = 0;
        for (var i = 0; i < totalSamples; i++)
        {
            phase += 2 * Math.PI * (440 + (i % sampleRate) / 200.0) / sampleRate;
            var s = (short)(Math.Sin(phase) * 10000);
            audio[i * 2] = (byte)s;
            audio[i * 2 + 1] = (byte)(s >> 8);
        }
        return audio;
    }

    private static long BeginList(BinaryWriter w, string type)
    {
        w.Write("LIST"u8);
        var sizePos = w.BaseStream.Position;
        w.Write(0);
        w.Write(System.Text.Encoding.ASCII.GetBytes(type));
        return sizePos;
    }

    private static void EndList(BinaryWriter w, long sizePos)
    {
        PatchSize((MemoryStream)w.BaseStream, sizePos);
    }

    private static void Chunk(BinaryWriter w, string id, Action<BinaryWriter> body)
    {
        w.Write(System.Text.Encoding.ASCII.GetBytes(id));
        var sizePos = w.BaseStream.Position;
        w.Write(0);
        body(w);
        PatchSize((MemoryStream)w.BaseStream, sizePos);
        if ((w.BaseStream.Position & 1) == 1)
            w.Write((byte)0); // chunks are word-aligned
    }

    private static void PatchSize(MemoryStream ms, long sizePos)
    {
        var end = ms.Position;
        var size = (uint)(end - sizePos - 4);
        ms.Position = sizePos;
        Span<byte> b = stackalloc byte[4];
        b[0] = (byte)size;
        b[1] = (byte)(size >> 8);
        b[2] = (byte)(size >> 16);
        b[3] = (byte)(size >> 24);
        ms.Write(b);
        ms.Position = end;
    }
}
