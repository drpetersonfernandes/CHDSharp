using CHDSharp.Utils;

namespace CHDSharp.Tests;

public class UtilityTests
{
    // ── Util.IsAllZeroArray ──

    [Fact]
    public void IsAllZeroArray_null_returns_true()
    {
        Assert.True(Util.IsAllZeroArray(null));
    }

    [Fact]
    public void IsAllZeroArray_empty_returns_true()
    {
        Assert.True(Util.IsAllZeroArray([]));
    }

    [Fact]
    public void IsAllZeroArray_all_zeros_returns_true()
    {
        Assert.True(Util.IsAllZeroArray(new byte[8]));
    }

    [Fact]
    public void IsAllZeroArray_single_nonzero_returns_false()
    {
        var arr = new byte[8];
        arr[4] = 0x01;
        Assert.False(Util.IsAllZeroArray(arr));
    }

    [Fact]
    public void IsAllZeroArray_first_byte_nonzero_returns_false()
    {
        Assert.False(Util.IsAllZeroArray([0xFF, 0, 0, 0]));
    }

    [Fact]
    public void IsAllZeroArray_last_byte_nonzero_returns_false()
    {
        Assert.False(Util.IsAllZeroArray([0, 0, 0, 0x01]));
    }

    // ── Util.ByteArrEquals ──

    [Fact]
    public void ByteArrEquals_identical_returns_true()
    {
        var a = new byte[] { 1, 2, 3, 4 };
        Assert.True(Util.ByteArrEquals(a, [1, 2, 3, 4]));
    }

    [Fact]
    public void ByteArrEquals_different_length_returns_false()
    {
        Assert.False(Util.ByteArrEquals([1, 2, 3], [1, 2, 3, 4]));
    }

    [Fact]
    public void ByteArrEquals_different_content_returns_false()
    {
        Assert.False(Util.ByteArrEquals([1, 2, 3], [1, 2, 4]));
    }

    [Fact]
    public void ByteArrEquals_empty_arrays_returns_true()
    {
        Assert.True(Util.ByteArrEquals([], []));
    }

    // ── Util.ByteArrCompare ──

    [Fact]
    public void ByteArrCompare_equal_returns_zero()
    {
        Assert.Equal(0, Util.ByteArrCompare([1, 2, 3], [1, 2, 3]));
    }

    [Fact]
    public void ByteArrCompare_less_than_returns_negative()
    {
        Assert.True(Util.ByteArrCompare([1, 2, 3], [1, 2, 4]) < 0);
    }

    [Fact]
    public void ByteArrCompare_greater_than_returns_positive()
    {
        Assert.True(Util.ByteArrCompare([1, 2, 4], [1, 2, 3]) > 0);
    }

    [Fact]
    public void ByteArrCompare_shorter_prefix_is_less()
    {
        Assert.True(Util.ByteArrCompare([1, 2], [1, 2, 0]) < 0);
    }

    [Fact]
    public void ByteArrCompare_longer_prefix_is_greater()
    {
        Assert.True(Util.ByteArrCompare([1, 2, 0], [1, 2]) > 0);
    }

    [Fact]
    public void ByteArrCompare_empty_arrays_returns_zero()
    {
        Assert.Equal(0, Util.ByteArrCompare([], []));
    }

    // ── Util.IsAscii ──

    [Fact]
    public void IsAscii_printable_returns_true()
    {
        Assert.True(Util.IsAscii("Hello World"u8.ToArray()));
    }

    [Fact]
    public void IsAscii_with_null_bytes_returns_true()
    {
        Assert.True(Util.IsAscii("A\0B"u8.ToArray()));
    }

    [Fact]
    public void IsAscii_control_char_returns_false()
    {
        Assert.False(Util.IsAscii([0x41, 0x01, 0x42])); // 0x01 is control
    }

    [Fact]
    public void IsAscii_tab_returns_false()
    {
        Assert.False(Util.IsAscii([0x09])); // tab is < 32
    }

    [Fact]
    public void IsAscii_empty_returns_true()
    {
        Assert.True(Util.IsAscii([]));
    }

    // ── ArrayPool ──

    [Fact]
    public void ArrayPool_rent_allocates_when_empty()
    {
        var pool = new ArrayPool(1024);
        var arr = pool.Rent();
        Assert.NotNull(arr);
        Assert.Equal(1024, arr.Length);
    }

    [Fact]
    public void ArrayPool_rent_reuses_returned_array()
    {
        var pool = new ArrayPool(512);
        var arr1 = pool.Rent();
        pool.Return(arr1);
        var arr2 = pool.Rent();
        Assert.Same(arr1, arr2);
    }

    [Fact]
    public void ArrayPool_read_stats_tracks_allocations()
    {
        var pool = new ArrayPool(256);
        pool.Rent();
        pool.Rent();
        var arr = pool.Rent();
        pool.Return(arr);

        pool.ReadStats(out var issued, out var returned);
        Assert.Equal(3, issued);
        Assert.Equal(1, returned);
    }

    [Fact]
    public async Task ArrayPool_concurrent_rent_return_is_safe()
    {
        var pool = new ArrayPool(64);
        var tasks = new Task[100];
        for (int i = 0; i < 100; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                var arr = pool.Rent();
                pool.Return(arr);
            }, TestContext.Current.CancellationToken);
        }

        await Task.WhenAll(tasks);
        pool.ReadStats(out var issued, out var returned);
        Assert.True(issued <= 100);
        Assert.True(returned <= issued);
    }

    // ── BitStream ──

    [Fact]
    public void BitStream_read_single_bits()
    {
        // 0xA5 = 10100101
        var bs = new BitStream([0xA5], 0, 1);
        Assert.Equal(1u, bs.Read(1));
        Assert.Equal(0u, bs.Read(1));
        Assert.Equal(1u, bs.Read(1));
        Assert.Equal(0u, bs.Read(1));
        Assert.Equal(0u, bs.Read(1));
        Assert.Equal(1u, bs.Read(1));
        Assert.Equal(0u, bs.Read(1));
        Assert.Equal(1u, bs.Read(1));
    }

    [Fact]
    public void BitStream_read_byte_at_a_time()
    {
        var bs = new BitStream([0xAB, 0xCD], 0, 2);
        Assert.Equal(0xABu, bs.Read(8));
        Assert.Equal(0xCDu, bs.Read(8));
    }

    [Fact]
    public void BitStream_peek_does_not_advance()
    {
        var bs = new BitStream([0xFF], 0, 1);
        var first = bs.Peek(4);
        var second = bs.Peek(4);
        Assert.Equal(first, second);
    }

    [Fact]
    public void BitStream_remove_advances_position()
    {
        var bs = new BitStream([0xFF, 0x00], 0, 2);
        bs.Peek(4);
        bs.Remove(4);
        var next = bs.Read(4);
        Assert.Equal(0xFu, next);
    }

    [Fact]
    public void BitStream_read_zero_bits_returns_zero()
    {
        var bs = new BitStream([0xFF], 0, 1);
        Assert.Equal(0u, bs.Read(0));
    }

    [Fact]
    public void BitStream_flush_returns_bytes_consumed()
    {
        var bs = new BitStream([0xAA, 0xBB, 0xCC], 0, 3);
        bs.Read(12); // 1.5 bytes
        var consumed = bs.Flush();
        Assert.Equal(2, consumed); // rounded up to 2 bytes
    }

    [Fact]
    public void BitStream_overflow_false_when_within_bounds()
    {
        var bs = new BitStream([0xFF], 0, 1);
        bs.Read(8);
        Assert.False(bs.Overflow());
    }

    // ── Crc16 ──

    [Fact]
    public void Crc16_empty_data_returns_init_value()
    {
        var crc = Crc16.Calc([], 0);
        Assert.Equal(0xFFFF, crc);
    }

    [Fact]
    public void Crc16_single_byte_is_deterministic()
    {
        var a = Crc16.Calc([0x42], 1);
        var b = Crc16.Calc([0x42], 1);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Crc16_different_inputs_produce_different_results()
    {
        var a = Crc16.Calc([0x00], 1);
        var b = Crc16.Calc([0x01], 1);
        Assert.NotEqual(a, b);
    }

    // ── CRC-32 additional vectors ──

    [Fact]
    public void Crc32_empty_data_returns_known_value()
    {
        var digest = Crc.CalculateDigest([], 0, 0);
        Assert.Equal(0x00000000u, digest);
    }

    [Fact]
    public void Crc32_single_zero_byte()
    {
        var digest = Crc.CalculateDigest([0x00], 0, 1);
        Assert.NotEqual(0u, digest);
    }

    [Fact]
    public void Crc32_full_data_matches_slice()
    {
        var data = "Hello, World!"u8.ToArray();
        var full = Crc.CalculateDigest(data, 0, (uint)data.Length);
        var slice = Crc.CalculateDigest(data, 2, 5); // "llo, "
        Assert.NotEqual(full, slice);
    }
}
