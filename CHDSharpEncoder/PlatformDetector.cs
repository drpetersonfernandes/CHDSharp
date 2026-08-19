using CHDSharp;
using CHDSharp.Models;
using CHDSharpEncoder.Models;

namespace CHDSharpEncoder;

/// <summary>
/// Detects the game platform of a raw disc image (CHDlite <c>detect_input</c> parity) and picks
/// smart per-platform codec presets (CHDlite <c>smart_compression_for</c>,
/// <c>chd_archiver.cpp:124-165</c>). Works on raw .bin/.iso/.img files and on CUE/GDI/NRG
/// descriptors before archiving.
/// </summary>
public static class PlatformDetector
{
    /// <summary>
    /// Detects the platform of a disc image file. For CUE/GDI/NRG descriptors the track layout
    /// is parsed and sectors are read from the underlying track files; for raw .bin/.iso/.img
    /// files the sector size is inferred from the content.
    /// </summary>
    /// <param name="inputPath">Path to the disc image (cue/gdi/nrg/iso/bin/img).</param>
    /// <returns>The detection result; on parse failure the platform is <see cref="DiscPlatform.Unknown"/>.</returns>
    public static DiscPlatformInfo Detect(string inputPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(inputPath);

        var extension = Path.GetExtension(inputPath).ToLowerInvariant();
        switch (extension)
        {
            case ".cue":
            case ".gdi":
            case ".nrg":
            case ".toc":
                return DetectDescriptor(inputPath);
            case ".iso":
            case ".bin":
            case ".img":
                return DetectRawFile(inputPath);
            default:
                return new DiscPlatformInfo(DiscPlatform.Unknown, null, null, "unsupported input extension");
        }
    }

    /// <summary>
    /// Selects the smart default codec list for a detected platform and content format
    /// (CHDlite <c>smart_compression_for</c> parity): PS2 DVD → zlib; PS2 CD → cdzl+cdfl;
    /// other DVD → zstd; other CD/GD-ROM → cdzs+cdfl. Returns <c>null</c> when no smart default
    /// applies (caller falls back to its own defaults).
    /// </summary>
    public static uint[]? AutoCodecs(DiscPlatform platform, string format)
    {
        switch (platform)
        {
            case DiscPlatform.Ps2 when string.Equals(format, "dvd", StringComparison.Ordinal):
                return [CodecTags.Zlib];
            case DiscPlatform.Ps2:
                return [CodecTags.Cdzl, CodecTags.Cdfl];
            case DiscPlatform.Dvd:
                return [CodecTags.Zstd];
            case DiscPlatform.GenericCd:
            case DiscPlatform.ThreeDo:
            case DiscPlatform.MegaCd:
            case DiscPlatform.Saturn:
            case DiscPlatform.Dreamcast:
            case DiscPlatform.Ps1:
            case DiscPlatform.Psp:
            case DiscPlatform.NeoGeoCd:
            case DiscPlatform.PcEngine:
                return [CodecTags.Cdzs, CodecTags.Cdfl];
            default:
                return null;
        }
    }

    private static DiscPlatformInfo DetectDescriptor(string descriptorPath)
    {
        try
        {
            var toc = CdImageParser.Parse(descriptorPath);
            if (toc.Tracks.Count == 0)
                return new DiscPlatformInfo(DiscPlatform.Unknown, null, null, "no tracks in descriptor");

            var firstDataTrack = toc.Tracks.FirstOrDefault(t => t.TrackType != CdTrackType.Audio);
            if (firstDataTrack.FileName is null)
                return new DiscPlatformInfo(DiscPlatform.Unknown, null, null, "no data track");

            var file = firstDataTrack.FileName;
            var frameSize = firstDataTrack.DataSize + firstDataTrack.SubSize;
            if (frameSize <= 0)
                return new DiscPlatformInfo(DiscPlatform.Unknown, null, null, "zero sector size");

            var isGdRom = (toc.Flags & CdTocFlags.GdRom) != 0;
            return DetectCore(ReadSector, isGdRom ? "gd" : "cd", "descriptor");

            byte[]? ReadSector(uint lba)
            {
                var offset = firstDataTrack.FileOffset + lba * frameSize;
                var fileLength = new FileInfo(file).Length;
                if (offset + Math.Min(frameSize, 2352) > fileLength)
                    return null;

                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                fs.Position = offset;
                var raw = new byte[Math.Min(frameSize, 2352)];
                if (fs.Read(raw, 0, raw.Length) != raw.Length)
                    return null;

                return ExtractCooked(raw, (uint)frameSize);
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or FileNotFoundException)
        {
            return new DiscPlatformInfo(DiscPlatform.Unknown, null, null, $"descriptor parse failed: {ex.Message}");
        }
    }

    private static DiscPlatformInfo DetectRawFile(string path)
    {
        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var length = probe.Length;
            uint frameSize = DetectSectorSize(probe, length);

            return DetectCore(ReadSector, frameSize == 2048 ? "dvd" : "cd", "raw file");

            byte[]? ReadSector(uint lba)
            {
                var offset = (long)lba * frameSize;
                if (offset + Math.Min(frameSize, 2352) > length)
                    return null;

                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                fs.Position = offset;
                var raw = new byte[Math.Min(frameSize, 2352)];
                if (fs.Read(raw, 0, raw.Length) != raw.Length)
                    return null;

                return ExtractCooked(raw, frameSize);
            }
        }
        catch (IOException ex)
        {
            return new DiscPlatformInfo(DiscPlatform.Unknown, null, null, $"cannot read input: {ex.Message}");
        }
    }

    /// <summary>Runs the shared CHDSharpLib detection core over a sector reader.</summary>
    private static DiscPlatformInfo DetectCore(DiscDetector.SectorReader readSector, string format, string source)
    {
        // GD-ROM images are always Dreamcast.
        if (string.Equals(format, "gd", StringComparison.Ordinal))
        {
            return new DiscPlatformInfo(DiscPlatform.Dreamcast, null, null, source + ", GD-ROM");
        }

        // 2048-byte sectors are DVDs; anything else is a CD.
        return string.Equals(format, "dvd", StringComparison.Ordinal)
            ? DiscDetector.DetectDvdFromSectors(readSector, source)
            : DiscDetector.DetectCdFromSectors(readSector, source);
    }

    private static uint DetectSectorSize(FileStream fs, long length)
    {
        // CD sync pattern: 00 FF FF FF FF FF FF FF FF FF FF 00
        Span<byte> header = stackalloc byte[16];
        fs.Position = 0;
        if (fs.Read(header) == header.Length && header[..12].SequenceEqual(CdSync))
        {
            if (length >= 2 * 2352)
            {
                fs.Position = 2352;
                Span<byte> sync2 = stackalloc byte[12];
                if (fs.Read(sync2) == 12 && sync2.SequenceEqual(CdSync))
                {
                }
            }

            return 2352;
        }

        if (length % 2048 == 0) return 2048;
        if (length % 2336 == 0) return 2336;
        if (length % 2352 == 0) return 2352;

        return 2048; // fallback
    }

    private static ReadOnlySpan<byte> CdSync => new byte[] { 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00 };

    private static byte[]? ExtractCooked(byte[] raw, uint frameSize)
    {
        return frameSize switch
        {
            2048 => raw,
            2352 => raw.AsSpan(raw[15] == 0x01 ? 16 : 24, 2048).ToArray(),
            2336 => raw.AsSpan(8, 2048).ToArray(),
            _ => raw.Length >= 2048 ? raw.AsSpan(0, 2048).ToArray() : null
        };
    }
}