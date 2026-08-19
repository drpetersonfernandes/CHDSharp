namespace CHDSharp.Utils;

/// <summary>
/// CD-ROM MSF (minute:second:frame) to LBA (logical block address) conversion helpers.
/// MSF values use the binary-coded-decimal (BCD) representation found in CD sector headers
/// and drive addressing (e.g. 2 minutes is <c>0x02</c>, 10 minutes is <c>0x10</c>).
/// </summary>
/// <remarks>
/// Per the Red Book, LBA 0 corresponds to MSF 00:02:00 (the 2-second lead-in offset,
/// <see cref="PregapFrames"/>); a negative LBA denotes a position inside the lead-in.
/// <see cref="LbaToMsfAlt"/> / <see cref="MsfToLbaAlt"/> omit this offset for systems
/// (Sega CD, PC Engine) that address frames relative to the start of the disc data.
/// </remarks>
public static class CdRomAddress
{
    /// <summary>CD frames per second.</summary>
    public const int FramesPerSecond = 75;

    /// <summary>Seconds per minute in an MSF address.</summary>
    public const int SecondsPerMinute = 60;

    /// <summary>The 2-second (150-frame) lead-in offset between MSF and LBA addressing.</summary>
    public const int PregapFrames = 2 * FramesPerSecond;

    /// <summary>
    /// Converts a BCD MSF address (e.g. 00:02:00 = <c>(0x00, 0x02, 0x00)</c>) to an LBA,
    /// subtracting the <see cref="PregapFrames"/> lead-in offset: <c>(m*60 + s)*75 + f - 150</c>.
    /// </summary>
    /// <param name="m">Minutes, BCD-encoded (each nibble 0-9).</param>
    /// <param name="s">Seconds, BCD-encoded.</param>
    /// <param name="f">Frames, BCD-encoded.</param>
    /// <returns>The LBA, or a negative value for addresses before 00:02:00.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Any byte is not valid BCD (a nibble above 9).</exception>
    public static int MsfToLba(byte m, byte s, byte f)
    {
        return MsfToLbaCore(m, s, f) - PregapFrames;
    }

    /// <summary>
    /// Converts a BCD MSF address to a frame count without the <see cref="PregapFrames"/> lead-in
    /// offset (for systems that address frames from the start of the disc data, e.g. Sega CD / PC Engine).
    /// </summary>
    /// <param name="m">Minutes, BCD-encoded (each nibble 0-9).</param>
    /// <param name="s">Seconds, BCD-encoded.</param>
    /// <param name="f">Frames, BCD-encoded.</param>
    /// <returns>The absolute frame count.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Any byte is not valid BCD (a nibble above 9).</exception>
    public static int MsfToLbaAlt(byte m, byte s, byte f)
    {
        return MsfToLbaCore(m, s, f);
    }

    /// <summary>
    /// Converts an LBA to a BCD MSF address, adding the <see cref="PregapFrames"/> lead-in offset:
    /// LBA 0 becomes 00:02:00, LBA -150 becomes 00:00:00.
    /// </summary>
    /// <param name="lba">The logical block address (may be negative for lead-in positions).</param>
    /// <returns>The BCD-encoded (minutes, seconds, frames) triple.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="lba"/> maps to a negative MSF
    /// position, or to more than 99 minutes (not representable in BCD).</exception>
    public static (byte m, byte s, byte f) LbaToMsf(int lba)
    {
        return LbaToMsfCore(lba + PregapFrames);
    }

    /// <summary>
    /// Converts a frame count to a BCD MSF address without the <see cref="PregapFrames"/> lead-in
    /// offset (for systems that address frames from the start of the disc data, e.g. Sega CD / PC Engine).
    /// </summary>
    /// <param name="lba">The frame count (must be non-negative).</param>
    /// <returns>The BCD-encoded (minutes, seconds, frames) triple.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="lba"/> is negative, or maps to
    /// more than 99 minutes (not representable in BCD).</exception>
    public static (byte m, byte s, byte f) LbaToMsfAlt(int lba)
    {
        return LbaToMsfCore(lba);
    }

    private static int MsfToLbaCore(byte m, byte s, byte f)
    {
        var minutes = UnpackBcd(m);
        var seconds = UnpackBcd(s);
        var frames = UnpackBcd(f);
        return (minutes * SecondsPerMinute + seconds) * FramesPerSecond + frames;
    }

    private static (byte m, byte s, byte f) LbaToMsfCore(int lba)
    {
        if (lba < 0)
            throw new ArgumentOutOfRangeException(nameof(lba), lba, "The MSF position cannot be negative.");

        var total = (long)lba;
        var minutes = (int)(total / (SecondsPerMinute * FramesPerSecond));
        if (minutes > 99)
            throw new ArgumentOutOfRangeException(nameof(lba), lba, "The MSF minute field cannot exceed 99 (BCD limit).");

        total -= (long)minutes * SecondsPerMinute * FramesPerSecond;
        var seconds = (int)(total / FramesPerSecond);
        var frames = (int)(total % FramesPerSecond);
        return (PackBcd(minutes), PackBcd(seconds), PackBcd(frames));
    }

    private static int UnpackBcd(byte value)
    {
        var hi = value >> 4;
        var lo = value & 0x0F;
        if (hi > 9 || lo > 9)
            throw new ArgumentOutOfRangeException(nameof(value), value, "MSF bytes must be valid BCD (each nibble 0-9).");

        return hi * 10 + lo;
    }

    private static byte PackBcd(int value)
    {
        return (byte)(((value / 10) << 4) | (value % 10));
    }
}