using System.Text.Json;
using System.Text.Json.Serialization;

namespace CHDSharpTestGen;

internal sealed class ManifestEntry
{
    [JsonPropertyName("file")] public string File { get; set; } = "";
    [JsonPropertyName("version")] public uint Version { get; set; }
    [JsonPropertyName("parent")] public string? Parent { get; set; }
    [JsonPropertyName("expect")] public string Expect { get; set; } = "ok";
    [JsonPropertyName("note")] public string Note { get; set; } = "";
}

internal static class Program
{
    private static readonly List<ManifestEntry> Manifest = [];

    private static string _repoRoot = "";
    private static string _work = "";
    private static string _outDir = "";
    private static string _hdcompV1 = "";
    private static string _chdmanV3 = "";
    private static string _chdmanV4 = "";
    private static string _chdmanV5 = "";

    private static int Main(string[] args)
    {
        _repoRoot = args.Length > 0 && Directory.Exists(args[0]) ? args[0] : FindRepoRoot();
        var chdmanDir = Path.Combine(_repoRoot, "CHDSharpTest", "chdman");
        _hdcompV1 = Path.Combine(chdmanDir, "hdcomp_v1.exe");
        _chdmanV3 = Path.Combine(chdmanDir, "chdman_v3.exe");
        _chdmanV4 = Path.Combine(chdmanDir, "chdman_v4.exe");
        _chdmanV5 = Path.Combine(chdmanDir, "chdman_v5.exe");

        foreach (var tool in new[] { _hdcompV1, _chdmanV3, _chdmanV4, _chdmanV5 })
            if (!File.Exists(tool))
            {
                Console.Error.WriteLine($"missing tool: {tool}");
                return 1;
            }

        _outDir = Path.Combine(_repoRoot, "CHDSharpTest", "TestData");
        Directory.CreateDirectory(_outDir);
        _work = Path.Combine(Path.GetTempPath(), "chdsharp_testgen");
        if (Directory.Exists(_work))
            Directory.Delete(_work, true);
        Directory.CreateDirectory(_work);

        if (args.Contains("--avitest"))
            return AviTest();

        if (args.Contains("--hunkdebug"))
            return HunkDebug(args[Array.IndexOf(args, "--hunkdebug") + 1]);

        BuildSources();
        GenerateV1V2();
        GenerateV3();
        GenerateV4();
        GenerateV5();
        WriteManifest();
        Directory.Delete(_work, true);
        Console.WriteLine($"\ndone: {Manifest.Count} corpus files in {_outDir}");
        return 0;
    }

    private static int HunkDebug(string chdPath)
    {
        var raw = SourceData.BuildRawImage();

        using var fs = File.OpenRead(chdPath);
        fs.Position = 16; // skip magic + length + version
        CHDSharp.ChdHeaders.ReadHeaderV5(fs, out var hdr);

        var buffer = new byte[hdr.Blocksize];
        var failures = 0;
        for (uint h = 0; h < hdr.Totalblocks; h++)
        {
            var me = hdr.Map[h];
            if (me.Comptype != CHDSharp.Models.CompressionType.Compressiontype0)
                continue;

            var buffIn = new byte[me.Length];
            fs.Position = (long)me.Offset;
            fs.ReadExactly(buffIn, 0, (int)me.Length);

            var state = new CHDSharp.Models.ChdCodecState();
            Array.Clear(buffer);
            try
            {
                var err = CHDSharp.ChdReaders.Flac(buffIn, (int)me.Length, buffer, (int)hdr.Blocksize, state);
                var match = buffer.AsSpan().SequenceEqual(raw.AsSpan((int)(h * hdr.Blocksize), (int)hdr.Blocksize));
                if (err != CHDSharp.Models.ChdError.Chderrnone || !match)
                {
                    failures++;
                    var firstDiff = -1;
                    for (var i = 0; i < buffer.Length; i++)
                        if (buffer[i] != raw[h * hdr.Blocksize + i]) { firstDiff = i;
                            break; }
                    Console.WriteLine($"hunk {h}: err={err} match={match} firstDiff={firstDiff} complen={me.Length}");
                }
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"hunk {h}: EXCEPTION {ex.GetType().Name}: {ex.Message}");
            }
        }
        Console.WriteLine($"{failures} failing flac hunks of {hdr.Totalblocks}");
        return 0;
    }

    private static int AviTest()
    {
        (int w, int h, int fps)[] variants = [(64, 48, 60), (320, 240, 60), (320, 480, 30), (640, 480, 30), (720, 524, 30)];
        foreach (var (vw, vh, fps) in variants)
        {
            var avi = Path.Combine(_work, $"t{vw}x{vh}_{fps}.avi");
            AviWriter.Write(avi, vw, vh, fps, 16, 44100);
            var outChd = Path.Combine(_work, $"t{vw}x{vh}_{fps}.chd");
            try
            {
                ToolRunner.Run(_chdmanV4, $"-createav \"{avi}\" \"{outChd}\"", _work);
                Console.WriteLine($"  OK {vw}x{vh}@{fps}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  FAIL {vw}x{vh}@{fps}: {ex.Message.Replace("\n", " | ")}");
            }
        }
        return 0;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CSharp_CHDSharp.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("repo root not found; pass it as arg[0]");
    }

    // ----- source images ------------------------------------------------

    private static string Raw => Path.Combine(_work, "raw.img");
    private static string RawChild => Path.Combine(_work, "raw_child.img");
    private static string RawTiny => Path.Combine(_work, "raw_tiny.img");
    private static string RawOdd => Path.Combine(_work, "raw_odd.img");
    private static string Cue => Path.Combine(_work, "disc.cue");
    private static string Toc => Path.Combine(_work, "disc.toc");
    private static string Avi => Path.Combine(_work, "test.avi");

    private static void BuildSources()
    {
        Console.WriteLine("building deterministic source images...");
        var raw = SourceData.BuildRawImage();
        File.WriteAllBytes(Raw, raw);
        File.WriteAllBytes(RawChild, SourceData.BuildChildImage(raw));
        File.WriteAllBytes(RawTiny, raw.AsSpan(16 * SourceData.HunkSize, SourceData.HunkSize).ToArray());
        File.WriteAllBytes(RawOdd, raw.AsSpan(16 * SourceData.HunkSize, 10240).ToArray()); // 2.5 hunks of 4096

        File.WriteAllBytes(Path.Combine(_work, "cd_data.bin"), SourceData.BuildCdDataTrack(600));
        File.WriteAllBytes(Path.Combine(_work, "cd_audio.bin"), SourceData.BuildCdAudioTrack(400));

        File.WriteAllText(Cue,
            "FILE \"cd_data.bin\" BINARY\n" +
            "  TRACK 01 MODE1/2048\n" +
            "    INDEX 01 00:00:00\n" +
            "FILE \"cd_audio.bin\" BINARY\n" +
            "  TRACK 02 AUDIO\n" +
            "    INDEX 01 00:00:00\n");

        File.WriteAllText(Toc,
            "CD_ROM\n\n" +
            "TRACK MODE1\n" +
            "DATAFILE \"cd_data.bin\"\n\n" +
            "TRACK AUDIO\n" +
            "AUDIOFILE \"cd_audio.bin\" 0\n");

        AviWriter.Write(Avi, 64, 48, 60, 32, 44100);
    }

    // ----- corpus generation ---------------------------------------------

    private static string Out(string name)
    {
        return Path.Combine(_outDir, name);
    }

    private static void Add(string name, uint version, string note, string? parent = null, string expect = "ok")
    {
        Manifest.Add(new ManifestEntry { File = name, Version = version, Parent = parent, Expect = expect, Note = note });
    }

    private static void GenerateV1V2()
    {
        Console.WriteLine("\n=== V1 (hdcomp, MAME 0.77) ===");
        ToolRunner.Run(_hdcompV1, $"-create raw.img \"{Out("v1_zlib.chd")}\" 0 16 4 16", _work);
        Add("v1_zlib.chd", 1, "raw hd, zlib, legacy map (none/self/type0)");

        Console.WriteLine("\n=== V2 (synthesized from V1) ===");
        V2Patcher.Convert(Out("v1_zlib.chd"), Out("v2_zlib.chd"));
        Add("v2_zlib.chd", 2, "V1 converted to V2 (seclen header field)");
    }

    private static void GenerateV3()
    {
        Console.WriteLine("\n=== V3 (chdman, MAME 0.130) ===");
        ToolRunner.Run(_chdmanV3, $"-createraw raw.img \"{Out("v3_zlib.chd")}\" 0 4096", _work);
        Add("v3_zlib.chd", 3, "raw, zlib+ codec, map: type0/none/self/mini");

        ToolRunner.Run(_chdmanV3, $"-createcd disc.toc \"{Out("v3_cd.chd")}\"", _work);
        Add("v3_cd.chd", 3, "cd (toc), zlib+ codec");

        ToolRunner.Run(_chdmanV3, $"-createav test.avi \"{Out("v3_av.chd")}\"", _work);
        Add("v3_av.chd", 3, "a/v laserdisc, avhuff legacy codec (comp type 3)");

        ToolRunner.Run(_chdmanV3, $"-createraw raw_child.img \"{Path.Combine(_work, "v3_child_full.chd")}\" 0 4096", _work);
        ToolRunner.Run(_chdmanV3, $"-diff \"{Out("v3_zlib.chd")}\" \"{Path.Combine(_work, "v3_child_full.chd")}\" \"{Out("v3_child.chd")}\"", _work);
        Add("v3_child.chd", 3, "diff chd, map: parent refs", "v3_zlib.chd");
    }

    private static void GenerateV4()
    {
        Console.WriteLine("\n=== V4 (chdman, MAME 0.145) ===");
        ToolRunner.Run(_chdmanV4, $"-createraw raw.img \"{Out("v4_zlib.chd")}\" 0 4096", _work);
        Add("v4_zlib.chd", 4, "raw, zlib+ codec, map: type0/none/self/mini");

        ToolRunner.Run(_chdmanV4, $"-createuncomphd raw.img \"{Out("v4_uncomp.chd")}\" 0 16 4 16 512 4096", _work);
        Add("v4_uncomp.chd", 4, "uncompressed hd, map: uncompressed entries");

        ToolRunner.Run(_chdmanV4, $"-createcd disc.cue \"{Out("v4_cd.chd")}\"", _work);
        Add("v4_cd.chd", 4, "cd (cue), zlib+flac era codec");

        // chdman 0.145's A/V codec crashes (access violation) on both -createav and -update,
        // so the V4 A/V file is synthesized from the V3 one (identical map/metadata layout).
        V3ToV4Patcher.Convert(Out("v3_av.chd"), Out("v4_av.chd"));
        Add("v4_av.chd", 4, "a/v laserdisc, avhuff legacy codec (comp type 3)");

        ToolRunner.Run(_chdmanV4, $"-createraw raw_child.img \"{Path.Combine(_work, "v4_child_full.chd")}\" 0 4096", _work);
        ToolRunner.Run(_chdmanV4, $"-diff \"{Out("v4_zlib.chd")}\" \"{Path.Combine(_work, "v4_child_full.chd")}\" \"{Out("v4_child.chd")}\"", _work);
        Add("v4_child.chd", 4, "diff chd, map: parent refs", "v4_zlib.chd");
    }

    private static void GenerateV5()
    {
        Console.WriteLine("\n=== V5 (chdman, MAME 0.288) ===");

        foreach (var codec in new[] { "zlib", "lzma", "huff", "flac", "zstd" })
        {
            var name = $"v5_{codec}.chd";
            ToolRunner.Run(_chdmanV5, $"createraw -f -i raw.img -o \"{Out(name)}\" -hs 4096 -us 512 -c {codec}", _work);
            Add(name, 5, $"raw, single codec: {codec}");
        }

        ToolRunner.Run(_chdmanV5, $"createraw -f -i raw.img -o \"{Out("v5_multi.chd")}\" -hs 4096 -us 512 -c lzma,zlib,huff,flac", _work);
        Add("v5_multi.chd", 5, "raw, 4 codec slots: lzma,zlib,huff,flac");

        ToolRunner.Run(_chdmanV5, $"createraw -f -i raw.img -o \"{Out("v5_none.chd")}\" -hs 4096 -us 512 -c none", _work);
        Add("v5_none.chd", 5, "raw, uncompressed map (compression none)");

        ToolRunner.Run(_chdmanV5, $"createraw -f -i raw_tiny.img -o \"{Out("v5_tiny.chd")}\" -hs 4096 -us 512 -c lzma,zlib,huff,flac", _work);
        // chdman writes a degenerate single-symbol huffman map for 1-hunk files that neither
        // MAME nor chdman itself can re-read; the library must reject it gracefully.
        Add("v5_tiny.chd", 5, "single hunk file, unreadable map (matches chdman behaviour)", expect: "invalid");

        ToolRunner.Run(_chdmanV5, $"createraw -f -i raw_odd.img -o \"{Out("v5_odd.chd")}\" -hs 4096 -us 512 -c lzma,zlib,huff,flac", _work);
        Add("v5_odd.chd", 5, "logical size not a hunk multiple (partial last hunk)");

        // parent / child chains
        ToolRunner.Run(_chdmanV5, $"createraw -f -i raw.img -o \"{Out("v5_parent.chd")}\" -hs 4096 -us 512 -c lzma,zlib,huff,flac", _work);
        Add("v5_parent.chd", 5, "parent for v5 children");

        ToolRunner.Run(_chdmanV5, $"createraw -f -i raw_child.img -o \"{Out("v5_child.chd")}\" -hs 4096 -us 512 -c lzma,zlib,huff,flac -op \"{Out("v5_parent.chd")}\"", _work);
        Add("v5_child.chd", 5, "compressed map child, parent refs (parent0/1/self)", "v5_parent.chd");

        ToolRunner.Run(_chdmanV5, $"createraw -f -i raw_child.img -o \"{Out("v5_child_none.chd")}\" -hs 4096 -us 512 -c none -op \"{Out("v5_parent.chd")}\"", _work);
        Add("v5_child_none.chd", 5, "uncompressed map child, offset-0 parent refs", "v5_parent.chd");

        TryUnalignedChild();

        // CD codecs
        ToolRunner.Run(_chdmanV5, $"createcd -f -i disc.cue -o \"{Out("v5_cd_default.chd")}\"", _work);
        Add("v5_cd_default.chd", 5, "cd, default codecs (cdlz,cdzl,cdfl)");

        foreach (var codec in new[] { "cdzl", "cdlz", "cdfl", "cdzs" })
        {
            var name = $"v5_cd_{codec}.chd";
            ToolRunner.Run(_chdmanV5, $"createcd -f -i disc.cue -o \"{Out(name)}\" -c {codec}", _work);
            Add(name, 5, $"cd, single codec: {codec}");
        }

        // laserdisc (avhuff)
        ToolRunner.Run(_chdmanV5, $"createld -f -i test.avi -o \"{Out("v5_ld_avhu.chd")}\"", _work);
        Add("v5_ld_avhu.chd", 5, "laserdisc, avhuff codec");
    }

    private static void TryUnalignedChild()
    {
        // A child with a different hunk size than its parent produces unit-unaligned
        // parent references (two-hunk stitching on read). Not all chdman versions accept
        // mismatched hunk sizes, so this entry is best-effort.
        try
        {
            ToolRunner.Run(_chdmanV5,
                $"createraw -f -i raw_child.img -o \"{Out("v5_child_hs2560.chd")}\" -hs 2560 -us 512 -c lzma,zlib,huff,flac -op \"{Out("v5_parent.chd")}\"",
                _work);
            Add("v5_child_hs2560.chd", 5, "child hunk size 2560 vs parent 4096 (unaligned parent refs)", "v5_parent.chd");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  (skipped unaligned child: {ex.Message.Split('\n')[0]})");
        }
    }

    private static readonly JsonSerializerOptions ManifestJsonOptions = new() { WriteIndented = true };

    private static void WriteManifest()
    {
        var json = JsonSerializer.Serialize(Manifest, ManifestJsonOptions);
        File.WriteAllText(Path.Combine(_outDir, "manifest.json"), json);
    }
}
