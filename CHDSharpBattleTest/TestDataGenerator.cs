namespace CHDSharpBattleTest;

/// <summary>
/// Deterministic corpus generator for the battle test: every input is produced from a
/// seeded RNG, so runs are reproducible. Produces raw binary inputs with different
/// compression profiles (zeros, random, pattern, text, PCM audio, mixed, repeated hunks)
/// plus CD images (CUE+BIN and ISO) with mixed track types.
/// </summary>
public static class TestDataGenerator
{
    // ----- raw inputs -----

    public static byte[] Zeros(int size)
    {
        return new byte[size];
    }

    public static byte[] Random(int size, int seed)
    {
        var b = new byte[size];
        new Random(seed).NextBytes(b);
        return b;
    }

    public static byte[] Pattern(int size, int seed)
    {
        var rng = new Random(seed);
        var block = new byte[1024];
        rng.NextBytes(block);

        var b = new byte[size];
        for (int i = 0; i < b.Length; i++)
        {
            b[i] = block[i % block.Length];
        }

        return b;
    }

    /// <summary>N distinct hunks, each repeated R times — SELF-dedup and RLE-map stress.</summary>
    public static byte[] RepeatedHunks(int distinctHunks, int repeats, int hunkBytes, int seed)
    {
        var rng = new Random(seed);
        var distinct = new byte[distinctHunks][];
        for (int i = 0; i < distinctHunks; i++)
        {
            distinct[i] = new byte[hunkBytes];
            rng.NextBytes(distinct[i]);
        }

        var b = new byte[distinctHunks * repeats * hunkBytes];
        for (int i = 0; i < distinctHunks * repeats; i++)
            Array.Copy(distinct[i % distinctHunks], 0, b, i * hunkBytes, hunkBytes);

        return b;
    }

    /// <summary>Pseudo-English text with a skewed letter distribution (Huffman-friendly).</summary>
    public static byte[] Text(int size, int seed)
    {
        var rng = new Random(seed);
        const string common = "etaoinshrdlucmfwypvbgkjqxz";
        const string all = "etaoinshrdlucmfwypvbgkjqxz ETAOINSHRDLUCMFWYPVBGKJQXZ0123456789.,!?;:'\"()-";

        var b = new byte[size];
        for (int i = 0; i < b.Length; i++)
        {
            var r = rng.NextDouble();
            b[i] = r switch
            {
                < 0.45 => (byte)common[rng.Next(common.Length)],
                < 0.90 => (byte)all[rng.Next(all.Length)],
                < 0.94 => (byte)' ',
                _ => (byte)'\n'
            };
        }

        return b;
    }

    /// <summary>16-bit little-endian PCM: sine blocks with slow frequency drift + light noise (FLAC-friendly).</summary>
    public static byte[] Pcm16(int size, int seed)
    {
        var rng = new Random(seed);
        var samples = size / 2;
        var b = new byte[samples * 2];

        double freq = 220 + rng.NextDouble() * 200;
        double phase = 0;
        for (int i = 0; i < samples; i++)
        {
            if (i % 4096 == 0)
            {
                freq = 180 + rng.NextDouble() * 1200;
            }

            phase += 2 * Math.PI * freq / 44100.0;
            var sample = (short)(Math.Sin(phase) * 11000 + (rng.NextDouble() - 0.5) * 400);
            b[i * 2] = (byte)sample;
            b[i * 2 + 1] = (byte)(sample >> 8);
        }

        return b;
    }

    /// <summary>Mixed-content image: zeros, random, text, pattern — exercises every codec path in one file.</summary>
    public static byte[] Mixed(int size, int seed)
    {
        var rng = new Random(seed);
        var b = new byte[size];
        var pos = 0;

        Fill(size / 4, (buf, o, n) => Array.Clear(buf, o, n));
        Fill(size / 4, (buf, o, n) =>
        {
            var r = new byte[n];
            rng.NextBytes(r);
            Array.Copy(r, 0, buf, o, n);
        });
        Fill(size / 4, (buf, o, n) =>
        {
            var t = Text(n, seed + 7);
            Array.Copy(t, 0, buf, o, n);
        });
        Fill(size / 8, (buf, o, n) =>
        {
            var p = Pattern(n, seed + 13);
            Array.Copy(p, 0, buf, o, n);
        });
        Fill(size / 8, (buf, o, n) =>
        {
            var r = new byte[n];
            rng.NextBytes(r);
            Array.Copy(r, 0, buf, o, n);
        });
        Fill(size, (buf, o, n) => Array.Clear(buf, o, n));

        return b;

        void Fill(int count, Action<byte[], int, int> writer)
        {
            var n = Math.Min(count, size - pos);
            writer(b, pos, n);
            pos += n;
        }
    }

    // ----- CD image generation -----

    /// <summary>Creates a 3-track CD image (MODE1 + AUDIO with pregap + MODE2) as CUE+BIN.</summary>
    public static void CreateMixedCd(string dir, int seed, out string cuePath, out string binPath)
    {
        cuePath = Path.Combine(dir, "cd-mixed.cue");
        binPath = Path.Combine(dir, "cd-mixed.bin");
        var rng = new Random(seed);

        const int track1Frames = 500; // MODE1/2352
        const int track2Frames = 300; // AUDIO (with 150-frame pregap)
        const int track3Frames = 300; // MODE2/2352

        using var fs = File.Create(binPath);
        int lba = 0;

        // track 1: Mode1 sectors with garbage EDC/ECC (chdman recomputes for CD codecs)
        for (int f = 0; f < track1Frames; f++, lba++)
            WriteMode1Frame(fs, lba, MakeSectorData(2048, rng, seed + f));

        // track 2: audio — 150 pregap frames then 300 data frames
        for (int f = 0; f < 150; f++, lba++)
            WriteAudioFrame(fs, lba, rng, silent: true);
        for (int f = 0; f < track2Frames; f++, lba++)
            WriteAudioFrame(fs, lba, rng, silent: false);

        // track 3: Mode2 formless (2336-byte user data)
        for (int f = 0; f < track3Frames; f++, lba++)
            WriteMode2Frame(fs, lba, MakeSectorData(2336, rng, seed + 10_000 + f));

        fs.Flush();

        // track 2 INDEX 01 = track1Frames + 150 pregap frames; track 3 INDEX 01 = + track2Frames + 150 pregap
        var track2Index = FramesToMsf(track1Frames + 150);
        var track3Index = FramesToMsf(track1Frames + 150 + track2Frames + 150);

        File.WriteAllText(cuePath, $"""
            FILE "cd-mixed.bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                PREGAP 00:02:00
                INDEX 01 {track2Index}
              TRACK 03 MODE2/2352
                INDEX 01 {track3Index}
            """);
    }

    /// <summary>Creates a single audio-track CD (byte-swap exercise: LE BIN → BE CHD).</summary>
    public static void CreateAudioOnlyCd(string dir, int seed, out string cuePath, out string binPath)
    {
        cuePath = Path.Combine(dir, "cd-audio.cue");
        binPath = Path.Combine(dir, "cd-audio.bin");
        var rng = new Random(seed);

        const int frames = 800;
        using var fs = File.Create(binPath);
        for (int f = 0; f < frames; f++)
            WriteAudioFrame(fs, f, rng, silent: false);

        fs.Flush();
        File.WriteAllText(cuePath, """
                                   FILE "cd-audio.bin" BINARY
                                     TRACK 01 AUDIO
                                       INDEX 01 00:00:00
                                   """);
    }

    /// <summary>Creates a 1 MB ISO-9660 image (single MODE1/2048 track source).</summary>
    public static void CreateIso(string dir, int seed, out string isoPath)
    {
        isoPath = Path.Combine(dir, "disc.iso");
        var rng = new Random(seed);
        const int sectors = 512;

        using var fs = File.Create(isoPath);
        for (int s = 0; s < sectors; s++)
        {
            var sector = new byte[2048];
            if (s == 16)
            {
                // primary volume descriptor: "CD001" at byte 1
                sector[1] = (byte)'C';
                sector[2] = (byte)'D';
                sector[3] = (byte)'0';
                sector[4] = (byte)'0';
                sector[5] = (byte)'1';
            }
            else
            {
                rng.NextBytes(sector);
                if (s % 8 == 0)
                    Array.Clear(sector, 0, sector.Length); // some compressible sectors
            }

            fs.Write(sector);
        }

        fs.Flush();
    }

    // ----- frame writers -----

    private static byte[] MakeSectorData(int length, Random rng, int salt)
    {
        var data = new byte[length];
        rng.NextBytes(data);
        // a recognizable marker so track contents can be eyeballed in extracts
        for (int i = 0; i < data.Length; i += 137)
        {
            data[i] = (byte)(i ^ salt);
        }

        return data;
    }

    private static void WriteMode1Frame(Stream fs, int lba, byte[] data)
    {
        var frame = new byte[2352];
        WriteSyncHeader(frame, lba, mode: 0x01);
        Array.Copy(data, 0, frame, 16, 2048);
        // EDC + 8 zero + ECC: intentionally garbage; CD codecs recompute it
        fs.Write(frame);
    }

    private static void WriteMode2Frame(Stream fs, int lba, byte[] data)
    {
        var frame = new byte[2352];
        WriteSyncHeader(frame, lba, mode: 0x02);
        Array.Copy(data, 0, frame, 16, 2336);
        fs.Write(frame);
    }

    private static void WriteAudioFrame(Stream fs, int lba, Random rng, bool silent)
    {
        var frame = new byte[2352];
        var phase = lba * 37.0 % (2 * Math.PI);
        for (int s = 0; s < 588; s++)
        {
            short sample = silent
                ? (short)0
                : (short)(Math.Sin(phase + s * 0.035) * 9000 + (rng.NextDouble() - 0.5) * 300);
            frame[s * 2] = (byte)sample; // little-endian in the BIN
            frame[s * 2 + 1] = (byte)(sample >> 8);
        }

        fs.Write(frame);
    }

    private static void WriteSyncHeader(byte[] frame, int lba, byte mode)
    {
        frame[0] = 0x00;
        for (int i = 1; i < 11; i++)
        {
            frame[i] = 0xFF;
        }

        frame[11] = 0x00;

        var msf = lba + 150; // lead-in
        frame[12] = (byte)(msf / (60 * 75));
        frame[13] = (byte)(msf / 75 % 60);
        frame[14] = (byte)(msf % 75);
        frame[15] = mode;
    }

    private static string FramesToMsf(int frames)
    {
        var m = frames / (60 * 75);
        var s = frames / 75 % 60;
        var f = frames % 75;
        return $"{m:00}:{s:00}:{f:00}";
    }
}