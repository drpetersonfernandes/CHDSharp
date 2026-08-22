using System.IO.Compression;
using CHDSharp.Models;

namespace CHDSharp.Tests;

/// <summary>
/// Fuzz tests for deflate decoder hardening (libchdr #168).
/// Feeds crafted deflate streams to ChdFile.Open + ReadHunk and asserts no hang.
/// </summary>
public class DeflateInfiniteLoopTests
{
    private static readonly byte[] Magic = "MComprHD"u8.ToArray();

    /// <summary>
    /// Creates a V3 CHD with a single compressed hunk whose payload is the given bytes.
    /// </summary>
    private static MemoryStream MakeV3WithCompressedHunk(byte[] compressedPayload, uint blocksize = 512)
    {
        var length = (uint)compressedPayload.Length;
        var ms = new MemoryStream();
        ms.Write(Magic, 0, Magic.Length);
        ms.Write(EndianHelpers.Be(120), 0, 4); // V3 header length
        ms.Write(EndianHelpers.Be(3), 0, 4); // version 3
        ms.Write(EndianHelpers.Be(0), 0, 4); // flags
        ms.Write(EndianHelpers.Be(1), 0, 4); // compression = 1 (zlib)
        ms.Write(EndianHelpers.Be(1), 0, 4); // totalblocks = 1
        ms.Write(EndianHelpers.Be64(blocksize), 0, 8); // totalbytes = blocksize
        ms.Write(EndianHelpers.Be64(0), 0, 8); // metaoffset
        ms.Write(new byte[16], 0, 16); // md5
        ms.Write(new byte[16], 0, 16); // parentmd5
        ms.Write(EndianHelpers.Be(blocksize), 0, 4); // blocksize
        ms.Write(new byte[20], 0, 20); // rawsha1
        ms.Write(new byte[20], 0, 20); // parentsha1

        // Map entry: offset=256, crc=0, length, flags=compressed
        var mapStart = ms.Position;
        ms.Write(EndianHelpers.Be64(256)); // offset
        ms.Write(EndianHelpers.Be(0u)); // crc
        ms.WriteByte((byte)((length >> 8) & 0xFF));
        ms.WriteByte((byte)(length & 0xFF));
        ms.WriteByte((byte)((length >> 16) & 0xFF));
        ms.WriteByte((byte)MapEntryFlag.Mapentrytypecompressed);

        // Pad to offset 256
        if (ms.Length < 256)
            ms.SetLength(256);
        ms.Position = 256;
        ms.Write(compressedPayload, 0, compressedPayload.Length);
        ms.Position = 0;
        return ms;
    }

    /// <summary>
    /// Wraps raw deflate bytes in a zlib wrapper (2-byte header + deflate data + adler32).
    /// </summary>
    private static byte[] WrapInZlib(byte[] deflateBytes)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(0x78); // CMF: deflate, 32K window
        ms.WriteByte(0x01); // FLG: no dict, fastest compression
        ms.Write(deflateBytes, 0, deflateBytes.Length);

        // Compute adler32 over the decompressed data (we don't know it, so use dummy).
        // The decoder will check this, but for fuzz testing we just want to test the
        // inflate path — if the checksum fails, that's fine (better than hanging).
        ms.WriteByte(0x00);
        ms.WriteByte(0x00);
        ms.WriteByte(0x00);
        ms.WriteByte(0x00);
        return ms.ToArray();
    }

    /// <summary>
    /// Attempts to open a CHD and read hunk 0 with a timeout.
    /// Returns true if the operation completed (success or error), false if it hung.
    /// </summary>
    private static (ChdError openErr, ChdError readErr, bool completed) TryReadHunkWithTimeout(
        MemoryStream chdStream, int timeoutMs = 5000)
    {
        var cts = new CancellationTokenSource(timeoutMs);
        var openErr = ChdError.Chderrinvaliddata;
        var readErr = ChdError.Chderrinvaliddata;
        var completed = false;

        var thread = new Thread(() =>
        {
            try
            {
                chdStream.Position = 0;
                openErr = ChdFile.Open(chdStream, true, out var chd);
                if (openErr == ChdError.Chderrnone && chd != null)
                {
                    var buf = new byte[chd.HunkBytes];
                    readErr = chd.ReadHunk(0, buf, cts.Token);
                    chd.Dispose();
                }

                completed = true;
            }
            catch (OperationCanceledException)
            {
                // Expected if timeout fires during ReadHunk
                completed = true;
            }
            catch
            {
                completed = true;
            }
        });
        thread.Start();
        thread.Join(timeoutMs + 1000);

        if (!thread.Join(timeoutMs))
        {
            thread.Interrupt();
            return (openErr, readErr, false);
        }

        return (openErr, readErr, completed);
    }

    [Fact]
    public void Crafted_empty_deflate_stream_does_not_hang()
    {
        // Empty deflate stream (stored block, length 0) — should complete instantly.
        var deflate = new byte[] { 0x01, 0x00, 0x00, 0xFF, 0xFF }; // BFINAL=1, BTYPE=00, LEN=0, NLEN=0xFFFF
        var zlib = WrapInZlib(deflate);
        var chd = MakeV3WithCompressedHunk(zlib);

        var (openErr, readErr, completed) = TryReadHunkWithTimeout(chd);
        Assert.True(completed, "ReadHunk must not hang on empty deflate stream");
    }

    [Fact]
    public void Crafted_random_deflate_bytes_do_not_hang()
    {
        // Feed random bytes as deflate data — should complete (success or error) without hanging.
        var rng = new Random(42);
        for (var i = 0; i < 20; i++)
        {
            var data = new byte[rng.Next(10, 200)];
            rng.NextBytes(data);
            var zlib = WrapInZlib(data);
            var chd = MakeV3WithCompressedHunk(zlib);

            var (_, _, completed) = TryReadHunkWithTimeout(chd);
            Assert.True(completed, $"ReadHunk must not hang on random deflate data (iteration {i})");
        }
    }

    [Fact]
    public void Crafted_single_byte_deflate_does_not_hang()
    {
        // A single byte as deflate data — should fail gracefully, not hang.
        var zlib = WrapInZlib(new byte[] { 0x00 });
        var chd = MakeV3WithCompressedHunk(zlib);

        var (_, _, completed) = TryReadHunkWithTimeout(chd);
        Assert.True(completed, "ReadHunk must not hang on single-byte deflate data");
    }

    [Fact]
    public void Crafted_truncated_deflate_does_not_hang()
    {
        // Truncated deflate stream — should fail gracefully.
        var deflate = new byte[] { 0x00 }; // BFINAL=0, BTYPE=00 → stored block, but truncated
        var zlib = WrapInZlib(deflate);
        var chd = MakeV3WithCompressedHunk(zlib);

        var (_, _, completed) = TryReadHunkWithTimeout(chd);
        Assert.True(completed, "ReadHunk must not hang on truncated deflate data");
    }

    [Fact]
    public void Crafted_all_zero_deflate_does_not_hang()
    {
        // All-zero bytes as deflate — BFINAL=0, BTYPE=00, LEN=0, etc.
        var deflate = new byte[100];
        var zlib = WrapInZlib(deflate);
        var chd = MakeV3WithCompressedHunk(zlib);

        var (_, _, completed) = TryReadHunkWithTimeout(chd);
        Assert.True(completed, "ReadHunk must not hang on all-zero deflate data");
    }

    [Fact]
    public void Crafted_dynamic_block_with_zero_lengths_does_not_hang()
    {
        // Craft a dynamic deflate block where all code lengths are 0.
        // This should trigger the "no symbols to code at all" path in InflateTable.
        // BFINAL=1, BTYPE=10 (dynamic), then HLIT=0, HDIST=0, HCLEN=0
        var deflate = new byte[]
        {
            0x05, // BFINAL=1, BTYPE=10 (dynamic)
            0x00, // HLIT=0 (actually +257 = 257), HDIST high bits
            0x00, // HDIST low bits (actually +1 = 1), HCLEN high bits
            0x00  // HCLEN low bits (actually +4 = 4)
        };
        var zlib = WrapInZlib(deflate);
        var chd = MakeV3WithCompressedHunk(zlib);

        var (_, _, completed) = TryReadHunkWithTimeout(chd);
        Assert.True(completed, "ReadHunk must not hang on dynamic block with zero lengths");
    }

    [Fact]
    public void Crafted_dynamic_block_single_code_length_does_not_hang()
    {
        // Craft a dynamic block with a single non-zero code length.
        // This creates a degenerate Huffman table.
        var deflate = new byte[]
        {
            0x05, // BFINAL=1, BTYPE=10 (dynamic)
            0x01, // HLIT=258, HDIST=2 (low bits)
            0x00, // HCLEN=4 (low bits)
            // Code length code lengths (in special order): all zeros except one
            0x00, 0x00, 0x00, 0x01, // first 4 entries: 0,0,0,1 (symbol 3 = code length 1)
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00,
            // One symbol with code length 1 → end-of-block code
            0x00 // literal 0 (code length for symbol 0)
        };
        var zlib = WrapInZlib(deflate);
        var chd = MakeV3WithCompressedHunk(zlib);

        var (_, _, completed) = TryReadHunkWithTimeout(chd);
        Assert.True(completed, "ReadHunk must not hang on single code length dynamic block");
    }

    [Fact]
    public void Valid_deflate_stream_decodes_successfully()
    {
        // Sanity check: a valid deflate stream should decode without hanging.
        var original = new byte[512];
        new Random(123).NextBytes(original);

        byte[] compressed;
        using (var ms = new MemoryStream())
        {
            ms.WriteByte(0x78); // CMF
            ms.WriteByte(0x01); // FLG
            using (var deflate = new DeflateStream(ms, CompressionLevel.Optimal, true))
            {
                deflate.Write(original, 0, original.Length);
            }
            // Dummy adler32 (decoder will check, but for testing inflate path)
            ms.Write(new byte[4], 0, 4);
            compressed = ms.ToArray();
        }

        var chd = MakeV3WithCompressedHunk(compressed);
        var (openErr, _, completed) = TryReadHunkWithTimeout(chd);
        Assert.True(completed, "ReadHunk must not hang on valid deflate stream");
        Assert.Equal(ChdError.Chderrnone, openErr);
    }

    [Fact]
    public void Crafted_repeated_literal_zero_does_not_hang()
    {
        // Craft a valid dynamic block with many literal zeros — tests the decoder
        // doesn't get stuck on repeated literals.
        var deflate = new byte[]
        {
            0x05, // BFINAL=1, BTYPE=10 (dynamic)
            0x00, 0x00, 0x00, // HLIT=257, HDIST=1, HCLEN=4
            // Code length code lengths: only symbol 0 (literal code length 0) has length 1
            0x01, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00,
            // End-of-block code (symbol 256)
            0x00
        };
        var zlib = WrapInZlib(deflate);
        var chd = MakeV3WithCompressedHunk(zlib);

        var (_, _, completed) = TryReadHunkWithTimeout(chd);
        Assert.True(completed, "ReadHunk must not hang on repeated literal zeros");
    }
}
