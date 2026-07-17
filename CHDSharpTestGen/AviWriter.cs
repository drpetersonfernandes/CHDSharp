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
        Chunk(w, "avih", () =>
        {
            w.Write((uint)(1_000_000 / fps));           // dwMicroSecPerFrame
            w.Write((uint)(frameBytes * fps));          // dwMaxBytesPerSec
            w.Write(0u);                                // dwPaddingGranularity
            w.Write(0x10u);                             // dwFlags = AVIF_HASINDEX
            w.Write((uint)frames);                      // dwTotalFrames
            w.Write(0u);                                // dwInitialFrames
            w.Write(2u);                                // dwStreams
            w.Write((uint)frameBytes);                  // dwSuggestedBufferSize
            w.Write((uint)width);
            w.Write((uint)height);
            w.Write(0u);
            w.Write(0u);
            w.Write(0u);
            w.Write(0u);
        });

        // video stream
        var strlVSizePos = BeginList(w, "strl");
        Chunk(w, "strh", () =>
        {
            w.Write("vids"u8);                          // fccType
            w.Write("YUY2"u8);                          // fccHandler
            w.Write(0u);                                // dwFlags
            w.Write((ushort)0);
            w.Write((ushort)0);     // priority, language
            w.Write(0u);                                // dwInitialFrames
            w.Write(1u);                                // dwScale
            w.Write((uint)fps);                         // dwRate
            w.Write(0u);                                // dwStart
            w.Write((uint)frames);                      // dwLength
            w.Write((uint)frameBytes);                  // dwSuggestedBufferSize
            w.Write(0xFFFFFFFFu);                       // dwQuality
            w.Write(0u);                                // dwSampleSize
            w.Write((short)0);
            w.Write((short)0);       // rcFrame
            w.Write((short)width);
            w.Write((short)height);
        });
        Chunk(w, "strf", () =>
        {
            w.Write(40u);                               // biSize
            w.Write(width);
            w.Write(height);
            w.Write((ushort)1);                         // biPlanes
            w.Write((ushort)16);                        // biBitCount
            w.Write("YUY2"u8);                          // biCompression
            w.Write((uint)frameBytes);                  // biSizeImage
            w.Write(0);
            w.Write(0);
            w.Write(0u);
            w.Write(0u);
        });
        EndList(w, strlVSizePos);

        // audio stream
        var strlASizePos = BeginList(w, "strl");
        Chunk(w, "strh", () =>
        {
            w.Write("auds"u8);                          // fccType
            w.Write(0u);                                // fccHandler
            w.Write(0u);                                // dwFlags
            w.Write((ushort)0);
            w.Write((ushort)0);
            w.Write(0u);                                // dwInitialFrames
            w.Write(1u);                                // dwScale
            w.Write((uint)sampleRate);                  // dwRate
            w.Write(0u);                                // dwStart
            w.Write((uint)totalSamples);                // dwLength
            w.Write((uint)(samplesPerFrame * 2));       // dwSuggestedBufferSize
            w.Write(0xFFFFFFFFu);                       // dwQuality
            w.Write(2u);                                // dwSampleSize (block align)
            w.Write(0L);                                // rcFrame
        });
        Chunk(w, "strf", () =>
        {
            w.Write((ushort)1);                         // wFormatTag = PCM
            w.Write((ushort)1);                         // nChannels
            w.Write((uint)sampleRate);
            w.Write((uint)(sampleRate * 2));            // nAvgBytesPerSec
            w.Write((ushort)2);                         // nBlockAlign
            w.Write((ushort)16);                        // wBitsPerSample
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
        Chunk(w, "idx1", () =>
        {
            foreach (var (id, offset, length) in index)
            {
                w.Write(id);
                w.Write(0x10u); // AVIIF_KEYFRAME
                w.Write(offset);
                w.Write(length);
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

    private static void Chunk(BinaryWriter w, string id, Action body)
    {
        w.Write(System.Text.Encoding.ASCII.GetBytes(id));
        var sizePos = w.BaseStream.Position;
        w.Write(0);
        body();
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
