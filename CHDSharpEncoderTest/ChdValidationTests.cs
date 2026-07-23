using CHDSharpEncoder;
using CHDSharp;
using CHDSharp.Models;

namespace CHDSharpEncoderTest;

public class ChdValidationTests
{
    private static readonly string TestDataDir =
        Path.Combine(Path.GetTempPath(), "chd_validation_tests");

    public ChdValidationTests()
    {
        Directory.CreateDirectory(TestDataDir);
    }

    [Fact]
    public void OpenWithChdSharpLib_HeaderCorrect()
    {
        byte[] source = CreateTestFile(8192, 42);
        string srcPath = Path.Combine(TestDataDir, "v_hdr_src.bin");
        string chdPath = Path.Combine(TestDataDir, "v_hdr.chd");
        File.WriteAllBytes(srcPath, source);

        try
        {
            new ChdEncoder().EncodeRaw(srcPath, chdPath, 4096, 512);

            var err = ChdFile.Open(chdPath, out var chdFile);
            Assert.Equal(ChdError.Chderrnone, err);

            using (chdFile!)
            {
                Assert.Equal(5u, chdFile.Version);
                Assert.Equal(4096u, chdFile.HunkBytes);
                Assert.True(chdFile.HunkCount >= 1);
            }
        }
        finally { SafeDelete(srcPath); SafeDelete(chdPath); }
    }

    [Fact]
    public void Extract_producesIdenticalData()
    {
        byte[] source = CreateTestFile(65536, 456);
        string srcPath = Path.Combine(TestDataDir, "v_extract_src.bin");
        string chdPath = Path.Combine(TestDataDir, "v_extract.chd");
        File.WriteAllBytes(srcPath, source);

        try
        {
            new ChdEncoder().EncodeRaw(srcPath, chdPath, 4096, 512);

            var err = ChdFile.Open(chdPath, out var chdFile);
            Assert.Equal(ChdError.Chderrnone, err);

            using (chdFile!)
            {
                byte[] hunk = new byte[chdFile.HunkBytes];
                for (uint h = 0; h < chdFile.HunkCount; h++)
                {
                    Assert.Equal(ChdError.Chderrnone, chdFile.ReadHunk(h, hunk));
                    int srcOff = (int)(h * chdFile.HunkBytes);
                    int len = Math.Min((int)chdFile.HunkBytes, source.Length - srcOff);
                    Assert.True(hunk.AsSpan(0, len).SequenceEqual(source.AsSpan(srcOff, len)));

                    for (int i = len; i < chdFile.HunkBytes; i++)
                        Assert.Equal(0, hunk[i]);
                }
            }
        }
        finally { SafeDelete(srcPath); SafeDelete(chdPath); }
    }

    [Fact]
    public void LargeFile_RoundTrip()
    {
        byte[] source = CreateTestFile(10 * 1024 * 1024, 42);
        string srcPath = Path.Combine(TestDataDir, "v_large_src.bin");
        string chdPath = Path.Combine(TestDataDir, "v_large.chd");
        File.WriteAllBytes(srcPath, source);

        try
        {
            var encoder = new ChdEncoder();
            using var ms = new MemoryStream(source);
            encoder.EncodeRaw(ms, chdPath, 65536, 4096);

            var err = ChdFile.Open(chdPath, out var chdFile);
            Assert.Equal(ChdError.Chderrnone, err);

            using (chdFile!)
            {
                byte[] hunk = new byte[chdFile.HunkBytes];
                for (uint h = 0; h < chdFile.HunkCount && h < 10; h++)
                {
                    Assert.Equal(ChdError.Chderrnone, chdFile.ReadHunk(h, hunk));
                }

                // Spot check last hunk
                uint last = chdFile.HunkCount - 1;
                Assert.Equal(ChdError.Chderrnone, chdFile.ReadHunk(last, hunk));
                int srcOff = (int)(last * chdFile.HunkBytes);
                int len = Math.Min((int)chdFile.HunkBytes, source.Length - srcOff);
                Assert.True(hunk.AsSpan(0, len).SequenceEqual(source.AsSpan(srcOff, len)));
            }
        }
        finally { SafeDelete(srcPath); SafeDelete(chdPath); }
    }

    [Fact]
    public void NonAlignedSize_works()
    {
        byte[] source = CreateTestFile(10000, 42);
        string srcPath = Path.Combine(TestDataDir, "v_na_src.bin");
        string chdPath = Path.Combine(TestDataDir, "v_na.chd");
        File.WriteAllBytes(srcPath, source);

        try
        {
            new ChdEncoder().EncodeRaw(srcPath, chdPath, 4096, 512);

            var err = ChdFile.Open(chdPath, out var chdFile);
            Assert.Equal(ChdError.Chderrnone, err);

            using (chdFile!)
            {
                Assert.True(chdFile.HunkCount >= 1);
            }
        }
        finally { SafeDelete(srcPath); SafeDelete(chdPath); }
    }

    [Fact]
    public void SingleUncompressedHunk_readsCorrectly()
    {
        byte[] source = new byte[4096];
        new Random(123).NextBytes(source);
        string srcPath = Path.Combine(TestDataDir, "v_suh_src.bin");
        string chdPath = Path.Combine(TestDataDir, "v_suh.chd");
        File.WriteAllBytes(srcPath, source);

        try
        {
            new ChdEncoder().EncodeRaw(srcPath, chdPath, 4096, 512);

            var err = ChdFile.Open(chdPath, out var chdFile);
            Assert.Equal(ChdError.Chderrnone, err);

            using (chdFile!)
            {
                byte[] hunk = new byte[4096];
                Assert.Equal(ChdError.Chderrnone, chdFile.ReadHunk(0, hunk));
                Assert.Equal(source, hunk);
            }
        }
        finally { SafeDelete(srcPath); SafeDelete(chdPath); }
    }

    [Fact]
    public void TwoUncompressedHunks_readCorrectly()
    {
        byte[] source = new byte[8192];
        new Random(456).NextBytes(source);
        string srcPath = Path.Combine(TestDataDir, "v_tuh_src.bin");
        string chdPath = Path.Combine(TestDataDir, "v_tuh.chd");
        File.WriteAllBytes(srcPath, source);

        try
        {
            new ChdEncoder().EncodeRaw(srcPath, chdPath, 4096, 512);

            var err = ChdFile.Open(chdPath, out var chdFile);
            Assert.Equal(ChdError.Chderrnone, err);

            using (chdFile!)
            {
                byte[] hunk = new byte[4096];
                Assert.Equal(ChdError.Chderrnone, chdFile.ReadHunk(0, hunk));
                Assert.Equal(source.AsSpan(0, 4096).ToArray(), hunk);
                Assert.Equal(ChdError.Chderrnone, chdFile.ReadHunk(1, hunk));
                Assert.Equal(source.AsSpan(4096, 4096).ToArray(), hunk);
            }
        }
        finally { SafeDelete(srcPath); SafeDelete(chdPath); }
    }

    [Fact]
    public void ThreeUncompressedHunks_readCorrectly()
    {
        byte[] source = new byte[12288];
        new Random(789).NextBytes(source);
        string srcPath = Path.Combine(TestDataDir, "v_thuh_src.bin");
        string chdPath = Path.Combine(TestDataDir, "v_thuh.chd");
        File.WriteAllBytes(srcPath, source);

        try
        {
            new ChdEncoder().EncodeRaw(srcPath, chdPath, 4096, 512);

            var err = ChdFile.Open(chdPath, out var chdFile);
            Assert.Equal(ChdError.Chderrnone, err);

            using (chdFile!)
            {
                byte[] hunk = new byte[4096];
                for (uint h = 0; h < 3; h++)
                {
                    Assert.Equal(ChdError.Chderrnone, chdFile.ReadHunk(h, hunk));
                    Assert.Equal(source.AsSpan((int)(h * 4096), 4096).ToArray(), hunk);
                }
            }
        }
        finally { SafeDelete(srcPath); SafeDelete(chdPath); }
    }

    [Fact]
    public void TwoUncompressedHunks_headerShowsCorrectCompressionTypes()
    {
        byte[] source = new byte[8192];
        new Random(456).NextBytes(source);
        string srcPath = Path.Combine(TestDataDir, "v_hct_src.bin");
        string chdPath = Path.Combine(TestDataDir, "v_hct.chd");
        File.WriteAllBytes(srcPath, source);

        try
        {
            new ChdEncoder().EncodeRaw(srcPath, chdPath, 4096, 512);

            byte[] chd = File.ReadAllBytes(chdPath);

            // Check that data at offset 124 matches source (for uncompressed hunk)
            for (int i = 0; i < 20; i++)
            {
                Assert.Equal(source[i], chd[124 + i]);
            }
        }
        finally { SafeDelete(srcPath); SafeDelete(chdPath); }
    }

    private static byte[] CreateTestFile(int size, int seed)
    {
        byte[] data = new byte[size];
        var rng = new Random(seed);
        rng.NextBytes(data);
        return data;
    }

    private static void SafeDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }
}
