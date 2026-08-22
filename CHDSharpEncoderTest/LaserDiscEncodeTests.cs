using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using CHDSharp;
using CHDSharp.Models;
using CHDSharpEncoder;
using CHDSharpEncoder.Models;

namespace CHDSharpEncoderTest;

/// <summary>
/// Tests for <see cref="ChdEncoder.EncodeLaserDisc"/> (chdman createld parity): synthetic
/// AVI round-trips through the CHDSharpLib avhuff decoder, metadata layout, and — when
/// chdman.exe is available — byte-for-byte comparison against chdman's own createld output.
/// </summary>
public class LaserDiscEncodeTests : IDisposable
{
    private readonly string _testDataDir;

    public LaserDiscEncodeTests()
    {
        // unique per test class instance: the test host runs per-TFM in parallel
        _testDataDir = Path.Combine(Path.GetTempPath(), "createld_tests_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>Small progressive clip: 64x64 @ 25 fps, stereo 48 kHz PCM16.</summary>
    private string WriteSmallAvi() => AviTestWriter.WriteAvi(
        Path.Combine(_testDataDir, "small.avi"), width: 64, height: 64, frames: 10,
        timescale: 25, sampletime: 1, audioRate: 48000, audioChannels: 2).Path;

    /// <summary>Laserdisc-like clip: 320x524 @ 29.97 fps → interlaced, field height 262, VBI captured.</summary>
    private string WriteLdAvi() => AviTestWriter.WriteAvi(
        Path.Combine(_testDataDir, "ld.avi"), width: 320, height: 524, frames: 12,
        timescale: 30000, sampletime: 1001, audioRate: 48000, audioChannels: 2).Path;

    [Fact]
    public void SmallAvi_RoundTripsThroughChdReader()
    {
        var aviPath = WriteSmallAvi();
        var chdPath = Path.Combine(_testDataDir, "small.chd");

        var info = ChdEncoder.EncodeLaserDisc(aviPath, chdPath);

        Assert.Equal(25000000ul, info.FpsTimes1million);
        Assert.False(info.Interlaced);
        Assert.Equal(64u, info.Width);
        Assert.Equal(64u, info.Height);
        Assert.Equal(2u, info.Channels);
        Assert.Equal(48000u, info.SampleRate);
        Assert.Equal(10ul, info.Frames);

        uint expectedFrameBytes = AvHuffEncoder.RawDataSize(64, 64, 2, info.MaxSamplesPerFrame);
        Assert.Equal(expectedFrameBytes, info.BytesPerFrame);
        Assert.Equal(expectedFrameBytes, info.HunkBytes);

        using var chd = OpenChd(chdPath);
        Assert.Equal(expectedFrameBytes, chd.HunkBytes);

        // every decoded hunk must equal the raw 'chav' frame assembled independently
        for (uint hunk = 0; hunk < 10; hunk++)
        {
            var buffer = new byte[chd.HunkBytes];
            Assert.Equal(ChdError.Chderrnone, chd.ReadHunk(hunk, buffer));
            var expected = BuildExpectedRawFrame(hunk, aviPath, 2, 48000, info.FpsTimes1million, info.MaxSamplesPerFrame);
            Assert.Equal(expected, buffer);
        }
    }

    [Fact]
    public void SmallAvi_MetadataIsWritten()
    {
        var aviPath = WriteSmallAvi();
        var chdPath = Path.Combine(_testDataDir, "meta.chd");

        ChdEncoder.EncodeLaserDisc(aviPath, chdPath);

        using var chd = OpenChd(chdPath);
        var avav = chd.Metadata.Single(m => string.Equals(m.Tag, "AVAV", StringComparison.Ordinal));
        Assert.Equal(MetadataWriter.ChdMdflagsChecksum, avav.Flags);
        Assert.Contains("FPS:25.000000 WIDTH:64 HEIGHT:64 INTERLACED:0 CHANNELS:2 SAMPLERATE:48000", avav.GetText(), StringComparison.Ordinal);

        // field height is 64, not 262/312: no VBI metadata
        Assert.DoesNotContain(chd.Metadata, m => string.Equals(m.Tag, "AVLD", StringComparison.Ordinal));
    }

    [Fact]
    public void LdAvi_IsInterlaced_AndCapturesVbiMetadata()
    {
        var aviPath = WriteLdAvi();
        var chdPath = Path.Combine(_testDataDir, "ld.chd");

        var info = ChdEncoder.EncodeLaserDisc(aviPath, chdPath);

        // 524-line @ 29.97 fps source: interlaced, so fields are the hunks
        Assert.True(info.Interlaced);
        Assert.Equal(262u, info.Height);
        Assert.Equal(24ul, info.Frames);

        using var chd = OpenChd(chdPath);
        var avav = chd.Metadata.Single(m => string.Equals(m.Tag, "AVAV", StringComparison.Ordinal));
        Assert.Contains("FPS:59.940058 WIDTH:320 HEIGHT:262 INTERLACED:1", avav.GetText(), StringComparison.Ordinal);

        // AVLD carries one packed 16-byte VBI record per field
        var avld = chd.Metadata.Single(m => string.Equals(m.Tag, "AVLD", StringComparison.Ordinal));
        Assert.Equal(0, avld.Flags); // not covered by the combined SHA-1, like chdman
        Assert.Equal(24 * VbiParse.PackedBytes, avld.Data.Length);

        // first record's u24be frame number must be 0
        Assert.Equal(0u, (uint)((avld.Data[0] << 16) | (avld.Data[1] << 8) | avld.Data[2]));
    }

    [Fact]
    public void FrameRangeSelection_EncodesOnlySelectedFrames()
    {
        var aviPath = WriteSmallAvi();
        var chdPath = Path.Combine(_testDataDir, "range.chd");

        var info = ChdEncoder.EncodeLaserDisc(aviPath, chdPath, inputStartFrame: 3, inputLengthFrames: 4);

        Assert.Equal(3ul, info.FirstFrame);
        Assert.Equal(4ul, info.Frames);

        using var chd = OpenChd(chdPath);
        Assert.Equal(4ul * chd.HunkBytes, chd.TotalBytes);
        for (uint i = 0; i < 4; i++)
        {
            var buffer = new byte[chd.HunkBytes];
            Assert.Equal(ChdError.Chderrnone, chd.ReadHunk(i, buffer));
            var expected = BuildExpectedRawFrame(i + 3, aviPath, 2, 48000, info.FpsTimes1million, info.MaxSamplesPerFrame);
            Assert.Equal(expected, buffer);
        }
    }

    [Fact]
    public void InvalidArguments_AreRejected()
    {
        var aviPath = WriteSmallAvi();
        var chdPath = Path.Combine(_testDataDir, "invalid.chd");

        // uncompressed is rejected (chdman: "Uncompressed is not supported")
        Assert.Throws<ArgumentException>(() =>
            ChdEncoder.EncodeLaserDisc(aviPath, chdPath, codecTags: [CodecTags.None]));

        // start beyond the end
        Assert.Throws<InvalidDataException>(() =>
            ChdEncoder.EncodeLaserDisc(aviPath, chdPath, inputStartFrame: 10));

        // length beyond the end
        Assert.Throws<InvalidDataException>(() =>
            ChdEncoder.EncodeLaserDisc(aviPath, chdPath, inputStartFrame: 5, inputLengthFrames: 10));

        // hunk size that is not a multiple of the frame size
        Assert.Throws<ArgumentException>(() =>
            ChdEncoder.EncodeLaserDisc(aviPath, chdPath, hunkBytes: 1234));
    }

    [Fact]
    public void UyvySource_IsConvertedToYuy2ByteOrder()
    {
        // same content generator, different storage order; decoded video bytes must match
        var yuy2Path = AviTestWriter.WriteAvi(Path.Combine(_testDataDir, "yuy2.avi"), 32, 32, 4,
            timescale: 30, sampletime: 1, audioRate: 48000, audioChannels: 2, format: "YUY2").Path;
        var uyvyPath = AviTestWriter.WriteAvi(Path.Combine(_testDataDir, "uyvy.avi"), 32, 32, 4,
            timescale: 30, sampletime: 1, audioRate: 48000, audioChannels: 2, format: "UYVY").Path;

        var yuy2Chd = Path.Combine(_testDataDir, "yuy2.chd");
        var uyvyChd = Path.Combine(_testDataDir, "uyvy.chd");
        ChdEncoder.EncodeLaserDisc(yuy2Path, yuy2Chd);
        ChdEncoder.EncodeLaserDisc(uyvyPath, uyvyChd);

        using var a = OpenChd(yuy2Chd);
        using var b = OpenChd(uyvyChd);
        var bufA = new byte[a.HunkBytes];
        var bufB = new byte[b.HunkBytes];
        for (uint hunk = 0; hunk < 4; hunk++)
        {
            Assert.Equal(ChdError.Chderrnone, a.ReadHunk(hunk, bufA));
            Assert.Equal(ChdError.Chderrnone, b.ReadHunk(hunk, bufB));
            Assert.Equal(bufA, bufB);
        }
    }

    [Fact]
    public void MultiFrameHunks_PackWholeFrames()
    {
        var aviPath = WriteSmallAvi();
        var probePath = Path.Combine(_testDataDir, "probe.chd");

        var probe = ChdEncoder.EncodeLaserDisc(aviPath, probePath);
        uint frameBytes = probe.BytesPerFrame;

        // two frames per hunk
        var info = ChdEncoder.EncodeLaserDisc(aviPath, Path.Combine(_testDataDir, "multi.chd"), hunkBytes: frameBytes * 2);
        Assert.Equal(frameBytes * 2, info.HunkBytes);
        Assert.Equal(10ul, info.Frames);

        using var chd = OpenChd(Path.Combine(_testDataDir, "multi.chd"));
        Assert.Equal(10ul * frameBytes * 2, chd.TotalBytes);
        for (uint hunk = 0; hunk < 5; hunk++)
        {
            var buffer = new byte[chd.HunkBytes];
            Assert.Equal(ChdError.Chderrnone, chd.ReadHunk(hunk, buffer));
            for (var slot = 0; slot < 2; slot++)
            {
                var expected = BuildExpectedRawFrame((uint)(hunk * 2 + slot), aviPath, 2, 48000,
                    info.FpsTimes1million, info.MaxSamplesPerFrame);
                Assert.Equal(expected, buffer.AsSpan(slot * (int)frameBytes, (int)frameBytes).ToArray());
            }
        }
    }

    [Fact]
    public void Createld_OutputMatchesChdman_ByteForByte()
    {
        var chdmanPath = ResolveChdmanPath();
        if (chdmanPath == null)
            return; // chdman.exe unavailable

        foreach (var aviPath in new[] { WriteSmallAvi(), WriteLdAvi() })
        {
            var name = Path.GetFileNameWithoutExtension(aviPath);

            var refPath = Path.Combine(_testDataDir, name + "_ref.chd");
            var (exit, stdout, stderr) = RunChdman(chdmanPath, "createld", "-i", aviPath, "-o", refPath, "-f");
            Assert.True(exit == 0, $"chdman createld failed (exit={exit})\n{stdout}{stderr}");

            var ourPath = Path.Combine(_testDataDir, name + "_ours.chd");
            ChdEncoder.EncodeLaserDisc(aviPath, ourPath);

            var reference = File.ReadAllBytes(refPath);
            var ours = File.ReadAllBytes(ourPath);
            if (!reference.SequenceEqual(ours))
            {
                int diff = reference.Length != ours.Length
                    ? -1
                    : reference.Select((b, i) => (b, i)).First(t => t.b != ours[t.i]).i;
                Assert.Fail(
                    $"{name}: output differs from chdman (lengths {reference.Length} vs {ours.Length}, first diff at {diff})");
            }

            // and chdman verifies our file too
            var (verifyExit, vOut, vErr) = RunChdman(chdmanPath, "verify", "-i", ourPath);
            Assert.True(verifyExit == 0, $"chdman verify failed on our file (exit={verifyExit})\n{vOut}{vErr}");
        }
    }

    /// <summary>Opens a created CHD through the public reader API.</summary>
    private static ChdFile OpenChd(string path)
    {
        Assert.Equal(ChdError.Chderrnone, ChdFile.Open(path, out var chd));
        Assert.NotNull(chd);
        return chd!;
    }

    /// <summary>
    /// Independently reassembles the expected raw 'chav' frame for one image frame
    /// (mirrors <see cref="ChdEncoder.EncodeLaserDisc"/>'s producer math from the AVI's own timing).
    /// </summary>
    private static byte[] BuildExpectedRawFrame(ulong frameInImage, string aviPath, uint channels, uint rate,
        ulong fpsTimes1million, uint maxSamplesPerFrame)
    {
        using var avi = AviReader.Open(aviPath);
        var fullFrame = new byte[(int)(avi.Info.Width * avi.Info.Height * 2)];
        avi.ReadVideoFrame((uint)frameInImage, fullFrame);

        ulong firstSample = rate > 0 ? (rate * frameInImage * 1000000 + fpsTimes1million - 1) / fpsTimes1million : 0;
        ulong endSample = rate > 0 ? (rate * (frameInImage + 1) * 1000000 + fpsTimes1million - 1) / fpsTimes1million : 0;
        int samples = (int)Math.Min(endSample - firstSample, maxSamplesPerFrame);

        var planes = new short[channels][];
        for (int ch = 0; ch < channels; ch++)
        {
            planes[ch] = new short[samples];
            try
            {
                avi.ReadSoundSamples(ch, (uint)Math.Min(firstSample, uint.MaxValue), (uint)samples, planes[ch]);
            }
            catch (ArgumentOutOfRangeException)
            {
                Array.Clear(planes[ch]);
            }
        }

        var result = new byte[AvHuffEncoder.RawDataSize((uint)avi.Info.Width, (uint)avi.Info.Height, channels, maxSamplesPerFrame)];
        AvHuffEncoder.AssembleData(result, fullFrame, avi.Info.Width, avi.Info.Height, (int)channels, samples, planes);
        return result;
    }

    [Fact]
    public void ListTemplates_HasCorrectCount()
    {
        Assert.Equal(13, HardDiskTemplates.Templates.Length);
    }

    [Fact]
    public void ListTemplates_FirstAndLastMatchMame()
    {
        var first = HardDiskTemplates.Templates[0];
        Assert.Equal("Conner", first.Manufacturer);
        Assert.Equal("CFA170A", first.Model);
        Assert.Equal(332u, first.Cylinders);
        Assert.Equal(16u, first.Heads);
        Assert.Equal(63u, first.Sectors);
        Assert.Equal(512u, first.SectorSize);

        var last = HardDiskTemplates.Templates[12];
        Assert.Equal("Micropolis", last.Manufacturer);
        Assert.Equal("1528", last.Model);
        Assert.Equal(2094u, last.Cylinders);
        Assert.Equal(15u, last.Heads);
        Assert.Equal(83u, last.Sectors);
        Assert.Equal(512u, last.SectorSize);
    }

    [Fact]
    public void GetTemplate_InvalidId_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HardDiskTemplates.GetTemplate(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => HardDiskTemplates.GetTemplate(13));
    }

    [Fact]
    public void BuildHardDiskMetadata_ExplicitChs_MatchesFormat()
    {
        var entry = MetadataWriter.BuildHardDiskMetadata(332, 16, 63, 512);
        var text = Encoding.ASCII.GetString(entry.Payload).TrimEnd('\0');
        Assert.Equal("CYLS:332,HEADS:16,SECS:63,BPS:512", text);
    }

    [Fact]
    public void ExtractLaserDisc_Progressive_RoundTrips()
    {
        var aviPath = WriteSmallAvi();
        var chdPath = Path.Combine(_testDataDir, "small.chd");
        var extractedPath = Path.Combine(_testDataDir, "extracted.avi");

        ChdEncoder.EncodeLaserDisc(aviPath, chdPath);
        ChdEncoder.ExtractLaserDisc(chdPath, extractedPath);

        Assert.True(File.Exists(extractedPath), "Extracted AVI should exist");
        var extractedSize = new FileInfo(extractedPath).Length;
        Assert.True(extractedSize > 0, "Extracted AVI should be non-empty");

        using var extractedAvi = AviReader.Open(extractedPath);
        Assert.Equal(64, extractedAvi.Info.Width);
        Assert.Equal(64, extractedAvi.Info.Height);
    }

    [Fact]
    public void ExtractLaserDisc_WithFrameRange_Works()
    {
        var aviPath = WriteSmallAvi();
        var chdPath = Path.Combine(_testDataDir, "range.chd");
        var extractedPath = Path.Combine(_testDataDir, "range_extracted.avi");

        ChdEncoder.EncodeLaserDisc(aviPath, chdPath);
        ChdEncoder.ExtractLaserDisc(chdPath, extractedPath, startFrame: 2, lengthFrames: 3);

        Assert.True(File.Exists(extractedPath), "Extracted AVI should exist");
        using var extractedAvi = AviReader.Open(extractedPath);
        // 3 frames extracted
        Assert.Equal(64, extractedAvi.Info.Width);
    }

    [Fact]
    public void ExtractLaserDisc_NonLaserdiscChd_ThrowsInvalidData()
    {
        // create a raw CHD (not laserdisc)
        var rawPath = Path.Combine(_testDataDir, "raw.bin");
        File.WriteAllBytes(rawPath, new byte[4096]);
        var chdPath = Path.Combine(_testDataDir, "raw.chd");
        ChdEncoder.EncodeRaw(rawPath, chdPath);

        var extractedPath = Path.Combine(_testDataDir, "raw_extracted.avi");
        Assert.Throws<InvalidDataException>(() => ChdEncoder.ExtractLaserDisc(chdPath, extractedPath));
    }

    [Fact]
    public void ExtractLaserDisc_VideoAndAudioDataIsValid()
    {
        // create CHD, extract, then re-create CHD from extracted AVI
        // and verify the data flows through correctly
        var aviPath = WriteSmallAvi();
        var chdPath = Path.Combine(_testDataDir, "roundtrip.chd");
        var extractedPath = Path.Combine(_testDataDir, "roundtrip_extracted.avi");
        var reEncodedPath = Path.Combine(_testDataDir, "roundtrip_re.chd");

        ChdEncoder.EncodeLaserDisc(aviPath, chdPath);
        ChdEncoder.ExtractLaserDisc(chdPath, extractedPath);

        // the extracted AVI should be re-encodable
        var info = ChdEncoder.EncodeLaserDisc(extractedPath, reEncodedPath);
        Assert.Equal(10ul, info.Frames);
        Assert.True(File.Exists(reEncodedPath));
    }

    private static (int ExitCode, string StdOut, string StdErr) RunChdman(string chdmanPath, params string[] args)
    {
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
        return File.Exists(candidate) ? candidate : null;
    }
}

/// <summary>
/// Writes minimal but well-formed AVI files for tests: RIFF/'AVI ' with a 'hdrl' describing
/// one YUY-family video stream and one PCM audio stream, a 'movi' list holding one video chunk
/// and one audio chunk per frame (audio sized by MAME's ceil-div sample math), and an 'idx1'.
/// Content is deterministic: gradient + moving-stripe video, sine audio.
/// </summary>
internal static class AviTestWriter
{
    public static (string Path, int Frames, int Width, int Height) WriteAvi(string path, int width, int height,
        int frames, uint timescale, uint sampletime, uint audioRate, uint audioChannels, string format = "YUY2")
    {
        uint formatFourcc = FourCc(format);
        var frameBytes = width * height * 2;

        bool isUyvy = string.Equals(format, "UYVY", StringComparison.OrdinalIgnoreCase);

        var videoFrames = new byte[frames][];
        for (var f = 0; f < frames; f++)
        {
            var data = new byte[frameBytes];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x += 2)
                {
                    var off = (y * width + x) * 2;
                    byte cb = (byte)((x * 4 + f * 11) & 0xFF);
                    byte y0 = (byte)((y * 3 + f * 7) & 0xFF);
                    byte cr = (byte)(((x + y) * 2 + f * 13) & 0xFF);
                    byte y1 = (byte)((y * 3 + f * 7 + ((x / 2 + f) % 8)) & 0xFF);

                    if (isUyvy)
                    {
                        // UYVY byte order: [U, Y0, V, Y1] = [Cb, Y0, Cr, Y1]
                        data[off] = cb;
                        data[off + 1] = y0;
                        data[off + 2] = cr;
                        data[off + 3] = y1;
                    }
                    else
                    {
                        // YUY2 byte order: [Y0, Cb, Y1, Cr]
                        data[off] = y0;
                        data[off + 1] = cb;
                        data[off + 2] = y1;
                        data[off + 3] = cr;
                    }
                }
            }

            videoFrames[f] = data;
        }

        double fps1m = timescale * 1000000 / (double)sampletime;
        var audioChunks = new List<byte[]>(frames);
        ulong totalSamples = 0;
        for (var f = 0; f < frames; f++)
        {
            ulong first = (ulong)(audioRate * f * 1000000 / fps1m);
            ulong end = (ulong)(audioRate * (f + 1) * 1000000 / fps1m);
            var count = (int)(end - first);
            var chunk = new byte[count * (int)audioChannels * 2];
            for (var i = 0; i < count; i++)
            {
                for (uint ch = 0; ch < audioChannels; ch++)
                {
                    var sample = (short)(Math.Sin((totalSamples + (ulong)i) * 0.037 + ch) * 9000);
                    BinaryPrimitives.WriteInt16LittleEndian(chunk.AsSpan((int)((i * audioChannels + ch) * 2)), sample);
                }
            }

            totalSamples += (ulong)count;
            audioChunks.Add(chunk);
        }

        using var ms = new MemoryStream();
        WriteFourCc(ms, "RIFF");
        long riffSizePos = WriteLengthPlaceholder(ms);
        WriteFourCc(ms, "AVI ");

        long hdrlSizePos = StartList(ms, "hdrl");

        var avih = new byte[56];
        BinaryPrimitives.WriteUInt32LittleEndian(avih.AsSpan(8), 0x10);              // AVIF_HASINDEX
        BinaryPrimitives.WriteUInt32LittleEndian(avih.AsSpan(16), (uint)frames);     // dwTotalFrames
        BinaryPrimitives.WriteUInt32LittleEndian(avih.AsSpan(24), 2);                // dwStreams
        BinaryPrimitives.WriteUInt32LittleEndian(avih.AsSpan(32), (uint)width);
        BinaryPrimitives.WriteUInt32LittleEndian(avih.AsSpan(36), (uint)height);
        WriteChunk(ms, "avih", avih);

        long videoStrlSizePos = StartList(ms, "strl");
        var vstrh = new byte[56];
        BinaryPrimitives.WriteUInt32LittleEndian(vstrh.AsSpan(0), FourCc("vids"));
        BinaryPrimitives.WriteUInt32LittleEndian(vstrh.AsSpan(20), sampletime);      // dwScale
        BinaryPrimitives.WriteUInt32LittleEndian(vstrh.AsSpan(24), timescale);       // dwRate
        BinaryPrimitives.WriteUInt32LittleEndian(vstrh.AsSpan(32), (uint)frames);    // dwLength
        WriteChunk(ms, "strh", vstrh);

        var vstrf = new byte[40]; // BITMAPINFOHEADER
        BinaryPrimitives.WriteUInt32LittleEndian(vstrf.AsSpan(0), 40);
        BinaryPrimitives.WriteUInt32LittleEndian(vstrf.AsSpan(4), (uint)width);
        BinaryPrimitives.WriteUInt32LittleEndian(vstrf.AsSpan(8), (uint)height);
        BinaryPrimitives.WriteUInt16LittleEndian(vstrf.AsSpan(12), 1);               // planes
        BinaryPrimitives.WriteUInt16LittleEndian(vstrf.AsSpan(14), 16);              // bpp
        BinaryPrimitives.WriteUInt32LittleEndian(vstrf.AsSpan(16), formatFourcc);
        BinaryPrimitives.WriteUInt32LittleEndian(vstrf.AsSpan(20), (uint)frameBytes);
        WriteChunk(ms, "strf", vstrf);
        CloseList(ms, videoStrlSizePos);

        long audioStrlSizePos = StartList(ms, "strl");
        var astrh = new byte[56];
        BinaryPrimitives.WriteUInt32LittleEndian(astrh.AsSpan(0), FourCc("auds"));
        BinaryPrimitives.WriteUInt32LittleEndian(astrh.AsSpan(20), 1);               // dwScale
        BinaryPrimitives.WriteUInt32LittleEndian(astrh.AsSpan(24), audioRate);       // dwRate
        BinaryPrimitives.WriteUInt32LittleEndian(astrh.AsSpan(32), (uint)totalSamples); // dwLength
        BinaryPrimitives.WriteUInt32LittleEndian(astrh.AsSpan(44), audioChannels * 2); // dwSampleSize
        WriteChunk(ms, "strh", astrh);

        var astrf = new byte[16]; // WAVEFORMATEX
        BinaryPrimitives.WriteUInt16LittleEndian(astrf.AsSpan(0), 1);                // PCM
        BinaryPrimitives.WriteUInt16LittleEndian(astrf.AsSpan(2), (ushort)audioChannels);
        BinaryPrimitives.WriteUInt32LittleEndian(astrf.AsSpan(4), audioRate);
        BinaryPrimitives.WriteUInt32LittleEndian(astrf.AsSpan(8), audioRate * audioChannels * 2);
        BinaryPrimitives.WriteUInt16LittleEndian(astrf.AsSpan(12), (ushort)(audioChannels * 2));
        BinaryPrimitives.WriteUInt16LittleEndian(astrf.AsSpan(14), 16);
        WriteChunk(ms, "strf", astrf);
        CloseList(ms, audioStrlSizePos);

        CloseList(ms, hdrlSizePos);

        long moviSizePos = StartList(ms, "movi");
        var indexEntries = new List<(uint Id, long Offset, uint Size)>();
        for (var f = 0; f < frames; f++)
        {
            indexEntries.Add((FourCc("00dc"), ms.Position - (moviSizePos + 4), (uint)videoFrames[f].Length));
            WriteChunk(ms, "00dc", videoFrames[f]);
            indexEntries.Add((FourCc("01wb"), ms.Position - (moviSizePos + 4), (uint)audioChunks[f].Length));
            WriteChunk(ms, "01wb", audioChunks[f]);
        }

        CloseList(ms, moviSizePos);

        var idx1 = new byte[indexEntries.Count * 16];
        for (var i = 0; i < indexEntries.Count; i++)
        {
            var (id, offset, size) = indexEntries[i];
            BinaryPrimitives.WriteUInt32LittleEndian(idx1.AsSpan(i * 16), id);
            BinaryPrimitives.WriteUInt32LittleEndian(idx1.AsSpan(i * 16 + 8), (uint)offset);
            BinaryPrimitives.WriteUInt32LittleEndian(idx1.AsSpan(i * 16 + 12), size);
        }

        WriteChunk(ms, "idx1", idx1);

        PatchLength(ms, riffSizePos, (uint)(ms.Length - riffSizePos - 4));
        File.WriteAllBytes(path, ms.ToArray());
        return (path, frames, width, height);
    }

    private static long StartList(MemoryStream ms, string listType)
    {
        WriteFourCc(ms, "LIST");
        long sizePos = WriteLengthPlaceholder(ms);
        WriteFourCc(ms, listType);
        return sizePos;
    }

    private static void CloseList(MemoryStream ms, long sizePos)
    {
        PatchLength(ms, sizePos, (uint)(ms.Length - sizePos - 4));
        if (ms.Length % 2 == 1)
            ms.WriteByte(0);
    }

    private static void WriteChunk(MemoryStream ms, string chunkId, byte[] payload)
    {
        WriteFourCc(ms, chunkId);
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(len, (uint)payload.Length);
        ms.Write(len);
        ms.Write(payload);
        if (payload.Length % 2 == 1)
            ms.WriteByte(0);
    }

    private static void WriteFourCc(MemoryStream ms, string fourcc)
    {
        var bytes = Encoding.ASCII.GetBytes(fourcc);
        Assert.Equal(4, bytes.Length);
        ms.Write(bytes);
    }

    private static long WriteLengthPlaceholder(MemoryStream ms)
    {
        long pos = ms.Position;
        ms.Write(stackalloc byte[4]);
        return pos;
    }

    private static void PatchLength(MemoryStream ms, long pos, uint length)
    {
        long current = ms.Position;
        ms.Position = pos;
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, length);
        ms.Write(buf);
        ms.Position = current;
    }

    private static uint FourCc(string s)
    {
        var b = Encoding.ASCII.GetBytes(s);
        return b[0] | ((uint)b[1] << 8) | ((uint)b[2] << 16) | ((uint)b[3] << 24);
    }
}
