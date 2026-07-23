using System.Globalization;
using System.Text.RegularExpressions;
using CHDSharp.Models;

namespace CHDSharp;

internal static partial class ChdTocParser
{
    private const string CdRomOldMetadataTag = "CHCD";
    private const string CdRomTrackMetadataTag = "CHTR";
    private const string CdRomTrackMetadata2Tag = "CHT2";
    private const string GdRomOldMetadataTag = "CHGT";
    private const string GdRomTrackMetadataTag = "CHGD";
    private const string DvdMetadataTag = "DVD ";
    private const string HardDiskMetadataTag = "GDDD";

    private const int TrackPadding = 4;

    private static readonly Regex KeyValueRegex = MyRegex();

    internal static List<ChdTrackInfo>? ParseTracks(IReadOnlyList<ChdMetadataEntry> metadata, out bool isGdRom)
    {
        isGdRom = false;

        var cht2Entries = metadata.Where(m => m.Tag == CdRomTrackMetadata2Tag).ToList();
        var chgdEntries = metadata.Where(m => m.Tag == GdRomTrackMetadataTag).ToList();
        var chtrEntries = metadata.Where(m => m.Tag == CdRomTrackMetadataTag).ToList();
        var chcdEntries = metadata.Where(m => m.Tag == CdRomOldMetadataTag).ToList();

        if (chgdEntries.Count > 0)
        {
            isGdRom = true;
            return ParseTextTracks(chgdEntries, TrackTypeParser.GdRom);
        }

        if (cht2Entries.Count > 0)
            return ParseTextTracks(cht2Entries, TrackTypeParser.Cht2);

        if (chtrEntries.Count > 0)
            return ParseTextTracks(chtrEntries, TrackTypeParser.Chtr);

        var chgtEntries = metadata.Where(m => m.Tag == GdRomOldMetadataTag).ToList();
        if (chgtEntries.Count > 0)
        {
            isGdRom = true;
            return ParseTextTracks(chgtEntries, TrackTypeParser.Chtr);
        }

        if (chcdEntries.Count > 0)
            return ParseBinaryTracks(chcdEntries);

        return null;
    }

    private enum TrackTypeParser { Chtr,
        Cht2,
        GdRom }

    private static List<ChdTrackInfo> ParseTextTracks(
        List<ChdMetadataEntry> entries, TrackTypeParser parser)
    {
        var tracks = new List<ChdTrackInfo>();
        ulong currentFrame = 0;

        foreach (var entry in entries)
        {
            var fields = ParseKeyValueFields(entry.GetText());
            if (fields.Count == 0) continue;

            if (!fields.TryGetValue("TRACK", out var trackNumStr)) continue;
            if (!int.TryParse(trackNumStr, out var trackNum)) continue;
            if (trackNum is < 1 or > 99) continue;

            if (!fields.TryGetValue("TYPE", out var typeStr)) continue;
            if (!fields.TryGetValue("SUBTYPE", out var subStr)) continue;
            if (!fields.TryGetValue("FRAMES", out var framesStr)) continue;

            var (trackType, dataSize) = ParseTypeString(typeStr);
            var (subType, subSize) = ParseSubTypeString(subStr);
            var frames = int.Parse(framesStr, CultureInfo.InvariantCulture);

            int pregap = 0, postgap = 0;
            var pgType = ChdTrackType.Mode1;
            var pgSub = ChdSubType.None;
            int pgDataSize = 0, pgSubSize = 0;
            var padFrames = 0;

            if (parser >= TrackTypeParser.Cht2 && fields.TryGetValue("PREGAP", out var pregapStr))
            {
                pregap = int.Parse(pregapStr, CultureInfo.InvariantCulture);

                if (fields.TryGetValue("PGTYPE", out var pgTypeStr))
                {
                    var pgHasData = pgTypeStr.StartsWith('V');
                    if (pgHasData)
                    {
                        pgTypeStr = pgTypeStr.Substring(1);
                    }

                    (pgType, pgDataSize) = ParseTypeString(pgTypeStr);
                    if (!pgHasData)
                    {
                        pgDataSize = 0;
                    }
                }

                if (fields.TryGetValue("PGSUB", out var pgSubStr))
                {
                    (pgSub, pgSubSize) = ParseSubTypeString(pgSubStr);
                }

                if (fields.TryGetValue("POSTGAP", out var postgapStr))
                {
                    postgap = int.Parse(postgapStr, CultureInfo.InvariantCulture);
                }
            }

            if (parser == TrackTypeParser.GdRom && fields.TryGetValue("PAD", out var padStr))
            {
                padFrames = int.Parse(padStr, CultureInfo.InvariantCulture);
            }

            var padded = (frames + TrackPadding - 1) / TrackPadding;
            var extraFrames = padded * TrackPadding - frames;

            var track = new ChdTrackInfo
            {
                TrackNumber = trackNum,
                TrackType = trackType,
                SubType = subType,
                DataSize = dataSize,
                SubSize = subSize,
                Frames = frames,
                ExtraFrames = extraFrames,
                PreGap = pregap,
                PostGap = postgap,
                PreGapType = pgType,
                PreGapSubType = pgSub,
                PreGapDataSize = pgDataSize,
                PreGapSubSize = pgSubSize,
                PadFrames = padFrames,
                StartFrame = currentFrame
            };

            tracks.Add(track);
            currentFrame += (ulong)(frames + extraFrames);
        }

        return tracks;
    }

    private static Dictionary<string, string> ParseKeyValueFields(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var matches = KeyValueRegex.Matches(text);
        foreach (Match m in matches)
        {
            if (m.Groups.Count >= 3)
            {
                var key = m.Groups[1].Value;
                var value = m.Groups[2].Value;
                // Trim trailing punctuation (e.g. period from truncated display)
                value = value.TrimEnd('.');
                result.TryAdd(key, value);
            }
        }
        return result;
    }

    private static List<ChdTrackInfo>? ParseBinaryTracks(List<ChdMetadataEntry> entries)
    {
        var data = entries[0].Data;
        if (data.Length < 4) return null;

        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);

        var trackCount = (int)ReadBeUInt32();
        var swapped = trackCount > 99;
        if (swapped)
        {
            trackCount = (int)SwapEndian((uint)trackCount);
        }

        var tracks = new List<ChdTrackInfo>();
        ulong currentFrame = 0;

        for (var i = 0; i < trackCount; i++)
        {
            var trkTypeVal = ReadBeUInt32();
            var subTypeVal = ReadBeUInt32();
            var dataSizeVal = ReadBeUInt32();
            var subSizeVal = ReadBeUInt32();
            var framesVal = ReadBeUInt32();
            var extraFramesVal = ReadBeUInt32();

            if (swapped)
            {
                trkTypeVal = SwapEndian(trkTypeVal);
                subTypeVal = SwapEndian(subTypeVal);
                dataSizeVal = SwapEndian(dataSizeVal);
                subSizeVal = SwapEndian(subSizeVal);
                framesVal = SwapEndian(framesVal);
                extraFramesVal = SwapEndian(extraFramesVal);
            }

            var track = new ChdTrackInfo
            {
                TrackNumber = i + 1,
                TrackType = (ChdTrackType)trkTypeVal,
                SubType = (ChdSubType)subTypeVal,
                DataSize = (int)dataSizeVal,
                SubSize = (int)subSizeVal,
                Frames = (int)framesVal,
                ExtraFrames = (int)extraFramesVal,
                StartFrame = currentFrame
            };

            tracks.Add(track);
            currentFrame += (ulong)((int)framesVal + (int)extraFramesVal);
        }

        return tracks;

        static uint SwapEndian(uint v)
        {
            return ((v & 0xFF) << 24) | ((v & 0xFF00) << 8) | ((v & 0xFF0000) >> 8) | ((v & 0xFF000000) >> 24);
        }

        uint ReadBeUInt32()
        {
            var bytes = br.ReadBytes(4);
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            return BitConverter.ToUInt32(bytes, 0);
        }
    }

    private static (ChdTrackType type, int dataSize) ParseTypeString(string str)
    {
        return str switch
        {
            "MODE1" or "MODE1/2048" => (ChdTrackType.Mode1, 2048),
            "MODE1_RAW" or "MODE1/2352" => (ChdTrackType.Mode1Raw, 2352),
            "MODE2" or "MODE2/2336" => (ChdTrackType.Mode2, 2336),
            "MODE2_FORM1" or "MODE2/2048" => (ChdTrackType.Mode2Form1, 2048),
            "MODE2_FORM2" or "MODE2/2324" => (ChdTrackType.Mode2Form2, 2324),
            "MODE2_FORM_MIX" => (ChdTrackType.Mode2FormMix, 2336),
            "MODE2_RAW" or "MODE2/2352" or "CDI/2352" => (ChdTrackType.Mode2Raw, 2352),
            "AUDIO" => (ChdTrackType.Audio, 2352),
            _ => (ChdTrackType.Mode1, 2048)
        };
    }

    private static (ChdSubType subType, int subSize) ParseSubTypeString(string str)
    {
        return str switch
        {
            "RW" => (ChdSubType.Normal, 96),
            "RW_RAW" => (ChdSubType.Raw, 96),
            _ => (ChdSubType.None, 0)
        };
    }

    internal static bool HasDvdMetadata(IReadOnlyList<ChdMetadataEntry> metadata)
    {
        return metadata.Any(m => m.Tag == DvdMetadataTag);
    }

    internal static bool HasHddMetadata(IReadOnlyList<ChdMetadataEntry> metadata)
    {
        return metadata.Any(m => m.Tag == HardDiskMetadataTag);
    }

    [GeneratedRegex(@"(\w+) *: *([^ ]+)", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex MyRegex();
}
