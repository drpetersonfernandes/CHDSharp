using System.Globalization;
using System.Text;
using CHDSharpEncoder.Models;

namespace CHDSharpEncoder;

/// <summary>
/// Parses CUE sheets (CUE/BIN, CUE/ISO, CUE/WAV) into a <see cref="CdToc"/>.
/// The parsing logic mirrors MAME's <c>cdrom_file::parse_cue</c> (src/lib/util/cdrom.cpp),
/// including track length and file offset resolution.
/// </summary>
public abstract class CueParser
{
    /// <summary>
    /// Parses a CUE sheet file into a table of contents.
    /// </summary>
    /// <param name="cueFilePath">Path to the .cue file.</param>
    /// <returns>The parsed table of contents.</returns>
    /// <exception cref="FileNotFoundException">The CUE file or a referenced data file does not exist.</exception>
    /// <exception cref="InvalidDataException">The CUE file is malformed or uses an unsupported track/file type.</exception>
    public static CdToc Parse(string cueFilePath)
    {
        ArgumentNullException.ThrowIfNull(cueFilePath);
        if (!File.Exists(cueFilePath))
            throw new FileNotFoundException($"CUE file not found: {cueFilePath}", cueFilePath);

        var baseDir = Path.GetDirectoryName(Path.GetFullPath(cueFilePath)) ?? string.Empty;

        var toc = new CdToc();
        var tracks = toc.Tracks;
        CdTrack? currentTrack = null;
        var lastFile = string.Empty;
        long wavLength = 0;
        long wavOffset = 0;

        foreach (var rawLine in File.ReadLines(cueFilePath))
        {
            var tokens = Tokenize(rawLine);
            if (tokens.Count == 0)
                continue;

            switch (tokens[0])
            {
                case "FILE":
                {
                    if (tokens.Count < 3)
                        throw new InvalidDataException($"Malformed FILE command: {rawLine}");

                    lastFile = Path.Combine(baseDir, tokens[1]);
                    switch (tokens[2])
                    {
                        case "BINARY":
                        case "MOTOROLA":
                            break;
                        case "WAVE":
                            (wavLength, wavOffset) = ParseWavSample(lastFile);
                            if (wavLength == 0)
                                throw new InvalidDataException($"Couldn't read [{lastFile}] or not a valid .WAV");

                            break;
                        default:
                            throw new InvalidDataException($"Unhandled file type [{tokens[2]}]");
                    }

                    break;
                }

                case "TRACK":
                {
                    if (tokens.Count < 3)
                        throw new InvalidDataException($"Malformed TRACK command: {rawLine}");
                    if (!int.TryParse(tokens[1], NumberStyles.None, CultureInfo.InvariantCulture, out var trackNumber) ||
                        trackNumber < 1 || trackNumber > CdConstants.MaxTracks)
                        throw new InvalidDataException($"Invalid track number [{tokens[1]}]");

                    if (currentTrack is { } previous)
                        tracks.Add(previous);

                    var track = new CdTrack
                    {
                        Number = trackNumber,
                        FileName = lastFile,
                        SubType = CdSubType.None,
                        SubSize = 0,
                        PgSub = CdSubType.None,
                        Pregap = 0,
                        Postgap = 0,
                        PgType = 0,
                        PgDataSize = 0,
                        Index00 = -1,
                        Index01 = -1
                    };

                    ParseTrackType(tokens[2], ref track);
                    if (tokens.Count >= 4)
                        ParseSubType(tokens[3], ref track);

                    if (wavLength != 0)
                    {
                        track.Frames = (int)(wavLength / CdConstants.MaxSectorData);
                        track.FileOffset = wavOffset;
                        wavLength = 0;
                        wavOffset = 0;
                    }

                    currentTrack = track;
                    break;
                }

                case "INDEX":
                {
                    if (currentTrack == null)
                        throw new InvalidDataException($"INDEX command without a preceding TRACK: {rawLine}");
                    if (tokens.Count < 3)
                        throw new InvalidDataException($"Malformed INDEX command: {rawLine}");
                    if (!int.TryParse(tokens[1], NumberStyles.None, CultureInfo.InvariantCulture, out var indexNumber) ||
                        indexNumber < 0 || indexNumber > CdConstants.MaxIndex)
                        throw new InvalidDataException($"Encountered invalid index [{tokens[1]}]");

                    var track = currentTrack.Value;
                    var frames = ParseMsfToFrames(tokens[2]);

                    switch (indexNumber)
                    {
                        case 1:
                        {
                            if (track.Pregap == 0 && track.Index00 != -1)
                            {
                                track.Pregap = frames - track.Index00;
                                track.PgType = track.TrackType;
                                track.PgDataSize = track.DataSize;
                            }
                            else if (track.Index00 == -1)
                            {
                                // no pregap sectors in the file; INDEX 00 defaults to the INDEX 01 position
                                track.Index00 = frames;
                            }

                            track.Index01 = frames;
                            break;
                        }
                        case 0:
                            track.Index00 = frames;
                            break;
                    }

                    currentTrack = track;
                    break;
                }

                case "PREGAP":
                {
                    if (currentTrack == null)
                        throw new InvalidDataException($"PREGAP command without a preceding TRACK: {rawLine}");
                    if (tokens.Count < 2)
                        throw new InvalidDataException($"Malformed PREGAP command: {rawLine}");

                    var track = currentTrack.Value;
                    track.Pregap = ParseMsfToFrames(tokens[1]);
                    currentTrack = track;
                    break;
                }

                case "POSTGAP":
                {
                    if (currentTrack == null)
                        throw new InvalidDataException($"POSTGAP command without a preceding TRACK: {rawLine}");
                    if (tokens.Count < 2)
                        throw new InvalidDataException($"Malformed POSTGAP command: {rawLine}");

                    var track = currentTrack.Value;
                    track.Postgap = ParseMsfToFrames(tokens[1]);
                    currentTrack = track;
                    break;
                }
            }
        }

        if (currentTrack is { } last)
            tracks.Add(last);

        ResolveTrackLengths(tracks);
        return toc;
    }

    /// <summary>
    /// Converts an MM:SS:FF (or bare frame count) token into a frame count.
    /// Matches MAME's <c>msf_to_frames</c>.
    /// </summary>
    public static int ParseMsfToFrames(string token)
    {
        var parts = token.Split(':');
        switch (parts.Length)
        {
            case 1:
            {
                if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var frames))
                    throw new InvalidDataException($"Invalid frame count [{token}]");

                return frames;
            }
            case 3 when
                int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) &&
                int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) &&
                int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var frame):
                return minutes * 60 * 75 + seconds * 75 + frame;
            default:
                throw new InvalidDataException($"Invalid MSF time format [{token}]");
        }
    }

    private static void ResolveTrackLengths(List<CdTrack> tracks)
    {
        for (var i = 0; i < tracks.Count; i++)
        {
            var track = tracks[i];

            if (track.Index01 == -1)
                throw new InvalidDataException($"Track {track.Number} is missing INDEX 01 marker");

            // audio data must be byte-swapped for CHD storage
            if (track.TrackType == CdTrackType.Audio)
            {
                track.Swap = true;
            }

            // WAV tracks already have their length and offset resolved
            if (track.FileOffset != 0)
            {
                tracks[i] = track;
                continue;
            }

            var sameFileAsPrev = i > 0 && string.Equals(track.FileName, tracks[i - 1].FileName, StringComparison.Ordinal);
            var sameFileAsNext = i + 1 < tracks.Count && string.Equals(track.FileName, tracks[i + 1].FileName, StringComparison.Ordinal);

            if (i + 1 >= tracks.Count && sameFileAsPrev)
            {
                // last track in a shared file: remainder of the file
                var prevSize = (long)tracks[i - 1].Frames * (tracks[i - 1].DataSize + tracks[i - 1].SubSize);
                track.FileOffset = tracks[i - 1].FileOffset + prevSize;
                track.Frames = (int)((GetFileSize(track.FileName!) - track.FileOffset) / (track.DataSize + track.SubSize));
            }
            else if (sameFileAsNext)
            {
                track.Frames = tracks[i + 1].Index00 - track.Index00;
                if (track.Frames == 0)
                    throw new InvalidDataException($"Unable to determine size of track {track.Number}, missing INDEX 01 markers?");

                if (i > 0)
                {
                    var prevSize = (long)tracks[i - 1].Frames * (tracks[i - 1].DataSize + tracks[i - 1].SubSize);
                    track.FileOffset = tracks[i - 1].FileOffset + prevSize;
                }
            }
            else if (track.Frames == 0)
            {
                // standalone file: whole file is the track
                track.Frames = (int)(GetFileSize(track.FileName!) / (track.DataSize + track.SubSize));
                track.FileOffset = 0;
            }

            tracks[i] = track;
        }
    }

    private static long GetFileSize(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Couldn't find bin file [{path}]", path);

        return new FileInfo(path).Length;
    }

    internal static void ParseTrackType(string typeString, ref CdTrack track)
    {
        switch (typeString)
        {
            case "MODE1":
            case "MODE1/2048":
                track.TrackType = CdTrackType.Mode1;
                track.DataSize = 2048;
                break;
            case "MODE1_RAW":
            case "MODE1/2352":
                track.TrackType = CdTrackType.Mode1Raw;
                track.DataSize = 2352;
                break;
            case "MODE2":
            case "MODE2/2336":
                track.TrackType = CdTrackType.Mode2;
                track.DataSize = 2336;
                break;
            case "MODE2_FORM1":
            case "MODE2/2048":
                track.TrackType = CdTrackType.Mode2Form1;
                track.DataSize = 2048;
                break;
            case "MODE2_FORM2":
            case "MODE2/2324":
                track.TrackType = CdTrackType.Mode2Form2;
                track.DataSize = 2324;
                break;
            case "MODE2_FORM_MIX":
                track.TrackType = CdTrackType.Mode2FormMix;
                track.DataSize = 2336;
                break;
            case "MODE2_RAW":
            case "MODE2/2352":
            case "CDI/2352":
                track.TrackType = CdTrackType.Mode2Raw;
                track.DataSize = 2352;
                break;
            case "AUDIO":
                track.TrackType = CdTrackType.Audio;
                track.DataSize = 2352;
                break;
            default:
                throw new InvalidDataException($"Unknown track type [{typeString}]");
        }
    }

    internal static void ParseSubType(string subTypeString, ref CdTrack track)
    {
        switch (subTypeString)
        {
            case "RW":
                track.SubType = CdSubType.Normal;
                track.SubSize = CdConstants.MaxSubcodeData;
                break;
            case "RW_RAW":
                track.SubType = CdSubType.Raw;
                track.SubSize = CdConstants.MaxSubcodeData;
                break;
            default:
                track.SubType = CdSubType.None;
                track.SubSize = 0;
                break;
        }
    }

    /// <summary>
    /// Validates a .WAV file (PCM, stereo, 44100 Hz, 16-bit) and returns the audio
    /// data length in bytes and its offset within the file. Matches MAME's
    /// <c>parse_wav_sample</c>.
    /// </summary>
    private static (long Length, long Offset) ParseWavSample(string fileName)
    {
        using var fs = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
        var fileSize = fs.Length;
        long offset = 0;

        if (!string.Equals(ReadFourCc(fs, offset), "RIFF", StringComparison.Ordinal))
            throw new InvalidDataException($"Could not find RIFF header ({fileName})");

        offset += 4;
        ReadU32Le(fs, ref offset);
        if (!string.Equals(ReadFourCc(fs, offset), "WAVE", StringComparison.Ordinal))
            throw new InvalidDataException($"Could not find WAVE header ({fileName})");

        offset += 4;

        // seek until we find a format tag
        long length;
        while (true)
        {
            var tag = ReadFourCc(fs, offset);
            offset += 4;
            length = ReadU32Le(fs, ref offset);
            if (string.Equals(tag, "fmt ", StringComparison.Ordinal))
                break;

            offset += length;
            if (offset >= fileSize)
                throw new InvalidDataException($"Could not find fmt tag ({fileName})");
        }

        // format must be PCM
        if (ReadU16Le(fs, ref offset) != 1)
            throw new InvalidDataException($"Unsupported WAV format - only PCM is supported ({fileName})");
        // only stereo is supported
        if (ReadU16Le(fs, ref offset) != 2)
            throw new InvalidDataException($"Unsupported number of channels - only stereo is supported ({fileName})");
        // sample rate
        if (ReadU32Le(fs, ref offset) != 44100)
            throw new InvalidDataException($"Unsupported samplerate - only 44100 is supported ({fileName})");
        // bytes/second and block alignment are ignored
        offset += 6;
        // bits/sample
        if (ReadU16Le(fs, ref offset) != 16)
            throw new InvalidDataException($"Unsupported bits/sample - only 16 is supported ({fileName})");
        // seek past any extra data
        offset += length - 16;

        // seek until we find a data tag
        while (true)
        {
            var tag = ReadFourCc(fs, offset);
            offset += 4;
            length = ReadU32Le(fs, ref offset);
            if (string.Equals(tag, "data", StringComparison.Ordinal))
                break;

            offset += length;
            if (offset >= fileSize)
                throw new InvalidDataException($"Could not find data tag ({fileName})");
        }

        return (length, offset);
    }

    private static string ReadFourCc(Stream stream, long position)
    {
        stream.Position = position;
        var buffer = new byte[4];
        if (stream.Read(buffer, 0, 4) != 4)
            throw new InvalidDataException("Unexpected end of WAV file");

        return Encoding.ASCII.GetString(buffer);
    }

    private static uint ReadU32Le(Stream stream, ref long offset)
    {
        stream.Position = offset;
        var buffer = new byte[4];
        if (stream.Read(buffer, 0, 4) != 4)
            throw new InvalidDataException("Unexpected end of WAV file");

        offset += 4;
        return buffer[0] | ((uint)buffer[1] << 8) | ((uint)buffer[2] << 16) | ((uint)buffer[3] << 24);
    }

    private static ushort ReadU16Le(Stream stream, ref long offset)
    {
        stream.Position = offset;
        var buffer = new byte[2];
        if (stream.Read(buffer, 0, 2) != 2)
            throw new InvalidDataException("Unexpected end of WAV file");

        offset += 2;
        return (ushort)(buffer[0] | (buffer[1] << 8));
    }

    /// <summary>
    /// Splits a CUE line into tokens, honoring single and double quotes
    /// (matching MAME's <c>tokenize</c> helper).
    /// </summary>
    private static List<string> Tokenize(string line)
    {
        return CdImageParser.Tokenize(line);
    }
}
