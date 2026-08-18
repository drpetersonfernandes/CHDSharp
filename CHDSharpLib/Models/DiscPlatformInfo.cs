namespace CHDSharp.Models;

/// <summary>Disc game platforms detected by <see cref="CHDSharp.DiscDetector"/> (CHDlite
/// <c>GamePlatform</c> parity, <c>detect_game_platform.cpp</c>).</summary>
public enum DiscPlatform
{
    /// <summary>Not detected / not a game disc.</summary>
    Unknown = 0,

    /// <summary>A CD-ROM with no recognizable platform marker.</summary>
    GenericCD = 1,

    /// <summary>Panasonic 3DO (Opera filesystem, sector 0 magic).</summary>
    ThreeDO = 2,

    /// <summary>Sega Mega CD / Sega CD (sector 0 "SEGADISC..." magic).</summary>
    MegaCD = 3,

    /// <summary>Sega Saturn (sector 0 "SEGA SEGASATURN " magic).</summary>
    Saturn = 4,

    /// <summary>Sega Dreamcast (GD-ROM, "SEGA SEGAKATANA " magic).</summary>
    Dreamcast = 5,

    /// <summary>Sony PlayStation (SYSTEM.CNF with BOOT).</summary>
    PS1 = 6,

    /// <summary>Sony PlayStation 2 (SYSTEM.CNF with BOOT2, or DVD Video marked PS2).</summary>
    PS2 = 7,

    /// <summary>Sony PlayStation Portable (PSP_GAME/PARAM.SFO).</summary>
    PSP = 8,

    /// <summary>SNK Neo Geo CD (IPL.TXT).</summary>
    NeoGeoCD = 9,

    /// <summary>NEC PC Engine / TurboGrafx-16 CD (IPL header heuristic).</summary>
    PCEngine = 10,

    /// <summary>DVD-Video (VIDEO_TS/VIDEO_TS.IFO) or a generic DVD.</summary>
    DVD = 11
}

/// <summary>The result of <see cref="CHDSharp.DiscDetector"/>: detected platform plus optional
/// title and manufacturer (product) ID extracted from the disc's filesystem.</summary>
/// <param name="Platform">The detected platform (see <see cref="DiscPlatform"/>).</param>
/// <param name="Title">Extracted game title, or <c>null</c> when unavailable.</param>
/// <param name="ManufacturerId">Extracted product/serial number (e.g. "SCPS_100.50",
/// "T-9527G", "ULJM05325"), or <c>null</c> when unavailable.</param>
/// <param name="Source">Human-readable description of how the platform was detected
/// (sector 0 magic, ISO-9660 path, IPL header, ...).</param>
public sealed record DiscPlatformInfo(DiscPlatform Platform, string? Title, string? ManufacturerId, string Source)
{
    /// <summary>The platform as a lowercase string ("cd", "dvd", "ps1", "dreamcast", ...),
    /// or "unknown".</summary>
    public string Name => Platform switch
    {
        DiscPlatform.GenericCD => "cd",
        DiscPlatform.ThreeDO => "3do",
        DiscPlatform.MegaCD => "megacd",
        DiscPlatform.Saturn => "saturn",
        DiscPlatform.Dreamcast => "dreamcast",
        DiscPlatform.PS1 => "ps1",
        DiscPlatform.PS2 => "ps2",
        DiscPlatform.PSP => "psp",
        DiscPlatform.NeoGeoCD => "neogeocd",
        DiscPlatform.PCEngine => "pcengine",
        DiscPlatform.DVD => "dvd",
        _ => "unknown"
    };

    /// <inheritdoc/>
    public override string ToString()
    {
        var result = Name;
        if (Title is { Length: > 0 })
            result += $" \"{Title}\"";
        if (ManufacturerId is { Length: > 0 })
            result += $" [{ManufacturerId}]";
        return result;
    }
}