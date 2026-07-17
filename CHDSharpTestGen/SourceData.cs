using System.Text;

namespace CHDSharpTestGen;

/// <summary>Builds deterministic source images designed to force every CHD hunk encoding:
/// zero hunks, 8-byte repeating hunks (mini), compressible data (codec), incompressible data (none),
/// and duplicate hunks (self).</summary>
internal static class SourceData
{
    public const int HunkSize = 4096;
    public const int RawHunks = 128; // 512 KiB => CHS 16/4/16 for hdcomp/createhd

    private static readonly string[] Words =
    [
        "the", "quick", "brown", "fox", "jumps", "over", "lazy", "dog", "compressed", "hunks",
        "of", "data", "synthetic", "test", "corpus", "zlib", "lzma", "huffman", "flac", "zstd"
    ];

    /// <summary>Builds the primary 512 KiB raw image.</summary>
    public static byte[] BuildRawImage()
    {
        var img = new byte[RawHunks * HunkSize];
        var rng = new DetRng(0xC0FFEE01);

        for (var h = 0; h < RawHunks; h++)
        {
            var hunk = img.AsSpan(h * HunkSize, HunkSize);
            switch (h)
            {
                case < 8:
                    // zeros (mini in V3/V4, zero/self in V5)
                    break;
                case < 16:
                    FillRepeating8(hunk, [0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02, 0x03, (byte)h]);
                    break;
                case < 32:
                    FillText(hunk, rng);
                    break;
                case < 40:
                    rng.Fill(hunk); // incompressible => stored uncompressed
                    break;
                case < 48:
                    // duplicates of hunks 16..23 => self references
                    img.AsSpan((h - 24) * HunkSize, HunkSize).CopyTo(hunk);
                    break;
                default:
                    FillStructured(hunk, rng, h);
                    break;
            }
        }

        return img;
    }

    /// <summary>Builds the child variant: mostly identical to the parent image so most hunks
    /// become parent references, with a few modified hunks.</summary>
    public static byte[] BuildChildImage(byte[] parent)
    {
        var img = (byte[])parent.Clone();
        var rng = new DetRng(0xC0FFEE02);

        foreach (var h in new[] { 20, 21, 22, 23, 60, 61, 62, 63, 64, 65, 66, 67, 68, 69 })
            rng.Fill(img.AsSpan(h * HunkSize, HunkSize));

        img.AsSpan(100 * HunkSize, HunkSize).Clear();
        return img;
    }

    /// <summary>CD data track: sectors * 2048 bytes of mixed content.</summary>
    public static byte[] BuildCdDataTrack(int sectors)
    {
        var data = new byte[sectors * 2048];
        var rng = new DetRng(0xC0FFEE03);

        for (var s = 0; s < sectors; s++)
        {
            var sec = data.AsSpan(s * 2048, 2048);
            switch (s % 4)
            {
                case 0: FillText(sec, rng); break;
                case 1: FillStructured(sec, rng, s); break;
                case 2: rng.Fill(sec); break;
                // case 3: zeros
            }
        }

        return data;
    }

    /// <summary>CD audio track: frames * 2352 bytes of 16-bit stereo audio (sine sweep + noise bursts).</summary>
    public static byte[] BuildCdAudioTrack(int frames)
    {
        var data = new byte[frames * 2352];
        var rng = new DetRng(0xC0FFEE04);
        var samples = data.Length / 4;
        double phaseL = 0, phaseR = 0;

        for (var i = 0; i < samples; i++)
        {
            var freqL = 220 + (i % 44100) / 100.0;
            var freqR = 330 + (i % 22050) / 50.0;
            phaseL += 2 * Math.PI * freqL / 44100;
            phaseR += 2 * Math.PI * freqR / 44100;

            var l = (short)(Math.Sin(phaseL) * 12000);
            var r = (short)(Math.Sin(phaseR) * 12000);

            if ((i / 4410) % 5 == 4) // periodic noise bursts
            {
                l = (short)(rng.NextU64() & 0xFFFF);
                r = (short)(rng.NextU64() & 0xFFFF);
            }

            // CD audio in .bin files is big-endian
            data[i * 4 + 0] = (byte)(l >> 8);
            data[i * 4 + 1] = (byte)l;
            data[i * 4 + 2] = (byte)(r >> 8);
            data[i * 4 + 3] = (byte)r;
        }

        return data;
    }

    private static void FillRepeating8(Span<byte> hunk, ReadOnlySpan<byte> pattern)
    {
        for (var i = 0; i < hunk.Length; i++)
        {
            hunk[i] = pattern[i % 8];
        }
    }

    private static void FillText(Span<byte> hunk, DetRng rng)
    {
        var sb = new StringBuilder(hunk.Length + 16);
        while (sb.Length < hunk.Length)
        {
            sb.Append(Words[rng.Next(Words.Length)]);
            sb.Append(' ');
            if (rng.Next(12) == 0)
                sb.Append('\n');
        }
        Encoding.ASCII.GetBytes(sb.ToString(0, hunk.Length), hunk);
    }

    private static void FillStructured(Span<byte> hunk, DetRng rng, int salt)
    {
        var i = 0;
        while (i < hunk.Length)
        {
            var runLen = 16 + rng.Next(64);
            var val = (byte)((salt * 31 + i) & 0xFF);
            var ramp = rng.Next(2) == 0;
            for (var j = 0; j < runLen && i < hunk.Length; j++, i++)
            {
                hunk[i] = ramp ? (byte)(val + j) : val;
            }
        }
    }
}
