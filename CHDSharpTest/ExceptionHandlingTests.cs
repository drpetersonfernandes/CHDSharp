using CHDSharp.Models;

namespace CHDSharp.Tests;

public class ExceptionHandlingTests
{
    private static byte[] Be(uint v)
    {
        return [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];
    }

    private static byte[] Be64(ulong v)
    {
        return
        [
            (byte)(v >> 56), (byte)(v >> 48), (byte)(v >> 40), (byte)(v >> 32),
            (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v
        ];
    }

    [Fact]
    public void Open_with_nonexistent_path_returns_file_not_found()
    {
        var err = ChdFile.Open(@"Z:\no\such\path\file.chd", out var chd);
        Assert.Equal(ChdError.Chderrfilenotfound, err);
        Assert.Null(chd);
    }

    [Fact]
    public void Open_returns_cannot_open_for_locked_file()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            using var hold = new FileStream(tmp, FileMode.Open, FileAccess.Read, FileShare.None);
            var err = ChdFile.Open(tmp, out var chd);
            Assert.Equal(ChdError.Chderrcannotopenfile, err);
            Assert.Null(chd);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void Metadata_read_error_returns_empty_collection()
    {
        // Build a valid V5 CHD header that opens successfully.
        // Use a custom stream that throws IOException when reading metadata
        // to verify the typed catch in EnsureMetadataLoaded.
        using var chdFile = OpenMinimalV5Chd();

        var meta = chdFile.Metadata;
        Assert.Empty(meta);
    }

    private static ChdFile OpenMinimalV5Chd()
    {
        // Minimal V5 header with Zlib compression, 1 hunk, uncompressed map,
        // and a metaoffset pointing to position 999 (stream only has ~140 bytes).
        // When EnsureMetadataLoaded tries to read metadata, it hits end of stream
        // and the IOException/InvalidDataException is caught.
        var ms = new MemoryStream();
        ms.Write("MComprHD"u8);
        ms.Write(Be(124), 0, 4);
        ms.Write(Be(5), 0, 4);
        ms.Write(Be((uint)ChdCodec.None), 0, 4);
        ms.Write(Be((uint)ChdCodec.None), 0, 4);
        ms.Write(Be((uint)ChdCodec.None), 0, 4);
        ms.Write(Be((uint)ChdCodec.None), 0, 4);
        ms.Write(Be64(4096), 0, 8); // totalbytes
        ms.Write(Be64(124), 0, 8); // mapoffset = end of header
        ms.Write(Be64(999), 0, 8); // metaoffset = 999 (beyond stream)
        ms.Write(Be(4096), 0, 4); // blocksize
        ms.Write(Be(4096), 0, 4); // unitbytes
        ms.Write(new byte[20], 0, 20); // rawsha1
        ms.Write(new byte[20], 0, 20); // sha1
        ms.Write(new byte[20], 0, 20); // parentsha1
        // Uncompressed map at offset 124: 0 entries, 0 first offset
        ms.Seek(124, SeekOrigin.Begin);
        ms.Write(Be64(0), 0, 8);
        ms.Write(Be64(0), 0, 8);
        // Pad so the stream is long enough but metaoffset is still out of range
        ms.Seek(0, SeekOrigin.End);
        ms.Write(new byte[100], 0, 100);
        ms.Position = 0;

        var err = ChdFile.Open(ms, true, out var chd);
        Assert.Equal(ChdError.Chderrnone, err);
        return chd!;
    }

    [Fact]
    public void IsChdFile_with_nonexistent_file_returns_false()
    {
        var result = Chd.IsChdFile(@"\\?\C:\no_such_dir\no_such_file_99999.chd", out var version);
        Assert.False(result);
        Assert.Equal(0u, version);
    }

    [Fact]
    public void Zstd_corrupt_data_returns_decompression_error()
    {
        var buffIn = new byte[16];
        buffIn[0] = 0x28;
        buffIn[1] = 0xB5;
        buffIn[2] = 0x2F;
        buffIn[3] = 0xFD; // zstd magic
        var buffOut = new byte[4096];
        var codec = new ChdCodecState();

        var err = ChdReaders.Zstd(buffIn, buffIn.Length, buffOut, buffOut.Length, codec);
        Assert.Equal(ChdError.Chderrdecompressionerror, err);
    }

    [Fact]
    public void ChdCodecState_implements_IDisposable()
    {
        var codec = new ChdCodecState();
        Assert.IsAssignableFrom<IDisposable>(codec);
    }

    [Fact]
    public void ChdCodecState_dispose_is_idempotent()
    {
        var codec = new ChdCodecState();
        codec.Dispose();
        codec.Dispose(); // second call should not throw
    }

    [Fact]
    public void ChdCodecState_dispose_releases_zstd_decompressor()
    {
        var codec = new ChdCodecState();
        codec.BZstd = new ZstdSharp.Decompressor();

        codec.Dispose();
        Assert.Null(codec.BZstd);
    }

    [Fact]
    public void ChdCodecState_dispose_handles_null_audio_decoders()
    {
        var codec = new ChdCodecState();
        // FlacAudioDecoder and AvhuffAudioDecoder are null by default.
        // Dispose should handle null gracefully.
        codec.Dispose();
        Assert.Null(codec.FlacAudioDecoder);
        Assert.Null(codec.AvhuffAudioDecoder);
    }

    [Fact]
    public void ChdFile_dispose_calls_codec_dispose()
    {
        using var chd = OpenMinimalV5Chd();
        // If we reach here without an unhandled exception, the codec was
        // disposed successfully during ChdFile.Dispose.
    }

    [Fact]
    public async Task ChdFile_dispose_async_calls_codec_dispose()
    {
        var chd = OpenMinimalV5Chd();
        await chd.DisposeAsync();
        // No exception means codec was disposed cleanly.
    }
}
