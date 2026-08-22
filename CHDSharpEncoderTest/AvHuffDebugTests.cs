using CHDSharp;
using CHDSharp.Models;
using CHDSharpEncoder;

namespace CHDSharpEncoderTest;

public class AvHuffDebugTests
{
    [Fact]
    public void EncodeSingleFrame_RoundTripsThroughChdLib()
    {
        // encode one synthetic 'chav' frame directly, bypassing the AVI/encode pipeline
        const int width = 64, height = 64, channels = 2, maxSamples = 1920;
        var video = new byte[width * height * 2];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x += 2)
            {
                var off = (y * width + x) * 2;
                video[off] = (byte)(x * 4);
                video[off + 1] = (byte)(y * 3);
                video[off + 2] = (byte)((x + y) * 2);
                video[off + 3] = (byte)(y * 3 + ((x / 2) % 8));
            }

        var planes = new short[channels][];
        for (var ch = 0; ch < channels; ch++)
        {
            planes[ch] = new short[maxSamples];
            for (var i = 0; i < maxSamples; i++)
            {
                planes[ch][i] = (short)(Math.Sin(i * 0.037 + ch) * 9000);
            }
        }

        var rawBytes = AvHuffEncoder.RawDataSize(width, height, channels, maxSamples);
        var raw = new byte[rawBytes];
        AvHuffEncoder.AssembleData(raw, video, width, height, channels, maxSamples, planes);

        var encoder = new AvHuffEncoder();
        var compressed = new byte[rawBytes];
        var compLen = encoder.EncodeData(raw, compressed);
        Assert.True(compLen < rawBytes, $"compression didn't save space: {compLen} >= {rawBytes}");

        // write a 1-hunk CHD via the full pipeline and verify with CHDSharpLib
        var chdPath = Path.Combine(Path.GetTempPath(), "avhuff_single.chd");
        try
        {
            using var source = new MemoryStream(raw);
            ChdEncoder.EncodeRaw(source, chdPath, rawBytes, rawBytes, codecTags: [CodecTags.Avhu]);

            Assert.Equal(ChdError.Chderrnone, ChdFile.Open(chdPath, out var chd));
            Assert.NotNull(chd);
            using (chd)
            {
                var buf = new byte[chd.HunkBytes];
                var readErr = chd.ReadHunk(0, buf);
                Assert.True(readErr == ChdError.Chderrnone,
                    $"ReadHunk(0) returned {readErr}; rawBytes={rawBytes} compLen={compLen}\n" +
                    $"compressed header: {string.Join(" ", compressed.Take(14).Select(b => b.ToString("X2")))}\n" +
                    $"samples={((compressed[2] << 8) | compressed[3])} " +
                    $"width={((compressed[4] << 8) | compressed[5])} " +
                    $"height={((compressed[6] << 8) | compressed[7])}\n" +
                    $"treesize=0x{((compressed[8] << 8) | compressed[9]):X4} " +
                    $"ch0size={((compressed[10] << 8) | compressed[11])} " +
                    $"ch1size={((compressed[12] << 8) | compressed[13])}\n" +
                    $"video starts at {14 + ((compressed[10] << 8) | compressed[11]) + ((compressed[12] << 8) | compressed[13])}: " +
                    $"0x{compressed[14 + ((compressed[10] << 8) | compressed[11]) + ((compressed[12] << 8) | compressed[13])]:X2} " +
                    $"(expect 0x80)");
                Assert.Equal(raw, buf);
            }
        }
        finally
        {
            File.Delete(chdPath);
        }
    }

    [Fact]
    public void EncodeVideoOnlyFrame_RoundTripsThroughChdLib()
    {
        const int width = 32, height = 32;
        var video = new byte[width * height * 2];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x += 2)
            {
                var off = (y * width + x) * 2;
                video[off] = (byte)(x * 4);
                video[off + 1] = (byte)(y * 3);
                video[off + 2] = (byte)((x + y) * 2);
                video[off + 3] = (byte)(y * 3 + ((x / 2) % 8));
            }

        var rawBytes = AvHuffEncoder.RawDataSize(width, height, 0, 0);
        var raw = new byte[rawBytes];
        AvHuffEncoder.AssembleData(raw, video, width, height, 0, 0, Array.Empty<short[]>());

        var encoder = new AvHuffEncoder();
        var compressed = new byte[rawBytes];
        var compLen = encoder.EncodeData(raw, compressed);
        Assert.True(compLen < rawBytes, $"compression didn't save space: {compLen} >= {rawBytes}");

        var chdPath = Path.Combine(Path.GetTempPath(), $"avhuff_vonly_{Guid.NewGuid():N}.chd");
        try
        {
            using var source = new MemoryStream(raw);
            ChdEncoder.EncodeRaw(source, chdPath, rawBytes, rawBytes, codecTags: [CodecTags.Avhu]);

            Assert.Equal(ChdError.Chderrnone, ChdFile.Open(chdPath, out var chd));
            Assert.NotNull(chd);
            using (chd)
            {
                var buf = new byte[chd.HunkBytes];
                var readErr = chd.ReadHunk(0, buf);
                if (readErr != ChdError.Chderrnone)
                {
                    const int videoStart = 10;
                    Assert.Fail($"ReadHunk(0) returned {readErr}; rawBytes={rawBytes} compLen={compLen}\n" +
                                $"compressed header: {string.Join(" ", compressed.Take(14).Select(b => b.ToString("X2")))}\n" +
                                $"video0x80={compressed[videoStart]:X2}");
                }

                Assert.Equal(raw, buf);
            }
        }
        finally
        {
            File.Delete(chdPath);
        }
    }

    [Theory]
    [InlineData("allZeros")]
    [InlineData("alternating")]
    [InlineData("dense")]
    public void ExportTreeRle_RoundTrips(string histogramName)
    {
        var he = new HuffmanEncoder(272, 16);
        switch (histogramName)
        {
            case "allZeros":
                for (var j = 0; j < 100; j++) he.CountSymbol(42);
                break;
            case "alternating":
                for (var i = 0; i < 272; i += 2) he.CountSymbol((uint)i);
                break;
            case "dense":
                var rng = new Random(42);
                for (var i = 0; i < 10000; i++)
                    he.CountSymbol((uint)rng.Next(0, 200));
                for (var i = 0; i < 100; i++)
                    he.CountSymbol((uint)(0x100 + rng.Next(0, 16)));
                break;
        }

        he.BuildTree();

        var bs = new BitStreamOut(4096);
        he.ExportTreeRle(bs);
        var byteLen = bs.Flush();

        var imported = ImportTreeRle(bs.ToArray(), 0, byteLen, 272);
        for (var i = 0; i < 272; i++)
            Assert.True(he.NumBits[i] == imported[i],
                $"numbits[{i}]: enc={he.NumBits[i]} dec={imported[i]}");
    }

    [Fact]
    public void EncoderYTreeRoundTrips()
    {
        // simulate the exact Y-plane histogram from the64x64 gradient video test
        var he = new HuffmanEncoder(272, 16);
        for (var i = 0; i < 272; i += 2) he.CountSymbol((uint)i);
        he.BuildTree();

        var bs = new BitStreamOut(2048);
        he.ExportTreeRle(bs);
        var byteLen = bs.Flush();

        var imported = ImportTreeRle(bs.ToArray(), 0, byteLen, 272);
        for (var i = 0; i < 272; i++)
            Assert.True(he.NumBits[i] == imported[i],
                $"numbits[{i}]: enc={he.NumBits[i]} dec={imported[i]}");
    }

    /// <summary>
    /// MAME's import_tree_rle (huffman.cpp:144). Returns the numbits array for the given
    /// number of codes. Throws if the stream overflows the array.
    /// </summary>
    private static int[] ImportTreeRle(byte[] data, int offset, int length, int numCodes)
    {
        var bitPos = offset * 8;
        var endBit = (offset + length) * 8;
        var result = new int[numCodes];
        var curnode = 0;

        while (curnode < numCodes)
        {
            if (bitPos + 5 > endBit)
                throw new InvalidDataException($"tree import underflow at node {curnode}, bit {bitPos}");

            var nodebits = ReadBits5(data, ref bitPos);
            if (nodebits != 1)
            {
                result[curnode++] = nodebits;
            }
            else
            {
                if (bitPos + 5 > endBit)
                    throw new InvalidDataException($"tree import underflow in escape at node {curnode}");

                nodebits = ReadBits5(data, ref bitPos);
                if (nodebits == 1)
                {
                    result[curnode++] = 1;
                }
                else
                {
                    if (bitPos + 5 > endBit)
                        throw new InvalidDataException($"tree import underflow in repcount at node {curnode}");

                    var repcount = ReadBits5(data, ref bitPos) + 3;
                    if (curnode + repcount > numCodes)
                        throw new InvalidDataException(
                            $"tree import overflow at node {curnode}: {repcount} would exceed {numCodes} (bit {bitPos})");

                    while (repcount-- > 0)
                    {
                        result[curnode++] = nodebits;
                    }
                }
            }
        }

        return result;
    }

    private static int ReadBits5(byte[] data, ref int bitPos)
    {
        var value = 0;
        for (var i = 0; i < 5; i++)
        {
            var byteIndex = bitPos >> 3;
            if (byteIndex < data.Length)
            {
                value = (value << 1) | ((data[byteIndex] >> (7 - (bitPos & 7))) & 1);
            }
            else
            {
                value <<= 1;
            }

            bitPos++;
        }

        return value;
    }
}
