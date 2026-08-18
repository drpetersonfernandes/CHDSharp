using System.Buffers.Binary;
using System.Text;
using CHDSharp.Models;

namespace CHDSharp;

/// <summary>
/// Detects the game platform of a disc image by probing sector-0 magic bytes, the ISO-9660
/// filesystem (PS1/PS2 SYSTEM.CNF, PSP PARAM.SFO, Neo Geo IPL.TXT, DVD Video), and the PC Engine
/// IPL header heuristic (CHDlite <c>detect_game_platform</c> parity,
/// <c>detect_game_platform.cpp:51-318</c>). Sector access is abstracted so both CHD files and
/// raw/disc images can be probed.
/// </summary>
public static class DiscDetector
{
    /// <summary>Reads one cooked 2048-byte sector by LBA; returns <c>null</c> on failure.</summary>
    public delegate byte[]? SectorReader(uint lba);

    private const string Ps1BootKey = "BOOT";
    private const string Ps2BootKey = "BOOT2";

    /// <summary>
    /// Detects the platform of a CHD file on disk: opens it (optionally with a parent) and runs
    /// the sector-based detection over its decompressed content.
    /// </summary>
    /// <param name="filename">Path to the CHD file.</param>
    /// <param name="parentFilename">Parent CHD path for a child CHD, or <c>null</c> for standalone.</param>
    /// <returns>The detection result (platform, title, manufacturer ID).</returns>
    /// <exception cref="InvalidDataException">The CHD cannot be opened.</exception>
    public static DiscPlatformInfo DetectChd(string filename, string? parentFilename = null)
    {
        var err = ChdFile.Open(filename, parentFilename, out var chd);
        if (err != ChdError.Chderrnone || chd == null)
            throw new InvalidDataException($"Cannot open CHD '{filename}' ({err.GetMessage()} ({err}))");

        using (chd)
        {
            return Detect(chd);
        }
    }

    /// <summary>Detects the platform of an already-open <see cref="ChdFile"/>.</summary>
    /// <param name="chd">An open CHD file (CD, DVD, or raw image).</param>
    public static DiscPlatformInfo Detect(ChdFile chd)
    {
        ArgumentNullException.ThrowIfNull(chd);

        byte[]? ReadCooked(uint lba)
        {
            return ReadChdCookedSector(chd, lba);
        }

        // GD-ROM: always Dreamcast (MAME never classified them otherwise).
        if (chd.IsGdRom)
        {
            const DiscPlatform platform = DiscPlatform.Dreamcast;
            return new DiscPlatformInfo(platform, ExtractTitle(ReadCooked, platform, null, 0), ExtractManufacturerId(ReadCooked, platform, null, 0), "GD-ROM metadata");
        }

        if (chd.IsDvd)
            return DetectDvdFromSectors(ReadCooked, "CHD DVD metadata");

        return DetectCdFromSectors(ReadCooked, "CHD CD data track");
    }

    /// <summary>Runs the DVD detection dispatch over a sector reader: ISO-9660 filesystem checks
    /// (PSP/PS1/PS2/DVD-Video), falling back to the DVD platform with the volume ID as title.</summary>
    /// <param name="readSector">Reads cooked 2048-byte sectors by LBA.</param>
    /// <param name="source">Human-readable description of the data source.</param>
    public static DiscPlatformInfo DetectDvdFromSectors(SectorReader readSector, string source)
    {
        var pvd = ReadPvd(readSector, 0);
        if (pvd != null)
        {
            var platform = CheckIsoFiles(readSector, pvd.Value);
            if (platform != DiscPlatform.Unknown)
            {
                return new DiscPlatformInfo(platform,
                    ExtractTitle(readSector, platform, pvd.Value, 0),
                    ExtractManufacturerId(readSector, platform, pvd.Value, 0),
                    source + ", ISO-9660 filesystem");
            }
        }

        return new DiscPlatformInfo(DiscPlatform.DVD,
            pvd != null ? TrimRight(Encoding.ASCII.GetString(pvd.Value.VolumeId)) : null,
            null,
            source);
    }

    /// <summary>Runs the CD detection dispatch over a sector reader: sector-0 magic checks
    /// (3DO/Mega CD/Saturn/Dreamcast), ISO-9660 filesystem checks (PS1/PS2/PSP/Neo Geo/DVD-Video),
    /// then the PC Engine IPL heuristic; falls back to <see cref="DiscPlatform.GenericCD"/>.</summary>
    /// <param name="readSector">Reads cooked 2048-byte sectors by LBA.</param>
    /// <param name="source">Human-readable description of the data source.</param>
    public static DiscPlatformInfo DetectCdFromSectors(SectorReader readSector, string source)
    {
        // CD-ROM path: sector 0 magics, then ISO-9660, then the PC Engine heuristic.
        var pvd = ReadPvd(readSector, 0);

        var sector0 = readSector(0);
        if (sector0 != null)
        {
            var platform = CheckSector0Magics(sector0);
            if (platform != DiscPlatform.Unknown)
            {
                return new DiscPlatformInfo(platform,
                    ExtractTitle(readSector, platform, null, 0),
                    ExtractManufacturerId(readSector, platform, null, 0),
                    source + ", sector 0 magic");
            }
        }

        if (pvd != null)
        {
            var isoPlatform = CheckIsoFiles(readSector, pvd.Value);
            if (isoPlatform != DiscPlatform.Unknown)
            {
                return new DiscPlatformInfo(isoPlatform,
                    ExtractTitle(readSector, isoPlatform, pvd.Value, 0),
                    ExtractManufacturerId(readSector, isoPlatform, pvd.Value, 0),
                    source + ", ISO-9660 filesystem");
            }
        }

        // PC Engine heuristic: IPL header at the second sector of the first data track.
        var pceSource = CheckPcEngine(readSector);
        if (pceSource != null)
        {
            return new DiscPlatformInfo(DiscPlatform.PCEngine,
                ExtractTitle(readSector, DiscPlatform.PCEngine, pvd, 1),
                ExtractManufacturerId(readSector, DiscPlatform.PCEngine, pvd, 1),
                source + ", " + pceSource);
        }

        return new DiscPlatformInfo(DiscPlatform.GenericCD,
            ExtractTitle(readSector, DiscPlatform.GenericCD, pvd, 0),
            ExtractManufacturerId(readSector, DiscPlatform.GenericCD, pvd, 0),
            source + ", no platform markers found");
    }

    /// <summary>Reads a cooked 2048-byte sector from a CHD by LBA. For CD images the sector is
    /// taken from the first data track (the ISO-9660 area): Mode 1/2 sync+header bytes are
    /// stripped from 2352-byte frames. For DVD/raw images the LBA maps directly to bytes.</summary>
    private static byte[]? ReadChdCookedSector(ChdFile chd, uint lba)
    {
        ulong startByte;
        int dataSize;
        int frameSize;

        if (chd.Tracks is { Count: > 0 } tracks)
        {
            ChdTrackInfo? dataTrack = null;
            foreach (var t in tracks)
            {
                if (t.TrackType != ChdTrackType.Audio)
                {
                    dataTrack = t;
                    break;
                }
            }

            if (dataTrack == null)
                return null;

            startByte = dataTrack.StartFrame * chd.UnitBytes;
            dataSize = dataTrack.DataSize;
            frameSize = (int)chd.UnitBytes;
        }
        else
        {
            startByte = 0;
            dataSize = 2048;
            frameSize = 2048;
        }

        if (frameSize <= 0)
            return null;

        var frameOffset = (long)(startByte + (ulong)lba * (ulong)frameSize);
        if (frameOffset + dataSize > (long)chd.TotalBytes)
            return null;

        var frame = new byte[dataSize];
        if (chd.Read((ulong)frameOffset, frame, 0, dataSize) != ChdError.Chderrnone)
            return null;

        // Extract the 2048 cooked bytes from a raw sector.
        return dataSize switch
        {
            2048 => frame,
            2352 => frame.AsSpan(frame[15] == 0x01 ? 16 : 24, 2048).ToArray(),
            2336 => frame.AsSpan(8, 2048).ToArray(),
            _ => frame.Length >= 2048 ? frame.AsSpan(0, 2048).ToArray() : null
        };
    }

    // ── Sector 0 magic checks ──

    private static DiscPlatform CheckSector0Magics(byte[] sector0)
    {
        if (Check3do(sector0)) return DiscPlatform.ThreeDO;
        if (CheckMegaCd(sector0)) return DiscPlatform.MegaCD;
        if (CheckSaturn(sector0)) return DiscPlatform.Saturn;
        if (CheckDreamcast(sector0)) return DiscPlatform.Dreamcast;
        return DiscPlatform.Unknown;
    }

    private static bool Check3do(byte[] sector0)
    {
        return sector0.Length >= 7 &&
               sector0[0] == 0x01 && sector0[6] == 0x01 &&
               sector0[1] == 0x5A && sector0[2] == 0x5A && sector0[3] == 0x5A &&
               sector0[4] == 0x5A && sector0[5] == 0x5A;
    }

    private static bool CheckMegaCd(byte[] sector0)
    {
        return StartsWith(sector0, "SEGADISCSYSTEM"u8) ||
               StartsWith(sector0, "SEGABOOTDISC"u8) ||
               StartsWith(sector0, "SEGADISC"u8) ||
               StartsWith(sector0, "SEGADATADISC"u8);
    }

    private static bool CheckSaturn(byte[] sector0) => StartsWith(sector0, "SEGA SEGASATURN "u8);

    private static bool CheckDreamcast(byte[] sector0) => StartsWith(sector0, "SEGA SEGAKATANA "u8);

    private static bool StartsWith(byte[] data, ReadOnlySpan<byte> prefix)
    {
        return data.Length >= prefix.Length && data.AsSpan(0, prefix.Length).SequenceEqual(prefix);
    }

    // ── ISO-9660 filesystem checks ──

    private readonly record struct Pvd(byte[] SystemId, byte[] VolumeId, uint RootExtent, uint RootLength);

    private static Pvd? ReadPvd(SectorReader readSector, uint baseLba)
    {
        var sector = readSector(baseLba + 16);
        if (sector == null || sector.Length < 2048)
            return null;

        // PVD: type 1, "CD001" at offset 1.
        if (sector[0] != 0x01 || !sector.AsSpan(1, 5).SequenceEqual("CD001"u8))
            return null;

        var root = sector.AsSpan(156);
        return new Pvd(
            sector.AsSpan(8, 32).ToArray(),
            sector.AsSpan(40, 32).ToArray(),
            ReadLe32(root[2..]),
            ReadLe32(root[10..]));
    }

    private static List<(string Name, uint Extent, uint Length, bool IsDirectory)> ReadDirectory(SectorReader readSector, uint extentLba, uint length)
    {
        var entries = new List<(string, uint, uint, bool)>();
        var sectors = Math.Min((length + 2047) / 2048, 32u);
        for (uint s = 0; s < sectors; s++)
        {
            var sector = readSector(extentLba + s);
            if (sector == null)
                break;

            uint offset = 0;
            while (offset + 33 < 2048)
            {
                byte recLen = sector[offset];
                if (recLen < 33 || offset + recLen > 2048)
                    break;

                var rec = sector.AsSpan((int)offset);
                byte nameLen = rec[32];
                byte flags = rec[25];
                if (nameLen > 1)
                {
                    var rawName = Encoding.ASCII.GetString(rec.Slice(33, nameLen));
                    var semi = rawName.IndexOf(';');
                    if (semi >= 0)
                        rawName = rawName[..semi];
                    if (rawName.EndsWith(".", StringComparison.Ordinal))
                        rawName = rawName[..^1];

                    entries.Add((rawName, ReadLe32(rec[2..]), ReadLe32(rec[10..]), (flags & 0x02) != 0));
                }

                offset += recLen;
            }
        }

        return entries;
    }

    private static (uint Extent, uint Length, bool IsDirectory)? FindInDirectory(SectorReader readSector, uint dirExtent, uint dirLength, string name)
    {
        foreach (var entry in ReadDirectory(readSector, dirExtent, dirLength))
        {
            if (string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
                return (entry.Extent, entry.Length, entry.IsDirectory);
        }

        return null;
    }

    private static bool IsoFileExists(SectorReader readSector, Pvd pvd, string path)
    {
        uint dirExtent = pvd.RootExtent;
        uint dirLength = pvd.RootLength;

        var components = path.Split('/');
        for (var i = 0; i < components.Length; i++)
        {
            var found = FindInDirectory(readSector, dirExtent, dirLength, components[i]);
            if (found == null)
                return false;

            if (i == components.Length - 1)
                return true;

            if (!found.Value.IsDirectory)
                return false;

            dirExtent = found.Value.Extent;
            dirLength = found.Value.Length;
        }

        return false;
    }

    private static byte[]? IsoReadFile(SectorReader readSector, Pvd pvd, string path, uint maxBytes = 4096)
    {
        uint dirExtent = pvd.RootExtent;
        uint dirLength = pvd.RootLength;

        var components = path.Split('/');
        for (var i = 0; i < components.Length; i++)
        {
            var found = FindInDirectory(readSector, dirExtent, dirLength, components[i]);
            if (found == null)
                return null;

            if (i == components.Length - 1)
            {
                if (found.Value.IsDirectory)
                    return null;

                var toRead = Math.Min(found.Value.Length, maxBytes);
                var content = new List<byte>((int)toRead);
                uint remaining = toRead;
                uint lba = found.Value.Extent;
                while (remaining > 0)
                {
                    var sector = readSector(lba);
                    if (sector == null)
                        break;

                    var chunk = (int)Math.Min(remaining, 2048u);
                    content.AddRange(sector.AsSpan(0, chunk).ToArray());
                    remaining -= (uint)chunk;
                    lba++;
                }

                return content.ToArray();
            }

            if (!found.Value.IsDirectory)
                return null;

            dirExtent = found.Value.Extent;
            dirLength = found.Value.Length;
        }

        return null;
    }

    private static DiscPlatform CheckIsoFiles(SectorReader readSector, Pvd pvd)
    {
        var cnf = IsoReadFile(readSector, pvd, "SYSTEM.CNF");
        if (cnf is { Length: > 0 })
        {
            var text = Encoding.ASCII.GetString(cnf);
            if (text.Contains(Ps2BootKey, StringComparison.Ordinal))
                return DiscPlatform.PS2;
            if (text.Contains(Ps1BootKey, StringComparison.Ordinal))
                return DiscPlatform.PS1;
        }

        if (IsoFileExists(readSector, pvd, "PSP_GAME/PARAM.SFO"))
            return DiscPlatform.PSP;
        if (IsoFileExists(readSector, pvd, "IPL.TXT"))
            return DiscPlatform.NeoGeoCD;
        if (IsoFileExists(readSector, pvd, "VIDEO_TS/VIDEO_TS.IFO"))
            return DiscPlatform.DVD;

        return DiscPlatform.Unknown;
    }

    // ── PC Engine heuristic ──

    private static string? CheckPcEngine(SectorReader readSector)
    {
        // The IPL header lives at the second logical sector of the data track (LBA 1).
        var sector1 = readSector(1);
        if (sector1 != null && LooksLikePceIpl(sector1))
            return "PC Engine IPL header at LBA 1";

        var sector0 = readSector(0);
        if (sector0 != null && LooksLikePceIpl(sector0))
            return "PC Engine IPL header at LBA 0";

        return null;
    }

    private static bool LooksLikePceIpl(byte[] sector)
    {
        if (sector.Length >= 32 + 23 &&
            sector.AsSpan(32, 23).SequenceEqual("PC Engine CD-ROM SYSTEM"u8))
            return true;

        if (sector.Length < 13)
            return false;

        byte iplbln = sector[0x03];
        ushort iplsta = ReadLe16(sector.AsSpan(0x04));
        ushort ipljmp = ReadLe16(sector.AsSpan(0x06));
        if (iplbln == 0 || iplsta < 0x2000 || ipljmp == 0)
            return false;
        for (var i = 0; i < 5; i++)
        {
            if (sector[0x08 + i] > 0x7F)
                return false;
        }

        return true;
    }

    // ── Title / manufacturer ID extraction ──

    private static string? ExtractTitle(SectorReader readSector, DiscPlatform platform, Pvd? pvd, uint dataLba)
    {
        switch (platform)
        {
            case DiscPlatform.PSP:
                return ReadSfoKey(readSector, pvd, "TITLE");
            case DiscPlatform.Saturn:
            {
                var sector0 = readSector(0);
                return sector0 != null ? TrimRight(Encoding.ASCII.GetString(sector0, 0x60, 112)) : null;
            }
            case DiscPlatform.MegaCD:
            {
                var sector0 = readSector(0);
                return sector0 != null ? TrimRight(Encoding.ASCII.GetString(sector0, 0x120, 48)) : null;
            }
            case DiscPlatform.Dreamcast:
            {
                var sector0 = readSector(0);
                return sector0 != null ? TrimRight(Encoding.ASCII.GetString(sector0, 0x80, 128)) : null;
            }
            case DiscPlatform.ThreeDO:
            {
                var sector0 = readSector(0);
                return sector0 != null ? TrimRight(Encoding.ASCII.GetString(sector0, 0x28, 32)) : null;
            }
            case DiscPlatform.NeoGeoCD:
            {
                var ipl = pvd != null ? IsoReadFile(readSector, pvd.Value, "IPL.TXT", 1024) : null;
                if (ipl is { Length: > 0 })
                {
                    var text = Encoding.ASCII.GetString(ipl);
                    var nl = text.IndexOfAny('\r', '\n');
                    var firstLine = nl >= 0 ? text[..nl] : text;
                    var trimmed = TrimRight(firstLine);
                    if (trimmed.Length > 0)
                        return trimmed;
                }

                break;
            }
            case DiscPlatform.PCEngine:
            {
                var sector1 = readSector(dataLba + 1);
                if (sector1 != null && sector1.Length >= 128 &&
                    sector1.AsSpan(32, 23).SequenceEqual("PC Engine CD-ROM SYSTEM"u8))
                {
                    var title = TrimRight(Encoding.ASCII.GetString(sector1, 106, 22));
                    if (title.Length > 0)
                        return title;
                }

                break;
            }
            case DiscPlatform.PS1:
            case DiscPlatform.PS2:
            case DiscPlatform.GenericCD:
            case DiscPlatform.DVD:
            default:
                break;
        }

        if (pvd != null)
        {
            var volumeId = TrimRight(Encoding.ASCII.GetString(pvd.Value.VolumeId));
            if (volumeId.Length > 0)
                return volumeId;
        }

        return null;
    }

    private static string? ExtractManufacturerId(SectorReader readSector, DiscPlatform platform, Pvd? pvd, uint dataLba)
    {
        switch (platform)
        {
            case DiscPlatform.PS1:
            case DiscPlatform.PS2:
            {
                var cnf = pvd != null ? IsoReadFile(readSector, pvd.Value, "SYSTEM.CNF") : null;
                if (cnf is { Length: > 0 })
                {
                    var text = Encoding.ASCII.GetString(cnf);
                    var bootKey = platform == DiscPlatform.PS2 ? Ps2BootKey : Ps1BootKey;
                    var pos = text.IndexOf(bootKey, StringComparison.Ordinal);
                    if (pos >= 0)
                    {
                        var start = text.IndexOfAny(new[] { '\\', ':' }, pos);
                        if (start >= 0)
                        {
                            start++;
                            var end = text.IndexOfAny([';', '\r', '\n'], start);
                            if (end < 0)
                                end = text.Length;
                            if (end > start)
                                return TrimRight(text[start..end]);
                        }
                    }
                }

                break;
            }
            case DiscPlatform.PSP:
                return ReadSfoKey(readSector, pvd, "DISC_ID");
            case DiscPlatform.Saturn:
            {
                var sector0 = readSector(0);
                return sector0 != null ? TrimRight(Encoding.ASCII.GetString(sector0, 0x20, 10)) : null;
            }
            case DiscPlatform.Dreamcast:
            {
                var sector0 = readSector(0);
                return sector0 != null ? TrimRight(Encoding.ASCII.GetString(sector0, 0x40, 10)) : null;
            }
            case DiscPlatform.MegaCD:
            {
                var sector0 = readSector(0);
                return sector0 != null ? TrimRight(Encoding.ASCII.GetString(sector0, 0x183, 11)) : null;
            }
            case DiscPlatform.ThreeDO:
            case DiscPlatform.NeoGeoCD:
            case DiscPlatform.PCEngine:
            case DiscPlatform.GenericCD:
            case DiscPlatform.DVD:
            default:
                break;
        }

        return null;
    }

    /// <summary>Reads a string value from a PSP PARAM.SFO binary key-value store.</summary>
    private static string? ReadSfoKey(SectorReader readSector, Pvd? pvd, string key)
    {
        if (pvd == null)
            return null;

        var sfo = IsoReadFile(readSector, pvd.Value, "PSP_GAME/PARAM.SFO", 16384);
        if (sfo is not { Length: >= 20 })
            return null;

        // Header: magic 0x00505346, key_table_start (8), data_table_start (12), entry count (16).
        if (sfo[0] != 0x00 || sfo[1] != 0x50 || sfo[2] != 0x53 || sfo[3] != 0x46)
            return null;

        uint keyTable = ReadLe32(sfo.AsSpan(8));
        uint dataTable = ReadLe32(sfo.AsSpan(12));
        uint numEntries = ReadLe32(sfo.AsSpan(16));

        for (uint i = 0; i < numEntries && 20 + (i + 1) * 16 <= sfo.Length; i++)
        {
            var idx = sfo.AsSpan((int)(20 + i * 16));
            ushort keyOffset = ReadLe16(idx);
            uint dataOffset = ReadLe32(idx[12..]);
            int keyPos = (int)(keyTable + keyOffset);
            int dataPos = (int)(dataTable + dataOffset);
            if (keyPos < 0 || keyPos >= sfo.Length || dataPos < 0 || dataPos >= sfo.Length)
                continue;

            var keyEnd = Array.IndexOf(sfo, (byte)0, keyPos);
            if (keyEnd < 0)
                continue;

            var keyName = Encoding.ASCII.GetString(sfo, keyPos, keyEnd - keyPos);
            if (string.Equals(keyName, key, StringComparison.Ordinal))
            {
                var valueEnd = Array.IndexOf(sfo, (byte)0, dataPos);
                if (valueEnd < 0)
                    valueEnd = sfo.Length;
                return TrimRight(Encoding.ASCII.GetString(sfo, dataPos, valueEnd - dataPos));
            }
        }

        return null;
    }

    private static string TrimRight(string s)
    {
        var end = s.Length;
        while (end > 0 && (s[end - 1] == ' ' || s[end - 1] == '\t' || s[end - 1] == '\r' || s[end - 1] == '\n' || s[end - 1] == '\0'))
            end--;
        return s[..end];
    }

    private static uint ReadLe32(ReadOnlySpan<byte> b) => BinaryPrimitives.ReadUInt32LittleEndian(b);

    private static ushort ReadLe16(ReadOnlySpan<byte> b) => BinaryPrimitives.ReadUInt16LittleEndian(b);
}