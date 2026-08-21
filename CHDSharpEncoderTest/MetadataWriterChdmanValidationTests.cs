using System.Diagnostics;
using CHDSharp;
using CHDSharp.Models;
using CHDSharpEncoder;

namespace CHDSharpEncoderTest;

/// <summary>
/// Validates MetadataWriter against chdman.exe: a CUE+BIN is converted to a CD CHD with
/// chdman, then the raw metadata chain (16-byte headers, payloads, and 'next' links) written
/// by chdman is compared byte-for-byte against the chain produced by our writer from the
/// same CUE parsed by our CueParser.
/// </summary>
public class MetadataWriterChdmanValidationTests : IDisposable
{
    private static readonly string? ChdmanPath = ResolveChdmanPath();

    private readonly string _testDataDir;

    public MetadataWriterChdmanValidationTests()
    {
        // unique per test class instance: the test host runs per-TFM in parallel
        _testDataDir = Path.Combine(Path.GetTempPath(), "metadata_writer_chdman_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDataDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_testDataDir, recursive: true);
        }
        catch
        {
            // ignored
        }
    }

    [Fact]
    public void MetadataChain_MatchesChdman_ByteForByte()
    {
        if (ChdmanPath == null) return;

        // Saturn-style: MODE1/2352 data track + AUDIO tracks with pregaps, single BIN
        const string cue = """
                           FILE "game.bin" BINARY
                             TRACK 01 MODE1/2352
                               INDEX 01 00:00:00
                             TRACK 02 AUDIO
                               INDEX 00 03:00:00
                               INDEX 01 03:02:00
                             TRACK 03 AUDIO
                               INDEX 00 06:00:00
                               INDEX 01 06:02:00
                             TRACK 04 AUDIO
                               INDEX 00 09:00:00
                               INDEX 01 09:02:00
                             TRACK 05 AUDIO
                               INDEX 01 12:02:00
                           """;
        var cuePath = Path.Combine(_testDataDir, "saturn.cue");
        var binPath = Path.Combine(_testDataDir, "game.bin");
        var chdPath = Path.Combine(_testDataDir, "saturn.chd");
        File.WriteAllText(cuePath, cue);
        using (var fs = File.Create(binPath))
        {
            fs.SetLength(2352L * 54550);
        }

        var (exitCode, stdout, stderr) = RunChdman("createcd", "-i", cuePath, "-o", chdPath, "-c", "zlib", "-f");
        Assert.True(exitCode == 0, $"chdman createcd failed (exit={exitCode})\nstdout: {stdout}\nstderr: {stderr}");

        // our metadata chain, from the same CUE through CueParser + MetadataWriter
        var toc = CueParser.Parse(cuePath);
        using var ourStream = new MemoryStream();
        ourStream.SetLength(4096); // simulate prior file content; also keeps first offset > 0
        ourStream.Position = 4096;
        var ourFirstOffset = MetadataWriter.WriteCdMetadata(ourStream, toc);
        var ourBytes = ourStream.ToArray();

        // chdman's metadata chain, walked from the header's metaoffset
        var chdBytes = File.ReadAllBytes(chdPath);
        var chdMetaOffset = ReadU64Be(chdBytes, 48);

        var ours = WalkChain(ourBytes, (ulong)ourFirstOffset);
        var theirs = WalkChain(chdBytes, chdMetaOffset);

        Assert.Equal(theirs.Count, ours.Count);
        for (var i = 0; i < theirs.Count; i++)
        {
            var expected = theirs[i];
            var actual = ours[i];
            Assert.Equal(expected.Tag, actual.Tag);
            Assert.Equal(expected.Flags, actual.Flags);
            Assert.Equal(expected.Length, actual.Length);
            Assert.Equal(expected.Payload, actual.Payload);
        }
    }

    [Fact]
    public void Metadata_ReadBack_MatchesChdsharpReader()
    {
        if (ChdmanPath == null) return;

        const string cue = """
                           FILE "game.bin" BINARY
                             TRACK 01 MODE1/2352
                               INDEX 01 00:00:00
                             TRACK 02 AUDIO
                               INDEX 00 01:00:00
                               INDEX 01 01:02:00
                             TRACK 03 AUDIO
                               INDEX 01 02:00:00
                           """;
        var cuePath = Path.Combine(_testDataDir, "saturn.cue");
        var binPath = Path.Combine(_testDataDir, "game.bin");
        var chdPath = Path.Combine(_testDataDir, "saturn.chd");
        File.WriteAllText(cuePath, cue);
        using (var fs = File.Create(binPath))
        {
            fs.SetLength(2352L * (4500 + 4650 + 8));
        }

        var (exitCode, stdout, stderr) = RunChdman("createcd", "-i", cuePath, "-o", chdPath, "-c", "zlib", "-f");
        Assert.True(exitCode == 0, $"chdman createcd failed (exit={exitCode})\nstdout: {stdout}\nstderr: {stderr}");

        // build the expected entries from our parser + writer
        var toc = CueParser.Parse(cuePath);
        using var ms = new MemoryStream();
        MetadataWriter.WriteCdMetadata(ms, toc);
        ms.Position = 0;

        var expectedEntries = new List<ChdMetadataEntry>();
        while (ms.Position < ms.Length)
        {
            var header = new byte[16];
            ms.ReadExactly(header, 0, header.Length);
            var tag = ReadU32Be(header, 0);
            var length = ReadU24Be(header, 5);
            var payload = new byte[length];
            ms.ReadExactly(payload, 0, payload.Length);
            expectedEntries.Add(new ChdMetadataEntry(
                $"{(char)((tag >> 24) & 0xFF)}{(char)((tag >> 16) & 0xFF)}{(char)((tag >> 8) & 0xFF)}{(char)(tag & 0xFF)}",
                payload)
            {
                Flags = header[4]
            });
        }

        // the same entries as parsed by the CHDSharpLib reader from chdman's file
        var openErr = ChdFile.Open(chdPath, out var chd);
        Assert.Equal(ChdError.Chderrnone, openErr);
        using (chd)
        {
            var actualEntries = chd!.Metadata
                .Where(m => string.Equals(m.Tag, "CHT2", StringComparison.Ordinal))
                .ToList();

            Assert.Equal(expectedEntries.Count, actualEntries.Count);
            for (var i = 0; i < expectedEntries.Count; i++)
            {
                Assert.Equal(expectedEntries[i].Flags, actualEntries[i].Flags);
                Assert.Equal(expectedEntries[i].Data, actualEntries[i].Data);
            }
        }
    }

    private static List<MetaEntry> WalkChain(byte[] fileBytes, ulong firstOffset)
    {
        var entries = new List<MetaEntry>();
        var offset = firstOffset;
        var visited = new HashSet<ulong>();
        while (offset != 0)
        {
            Assert.True(visited.Add(offset), "metadata chain contains a cycle");
            Assert.True((long)offset + 16 <= fileBytes.Length, "metadata header out of range");

            var tag = ReadU32Be(fileBytes, (int)offset);
            var flags = fileBytes[(int)offset + 4];
            var length = ReadU24Be(fileBytes, (int)offset + 5);
            var next = ReadU64Be(fileBytes, (int)offset + 8);

            Assert.True(length <= 1024 * 1024, "metadata length out of range");
            var payload = new byte[length];
            Array.Copy(fileBytes, (int)offset + 16, payload, 0, (int)length);

            entries.Add(new MetaEntry
            {
                Tag = tag,
                Flags = flags,
                Length = length,
                Next = next,
                Payload = payload
            });

            // a non-zero next must point exactly past this entry (chained, not scattered)
            if (next != 0)
                Assert.Equal(offset + 16 + length, next);
            offset = next;
        }

        return entries;
    }

    private sealed class MetaEntry
    {
        public uint Tag;
        public byte Flags;
        public uint Length;
        public ulong Next;
        public byte[] Payload = Array.Empty<byte>();
    }

    private static uint ReadU32Be(byte[] data, int offset)
    {
        return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) |
               ((uint)data[offset + 2] << 8) | data[offset + 3];
    }

    private static uint ReadU24Be(byte[] data, int offset)
    {
        return ((uint)data[offset] << 16) | ((uint)data[offset + 1] << 8) | data[offset + 2];
    }

    private static ulong ReadU64Be(byte[] data, int offset)
    {
        return ((ulong)ReadU32Be(data, offset) << 32) | ReadU32Be(data, offset + 4);
    }

    private static (int ExitCode, string StdOut, string StdErr) RunChdman(params string[] args)
    {
        var chdmanPath = ChdmanPath ?? throw new InvalidOperationException("chdman.exe not available");

        var psi = new ProcessStartInfo
        {
            FileName = chdmanPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        var tOut = p.StandardOutput.ReadToEndAsync();
        var tErr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();

        return (p.ExitCode, tOut.Result, tErr.Result);
    }

    private static string? ResolveChdmanPath()
    {
        var exeName = OperatingSystem.IsWindows() ? "chdman.exe" : "chdman";

        var baseDir = AppContext.BaseDirectory;
        var candidate = Path.Combine(baseDir, exeName);
        if (File.Exists(candidate))
            return candidate;

        candidate = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "CHDSharpTester", exeName));
        if (File.Exists(candidate))
            return candidate;

        return null;
    }
}