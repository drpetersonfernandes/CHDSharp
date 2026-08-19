using System.Buffers.Binary;
using CHDSharpEncoder.Models;

namespace CHDSharpEncoder;

/// <summary>
/// Parses Nero .NRG disc images (DAO/CUE layout) into a <see cref="CdToc"/> (CHDlite
/// <c>cdrom_file::parse_nero</c> parity, <c>cdrom.cpp:1839-2004</c>). The image is a chain of
/// big-endian chunks; the DAOX chunk holds the track table. Supported sector modes: 2048-byte
/// Mode 1, 2352-byte Mode 2 raw, and 2352-byte audio (byte-swapped for CHD storage). Pregaps
/// whose sectors are not physically stored are zero-filled when encoded.
/// </summary>
public sealed class NrgParser
{
    /// <summary>Parses a Nero NRG image file.</summary>
    /// <param name="nrgPath">Path to the .nrg file (track data is read from this same file).</param>
    /// <returns>The parsed table of contents.</returns>
    /// <exception cref="FileNotFoundException">The NRG file does not exist.</exception>
    /// <exception cref="InvalidDataException">The image is not a Nero 5.x image, its chain is
    /// malformed, or it uses an unsupported track mode.</exception>
    public CdToc Parse(string nrgPath)
    {
        ArgumentNullException.ThrowIfNull(nrgPath);
        if (!File.Exists(nrgPath))
            throw new FileNotFoundException($"Couldn't find NRG file [{nrgPath}]", nrgPath);

        using var file = new FileStream(nrgPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length < 12)
            throw new InvalidDataException("File is too small to be a Nero NRG image");

        // The last 12 bytes hold the "NER5" magic and the offset of the first chunk.
        Span<byte> tail = stackalloc byte[12];
        file.Position = file.Length - 12;
        file.ReadExactly(tail);
        if (!tail[..4].SequenceEqual("NER5"u8))
            throw new InvalidDataException("Not a Nero 5.5 or later image");

        if (tail[4] != 0 || tail[5] != 0 || tail[6] != 0 || tail[7] != 0)
            throw new InvalidDataException("NRG file size exceeds 4 GB, unsupported");

        uint chainOffset = BinaryPrimitives.ReadUInt32BigEndian(tail[8..]);

        var toc = new CdToc();
        List<CdTrack>? tracks = null;
        var done = false;
        Span<byte> chunkHeader = stackalloc byte[8];
        Span<byte> buffer = stackalloc byte[30];

        while (!done)
        {
            if (chainOffset > file.Length - 8)
                throw new InvalidDataException("Corrupt NRG chunk chain");

            file.Position = chainOffset;
            file.ReadExactly(chunkHeader);
            uint chunkSize = BinaryPrimitives.ReadUInt32BigEndian(chunkHeader[4..]);

            if (chunkHeader[..4].SequenceEqual("DAOX"u8))
            {
                // Skip the second chunk size field and the UPC code (16 + 4 bytes).
                file.Position = chainOffset + 8 + 16 + 4;

                byte startTrack = (byte)file.ReadByte();
                byte endTrack = (byte)file.ReadByte();
                int numTracks = endTrack - startTrack + 1;
                if (numTracks is <= 0 or > CdConstants.MaxTracks)
                    throw new InvalidDataException($"Invalid NRG track range {startTrack}-{endTrack}");

                tracks = new List<CdTrack>(numTracks);
                ulong offset = 0;
                for (int track = startTrack; track <= endTrack; track++)
                {
                    // Skip the 12-byte ISRC code, then read the 30-byte track descriptor:
                    // sector size (2), mode (2), unused (2), index0 (8), index1 (8), track_end (8).
                    file.Position += 12;
                    file.ReadExactly(buffer[..30]);
                    uint size = BinaryPrimitives.ReadUInt16BigEndian(buffer[..]);
                    ushort mode = BinaryPrimitives.ReadUInt16BigEndian(buffer[2..]);
                    ulong index0 = BinaryPrimitives.ReadUInt64BigEndian(buffer[6..14]);
                    ulong index1 = BinaryPrimitives.ReadUInt64BigEndian(buffer[14..22]);
                    ulong trackEnd = BinaryPrimitives.ReadUInt64BigEndian(buffer[22..]);

                    if (size == 0)
                        throw new InvalidDataException($"NRG track {track} has a zero sector size");

                    var trackInfo = new CdTrack
                    {
                        Number = track,
                        FileName = nrgPath,
                        SubType = CdSubType.None,
                        SubSize = 0,
                        PgSub = CdSubType.None,
                        PgType = CdTrackType.Mode1,
                        PgDataSize = 0,
                        Postgap = 0,
                        PadFrames = 0,
                        Index00 = -1,
                        Index01 = -1,
                        // INDEX 01 starts after the (possibly unstored) pregap bytes.
                        FileOffset = (long)(offset + (index1 - index0)),
                        // MAME reports the pregap (INDEX 00 → INDEX 01) without physical data.
                        Pregap = (int)((index1 - index0) / size),
                        Frames = (int)((index1 - index0) / size + (trackEnd - index1) / size)
                    };

                    switch (mode)
                    {
                        case 0x0000: // 2048-byte data
                            trackInfo.TrackType = CdTrackType.Mode1;
                            trackInfo.DataSize = 2048;
                            trackInfo.Swap = false;
                            break;
                        case 0x0600: // 2352-byte Mode 2 raw
                            trackInfo.TrackType = CdTrackType.Mode2Raw;
                            trackInfo.DataSize = 2352;
                            trackInfo.Swap = false;
                            break;
                        case 0x0700: // 2352-byte audio
                            trackInfo.TrackType = CdTrackType.Audio;
                            trackInfo.DataSize = 2352;
                            trackInfo.Swap = true;
                            break;
                        default:
                            throw new InvalidDataException(
                                $"Unsupported NRG track mode 0x{mode:X4} in track {track}");
                    }

                    tracks.Add(trackInfo);
                    offset += trackEnd - index1;
                }
            }

            if (chunkHeader[..4].SequenceEqual("END!"u8))
            {
                done = true;
            }
            else
            {
                chainOffset += chunkSize + 8;
            }
        }

        if (tracks is not { Count: > 0 })
            throw new InvalidDataException("NRG image contains no DAOX track table");

        toc.Tracks.AddRange(tracks);
        return toc;
    }
}