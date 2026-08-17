using CHDSharp;

namespace CHDSharpEncoder;

/// <summary>
/// Creates CHD v5 files from raw binary data (<see cref="EncodeRaw"/>) or from CD
/// CUE/BIN sources (<see cref="EncodeCd"/>). Uses the zlib codec by default, matching chdman's
/// <c>--compression zlib</c> output; produced files pass <c>chdman verify</c> and
/// extract byte-identically via <c>chdman extractraw</c>.
/// </summary>
/// <remarks>
/// Encoding runs a producer→worker→consumer pipeline (<see cref="HunkProcessor.CompressAll"/>):
/// hunks are read and hashed on one thread, compressed in parallel by <c>TaskCount</c> workers
/// (each with private, persistent codec instances), and written back strictly in hunk order by a
/// single consumer. The output is byte-identical to a single-threaded encode regardless of the
/// worker count, because codec outputs are deterministic and dedup/offset assignment stays
/// sequential.
/// </remarks>
public static class ChdEncoder
{
    private const uint DefaultHunkBytes = 4096;
    private const uint DefaultUnitBytes = 512;
    private const uint DvdSectorSize = 2048;
    private const ulong Iso9660PvdOffset = 16 * DvdSectorSize;

    /// <summary>
    /// Encodes a raw binary stream into a compressed CHD v5 file. The last hunk is
    /// zero-padded in the file when the source size is not a multiple of
    /// <paramref name="hunkBytes"/>; the stored raw SHA-1 covers only the actual source
    /// bytes, so <c>chdman verify</c> succeeds for any input size.
    /// </summary>
    /// <param name="sourceStream">The raw source data; the full stream is consumed from its start.</param>
    /// <param name="chdPath">Path of the output .chd file (created/overwritten).</param>
    /// <param name="hunkBytes">Hunk size in bytes (default 4096).</param>
    /// <param name="unitBytes">Unit size in bytes (default 512; 2048 when
    /// <see cref="ChdEncodeOptions.AutoClassify"/> detects an ISO-9660 DVD image).</param>
    /// <param name="codecTags">The codec tags to use, tried per hunk in order (default zlib).</param>
    /// <param name="options">Optional encoding configuration (see <see cref="ChdEncodeOptions"/>).</param>
    /// <param name="cancellationToken">Cancels the encode; <see cref="OperationCanceledException"/>
    /// is thrown when cancellation is requested.</param>
    /// <exception cref="ArgumentException"><paramref name="hunkBytes"/> is not a multiple of <paramref name="unitBytes"/>.</exception>
    public static void EncodeRaw(Stream sourceStream, string chdPath, uint hunkBytes = DefaultHunkBytes, uint unitBytes = DefaultUnitBytes,
        IReadOnlyList<uint>? codecTags = null, ChdEncodeOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceStream);
        if (hunkBytes == 0 || unitBytes == 0 || hunkBytes % unitBytes != 0)
            throw new ArgumentException($"hunkBytes ({hunkBytes}) must be a multiple of unitBytes ({unitBytes})");

        codecTags ??= [CodecTags.ZLIB];
        var codecs = ChdCodecs.CreateAll(codecTags, hunkBytes);

        var logicalBytes = (ulong)sourceStream.Length;

        // User-supplied metadata entries plus optional automatic classification
        // ('DVD ' for ISO-9660 images, synthesized 'GDDD' hard-disk geometry otherwise).
        var metadataEntries = new List<MetadataEntry>();
        if (options?.Metadata is { Count: > 0 } userMetadata)
            metadataEntries.AddRange(userMetadata);

        if (options?.AutoClassify == true)
        {
            if (IsIso9660Image(sourceStream, logicalBytes))
            {
                metadataEntries.Add(MetadataWriter.BuildDvdMetadata());
                if (unitBytes == DefaultUnitBytes && hunkBytes % DvdSectorSize == 0)
                    unitBytes = DvdSectorSize;
            }
            else
            {
                metadataEntries.Add(MetadataWriter.BuildHardDiskMetadata(logicalBytes, unitBytes));
            }
        }

        var hunkCount = (uint)((logicalBytes + hunkBytes - 1) / hunkBytes);
        if (hunkCount == 0)
        {
            hunkCount = 1;
        }

        var entries = new MapEntry[hunkCount];
        using var sha1 = new Sha1();
        var selfMap = new Dictionary<string, uint>((int)hunkCount);
        var processor = new HunkProcessor(hunkBytes, codecTags, options?.TaskCount ?? Chd.TaskCount);

        using var fs = new FileStream(chdPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        var header = ChdHeaderV5.CreateRaw(codecTags.ToArray(), logicalBytes, hunkBytes, unitBytes);
        header.WriteToStream(fs);

        // the compressed blocks are appended to the file in hunk order by the pipeline's
        // single consumer; offsets and the dedup map advance in the same order, so the
        // output is byte-identical to the sequential path
        long currentOffset = ChdHeaderV5.LENGTH;
        processor.CompressAll(
            hunkCount,
            (hunkIndex, buffer) => ReadRawHunk(sourceStream, hunkIndex, buffer, logicalBytes, hunkBytes),
            sha1,
            result => ConsumeHunk(result, entries, selfMap, fs, ref currentOffset, codecs, options, hunkCount, hunkBytes),
            cancellationToken);

        var rawSha1 = sha1.Finish();

        var compressedMap = MapCompressor.Compress(entries, hunkCount, hunkBytes, unitBytes);
        var mapOffset = (ulong)currentOffset;

        // Metadata lives between the compressed blocks and the map; the header's metaoffset
        // field is patched below (0 when no metadata is present, as chdman leaves it).
        long? metaOffset = null;
        if (metadataEntries.Count > 0)
        {
            metaOffset = MetadataWriter.WriteCdMetadata(fs, metadataEntries);
            mapOffset = (ulong)fs.Position;
        }

        fs.Write(compressedMap, 0, compressedMap.Length);

        // Patch header: mapoffset at byte 40, metaoffset at byte 48
        var patchW = new BigEndianWriter();
        patchW.WriteU64(mapOffset);
        fs.Position = 40;
        fs.Write(patchW.ToArray(), 0, 8);

        if (metaOffset.HasValue)
        {
            patchW = new BigEndianWriter();
            patchW.WriteU64((ulong)metaOffset.Value);
            fs.Position = 48;
            fs.Write(patchW.ToArray(), 0, 8);
        }

        // Patch rawsha1 at byte 64
        fs.Position = 64;
        fs.Write(rawSha1, 0, 20);

        // Patch sha1 (combined raw+meta; with no metadata: SHA1(rawSha1))
        var combinedSha1 = metadataEntries.Count > 0
            ? MetadataWriter.ComputeCombinedSha1(rawSha1, metadataEntries)
            : Sha1.Compute(rawSha1);
        fs.Position = 84;
        fs.Write(combinedSha1, 0, 20);
    }

    /// <summary>
    /// Detects an ISO-9660 filesystem image: the primary volume descriptor at sector 16
    /// (byte offset 0x8000) starts with the "CD001" magic. Restores the stream position.
    /// </summary>
    private static bool IsIso9660Image(Stream sourceStream, ulong length)
    {
        if (length < Iso9660PvdOffset + 5)
            return false;

        var original = sourceStream.Position;
        try
        {
            sourceStream.Position = (long)Iso9660PvdOffset;
            Span<byte> magic = stackalloc byte[5];
            if (sourceStream.Read(magic) != 5)
                return false;
            return magic.SequenceEqual("CD001"u8);
        }
        finally
        {
            sourceStream.Position = original;
        }
    }

    /// <summary>
    /// Encodes a raw binary file into a compressed CHD v5 file.
    /// </summary>
    /// <param name="sourcePath">Path of the raw input file.</param>
    /// <param name="chdPath">Path of the output .chd file (created/overwritten).</param>
    /// <param name="hunkBytes">Hunk size in bytes (default 4096).</param>
    /// <param name="unitBytes">Unit size in bytes (default 512).</param>
    /// <param name="codecTags">The codec tags to use, tried per hunk in order (default zlib).</param>
    /// <param name="options">Optional encoding configuration (see <see cref="ChdEncodeOptions"/>).</param>
    /// <param name="cancellationToken">Cancels the encode; <see cref="OperationCanceledException"/>
    /// is thrown when cancellation is requested.</param>
    /// <exception cref="ArgumentException"><paramref name="hunkBytes"/> is not a multiple of <paramref name="unitBytes"/>.</exception>
    public static void EncodeRaw(string sourcePath, string chdPath, uint hunkBytes = DefaultHunkBytes, uint unitBytes = DefaultUnitBytes,
        IReadOnlyList<uint>? codecTags = null, ChdEncodeOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var fs = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        EncodeRaw(fs, chdPath, hunkBytes, unitBytes, codecTags, options, cancellationToken);
    }

    /// <summary>
    /// Encodes a CD image from a CUE sheet into a compressed CHD v5 file. Tracks are
    /// padded to 4-frame boundaries, audio sectors are byte-swapped to big-endian (as on
    /// the physical disc), and one CHT2 metadata entry is written per track.
    /// </summary>
    /// <param name="cuePath">Path of the .cue file; referenced BIN/WAV files are resolved relative to it.</param>
    /// <param name="chdPath">Path of the output .chd file (created/overwritten).</param>
    /// <param name="hunkBytes">Hunk size in bytes (default 19584 = 8 CD frames).</param>
    /// <param name="unitBytes">Unit size in bytes (default 2448 = CD frame with subcode).</param>
    /// <param name="codecTags">The codec tags to use, tried per hunk in order (default zlib).</param>
    /// <param name="options">Optional encoding configuration (see <see cref="ChdEncodeOptions"/>).</param>
    /// <param name="cancellationToken">Cancels the encode; <see cref="OperationCanceledException"/>
    /// is thrown when cancellation is requested.</param>
    /// <exception cref="ArgumentException"><paramref name="unitBytes"/> is not the CD frame size, or
    /// <paramref name="hunkBytes"/> is not a multiple of it.</exception>
    /// <exception cref="FileNotFoundException">The CUE file or a referenced data file does not exist.</exception>
    /// <exception cref="InvalidDataException">The CUE sheet is malformed or contains no tracks.</exception>
    public static void EncodeCd(string cuePath, string chdPath,
        uint hunkBytes = CdConstants.FramesPerHunk * CdConstants.FrameSize, uint unitBytes = CdConstants.FrameSize,
        IReadOnlyList<uint>? codecTags = null, ChdEncodeOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cuePath);
        if (unitBytes != CdConstants.FrameSize)
            throw new ArgumentException($"unitBytes ({unitBytes}) must be the CD frame size ({CdConstants.FrameSize})");
        if (hunkBytes == 0 || hunkBytes % unitBytes != 0)
            throw new ArgumentException($"hunkBytes ({hunkBytes}) must be a multiple of unitBytes ({unitBytes})");

        codecTags ??= [CodecTags.ZLIB];
        var codecs = ChdCodecs.CreateAll(codecTags, hunkBytes);

        // 1. Parse the image descriptor (CUE, GDI, ISO or TOC)
        var toc = CdImageParser.Parse(cuePath);
        if (toc.Tracks.Count == 0)
            throw new InvalidDataException($"{Path.GetExtension(cuePath)} file contains no tracks");

        // 2. Pad each track to a 4-frame boundary and assign logical frame positions
        ulong totalFrames = 0;
        for (int i = 0; i < toc.Tracks.Count; i++)
        {
            var track = toc.Tracks[i];
            int extraFrames = (CdConstants.TrackPadding - track.Frames % CdConstants.TrackPadding) % CdConstants.TrackPadding;
            track.PaddedFrames = track.Frames + extraFrames;
            track.LogicalFrameStart = (long)totalFrames;
            totalFrames += (ulong)track.PaddedFrames;
            toc.Tracks[i] = track;
        }

        ulong logicalBytes = totalFrames * CdConstants.FrameSize;
        uint hunkCount = (uint)((logicalBytes + hunkBytes - 1) / hunkBytes);
        int framesPerHunk = (int)(hunkBytes / CdConstants.FrameSize);

        // 3. Parallel pipeline: the producer performs track-aware reads from the BIN file(s)
        // (only the producer thread touches the source files), workers compress, and the
        // single consumer writes blocks and map entries in hunk order
        var entries = new MapEntry[hunkCount];
        using var sha1 = new Sha1();
        var selfMap = new Dictionary<string, uint>((int)hunkCount);
        var processor = new HunkProcessor(hunkBytes, codecTags, options?.TaskCount ?? Chd.TaskCount);
        var sourceFiles = new Dictionary<string, FileStream>(StringComparer.OrdinalIgnoreCase);

        using var fs = new FileStream(chdPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        var header = ChdHeaderV5.CreateRaw(codecTags.ToArray(), logicalBytes, hunkBytes, unitBytes);
        header.WriteToStream(fs);

        long currentOffset = ChdHeaderV5.LENGTH;
        try
        {
            processor.CompressAll(
                hunkCount,
                (hunkIndex, buffer) => ReadCdHunk(hunkIndex, buffer, toc, framesPerHunk, totalFrames, sourceFiles),
                sha1,
                result => ConsumeHunk(result, entries, selfMap, fs, ref currentOffset, codecs, options, hunkCount, hunkBytes),
                cancellationToken);
        }
        finally
        {
            foreach (var file in sourceFiles.Values)
                file.Dispose();
        }

        var rawSha1 = sha1.Finish();

        // 4. Build metadata entries (track entries + any user-supplied entries) and compressed map
        var metadataEntries = MetadataWriter.BuildCdMetadataEntries(toc);
        if (options?.Metadata is { Count: > 0 } userMetadata)
            metadataEntries.AddRange(userMetadata);
        var compressedMap = MapCompressor.Compress(entries, hunkCount, hunkBytes, unitBytes);

        // 5. Write metadata + map (the compressed blocks were already appended by the pipeline)
        long metaOffset = MetadataWriter.WriteCdMetadata(fs, metadataEntries);
        ulong mapOffset = (ulong)fs.Position;
        fs.Write(compressedMap, 0, compressedMap.Length);

        // 6. Patch header: mapoffset at byte 40, metaoffset at byte 48, rawsha1 at byte 64,
        // sha1 (combined raw+metadata) at byte 84
        var patchW = new BigEndianWriter();
        patchW.WriteU64(mapOffset);
        fs.Position = 40;
        fs.Write(patchW.ToArray(), 0, 8);

        patchW = new BigEndianWriter();
        patchW.WriteU64((ulong)metaOffset);
        fs.Position = 48;
        fs.Write(patchW.ToArray(), 0, 8);

        fs.Position = 64;
        fs.Write(rawSha1, 0, 20);

        var combinedSha1 = MetadataWriter.ComputeCombinedSha1(rawSha1, metadataEntries);
        fs.Position = 84;
        fs.Write(combinedSha1, 0, 20);
    }

    /// <summary>Reads hunk <paramref name="hunkIndex"/> from a raw stream; returns the number of
    /// valid bytes (the tail of a partial final hunk stays zero-filled for the file, but is
    /// excluded from the raw SHA-1 — matching chdman's verify semantics).</summary>
    private static int ReadRawHunk(Stream source, uint hunkIndex, byte[] buffer, ulong logicalBytes, uint hunkBytes)
    {
        var streamOffset = (long)hunkIndex * hunkBytes;
        if (streamOffset >= (long)logicalBytes)
            return 0;

        source.Position = streamOffset;
        return source.Read(buffer, 0, (int)hunkBytes);
    }

    /// <summary>Reads hunk <paramref name="hunkIndex"/> of a CD image: track-aware reads from the
    /// BIN/WAV file(s), zero-filled padding frames, and little-endian→big-endian audio swapping.
    /// CD hunks are always fully hashed (including zero padding), like the sequential path.</summary>
    private static int ReadCdHunk(uint hunkIndex, byte[] buffer, CdToc toc, int framesPerHunk, ulong totalFrames,
        Dictionary<string, FileStream> files)
    {
        long hunkStartFrame = (long)hunkIndex * framesPerHunk;
        for (int f = 0; f < framesPerHunk; f++)
        {
            long frame = hunkStartFrame + f;
            if (frame >= (long)totalFrames)
                break;

            var track = FindTrackContainingFrame(toc, frame);
            int frameInTrack = (int)(frame - track.LogicalFrameStart);

            // frames past the track's data and GDI gap (pad) frames are zero-filled
            if (frameInTrack >= track.Frames)
                continue;
            if (track.PadFrames > 0 && frameInTrack >= track.Frames - track.PadFrames)
                continue;

            // the BIN file stores datasize+subsize bytes per sector (no subcode → 2352);
            // the remainder of the 2448-byte CHD frame stays zero-filled
            int binFrameSize = track.DataSize + track.SubSize;
            long sourceOffset = track.FileOffset + (long)frameInTrack * binFrameSize;
            var file = GetSourceFile(files, track.FileName!);
            file.Position = sourceOffset;
            var bytesRead = file.Read(buffer, f * CdConstants.FrameSize, binFrameSize);
            if (bytesRead != binFrameSize)
                throw new InvalidDataException($"Unexpected end of file [{track.FileName}]");

            // audio sectors are little-endian in BIN files; swap to big-endian for CHD
            if (track.Swap)
                SwapPairs(buffer, f * CdConstants.FrameSize, track.DataSize);
        }

        return buffer.Length;
    }

    /// <summary>
    /// Single-consumer hunk sink, invoked by the pipeline in hunk order: performs SELF-dedup
    /// (the map is only ever updated with already-consumed hunks, so references never chain),
    /// assigns the sequential file offset, appends the block to the output, and reports progress.
    /// </summary>
    private static void ConsumeHunk(HunkResult result, MapEntry[] entries, Dictionary<string, uint> selfMap,
        Stream output, ref long currentOffset, IReadOnlyList<IChdCodec> codecs, ChdEncodeOptions? options,
        uint hunkCount, uint hunkBytes)
    {
        var sha1Hex = Convert.ToHexString(result.Sha1);
        MapEntry entry;
        byte[]? data = result.Data;
        if (selfMap.TryGetValue(sha1Hex, out var sourceHunk))
        {
            entry = new MapEntry
            {
                Compression = MapEntry.COMPRESSION_SELF,
                CompLength = 0,
                Offset = sourceHunk,
                Crc16 = 0,
            };
            data = null;
        }
        else
        {
            entry = new MapEntry
            {
                Compression = result.Compression,
                CompLength = result.CompLength,
                Offset = (ulong)currentOffset,
                Crc16 = result.Crc16,
            };
            selfMap[sha1Hex] = result.HunkIndex;
        }

        entries[result.HunkIndex] = entry;
        if (data != null)
        {
            output.Write(data, 0, (int)result.CompLength);
            currentOffset += result.CompLength;
        }

        ReportHunkProgress(options, codecs, entry, result.HunkIndex, hunkCount, hunkBytes);
    }

    private static CdTrack FindTrackContainingFrame(CdToc toc, long frame)
    {
        foreach (var track in toc.Tracks)
        {
            if (frame >= track.LogicalFrameStart && frame < track.LogicalFrameStart + track.PaddedFrames)
                return track;
        }
        throw new InvalidDataException($"Frame {frame} falls outside all tracks");
    }

    private static FileStream GetSourceFile(Dictionary<string, FileStream> files, string fileName)
    {
        if (files.TryGetValue(fileName, out var existing))
            return existing;

        var file = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
        files.Add(fileName, file);
        return file;
    }

    private static void SwapPairs(byte[] buffer, int offset, int length)
    {
        for (int i = 0; i < length; i += 2)
        {
            (buffer[offset + i], buffer[offset + i + 1]) = (buffer[offset + i + 1], buffer[offset + i]);
        }
    }

    /// <summary>Raises <see cref="ChdEncodeOptions.HunkCompleted"/> for one hunk (no-op when unset).</summary>
    private static void ReportHunkProgress(ChdEncodeOptions? options, IReadOnlyList<IChdCodec> codecs,
        MapEntry entry, uint hunkIndex, uint hunkCount, uint hunkBytes)
    {
        if (options?.HunkCompleted is not { } callback)
            return;

        int storedBytes;
        string codecName;
        switch (entry.Compression)
        {
            case MapEntry.COMPRESSION_NONE:
                storedBytes = (int)hunkBytes;
                codecName = "none";
                break;
            case MapEntry.COMPRESSION_SELF:
                storedBytes = 0;
                codecName = "self";
                break;
            default:
                storedBytes = (int)entry.CompLength;
                codecName = entry.Compression < codecs.Count
                    ? CodecTags.ToString(codecs[(int)entry.Compression].Tag)
                    : "?";
                break;
        }

        callback(new HunkProgress(hunkIndex, hunkCount, (int)hunkBytes, storedBytes, entry.Compression,
            codecName, storedBytes / (double)hunkBytes));
    }
}