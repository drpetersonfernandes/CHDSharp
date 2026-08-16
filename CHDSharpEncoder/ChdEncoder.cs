namespace CHDSharpEncoder;

internal class ChdEncoder
{
    internal static void EncodeRaw(Stream sourceStream, string chdPath, uint hunkBytes = 4096, uint unitBytes = 512)
    {
        ArgumentNullException.ThrowIfNull(sourceStream);
        if (hunkBytes == 0 || unitBytes == 0 || hunkBytes % unitBytes != 0)
            throw new ArgumentException($"hunkBytes ({hunkBytes}) must be a multiple of unitBytes ({unitBytes})");

        var logicalBytes = (ulong)sourceStream.Length;
        var hunkCount = (uint)((logicalBytes + hunkBytes - 1) / hunkBytes);
        if (hunkCount == 0)
        {
            hunkCount = 1;
        }

        var entries = new MapEntry[hunkCount];
        var blockList = new List<byte[]>();
        var sha1 = new Sha1();
        long currentOffset = ChdHeaderV5.LENGTH;
        var processor = new HunkProcessor(hunkBytes);

        var readBuffer = new byte[hunkBytes];

        for (uint h = 0; h < hunkCount; h++)
        {
            Array.Clear(readBuffer, 0, (int)hunkBytes);

            var streamOffset = (long)h * hunkBytes;
            if (streamOffset < (long)logicalBytes)
            {
                sourceStream.Position = streamOffset;
                var bytesRead = sourceStream.Read(readBuffer, 0, (int)hunkBytes);
                // remaining bytes stay zero (default)
            }

            sha1.Append(readBuffer, 0, (int)hunkBytes);

            var (entry, data) = processor.ProcessHunk(readBuffer, currentOffset);
            entries[h] = entry;
            blockList.Add(data);
            currentOffset += data.Length;
        }

        var rawSha1 = sha1.Finish();

        var compressedMap = MapCompressor.Compress(entries, hunkCount, hunkBytes, unitBytes);
        var mapOffset = (ulong)currentOffset;

        // Write file
        using var fs = new FileStream(chdPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        var header = ChdHeaderV5.CreateRaw(CodecTags.ZLIB, logicalBytes, hunkBytes, unitBytes);
        header.WriteToStream(fs);

        foreach (var block in blockList)
            fs.Write(block, 0, block.Length);

        fs.Write(compressedMap, 0, compressedMap.Length);

        // Patch header: mapoffset at byte 40
        var patchW = new BigEndianWriter();
        patchW.WriteU64(mapOffset);
        fs.Position = 40;
        fs.Write(patchW.ToArray(), 0, 8);

        // Patch rawsha1 at byte 64
        fs.Position = 64;
        fs.Write(rawSha1, 0, 20);

        // Patch sha1 (combined raw+meta, with no metadata: SHA1(rawSha1))
        var combinedSha1 = Sha1.Compute(rawSha1);
        fs.Position = 84;
        fs.Write(combinedSha1, 0, 20);
    }

    internal static void EncodeRaw(string sourcePath, string chdPath, uint hunkBytes = 4096, uint unitBytes = 512)
    {
        using var fs = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        EncodeRaw(fs, chdPath, hunkBytes, unitBytes);
    }

    internal static void EncodeCd(string cuePath, string chdPath,
        uint hunkBytes = CdConstants.FramesPerHunk * CdConstants.FrameSize, uint unitBytes = CdConstants.FrameSize)
    {
        ArgumentNullException.ThrowIfNull(cuePath);
        if (unitBytes != CdConstants.FrameSize)
            throw new ArgumentException($"unitBytes ({unitBytes}) must be the CD frame size ({CdConstants.FrameSize})");
        if (hunkBytes == 0 || hunkBytes % unitBytes != 0)
            throw new ArgumentException($"hunkBytes ({hunkBytes}) must be a multiple of unitBytes ({unitBytes})");

        // 1. Parse the CUE sheet
        var toc = new CueParser().Parse(cuePath);
        if (toc.Tracks.Count == 0)
            throw new InvalidDataException("CUE file contains no tracks");

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

        // 3. Process hunks (track-aware reads from the BIN file(s))
        var entries = new MapEntry[hunkCount];
        var blockList = new List<byte[]>();
        var sha1 = new Sha1();
        long currentOffset = ChdHeaderV5.LENGTH;
        var processor = new HunkProcessor(hunkBytes);
        var readBuffer = new byte[hunkBytes];
        var sourceFiles = new Dictionary<string, FileStream>(StringComparer.OrdinalIgnoreCase);

        try
        {
            for (uint h = 0; h < hunkCount; h++)
            {
                Array.Clear(readBuffer, 0, (int)hunkBytes);

                long hunkStartFrame = (long)h * framesPerHunk;
                for (int f = 0; f < framesPerHunk; f++)
                {
                    long frame = hunkStartFrame + f;
                    if (frame >= (long)totalFrames)
                        break;

                    var track = FindTrackContainingFrame(toc, frame);
                    int frameInTrack = (int)(frame - track.LogicalFrameStart);

                    // frames past the track's data are padding and stay zero-filled
                    if (frameInTrack >= track.Frames)
                        continue;

                    // the BIN file stores datasize+subsize bytes per sector (no subcode → 2352);
                    // the remainder of the 2448-byte CHD frame stays zero-filled
                    int binFrameSize = track.DataSize + track.SubSize;
                    long sourceOffset = track.FileOffset + (long)frameInTrack * binFrameSize;
                    var file = GetSourceFile(sourceFiles, track.FileName!);
                    file.Position = sourceOffset;
                    var bytesRead = file.Read(readBuffer, f * CdConstants.FrameSize, binFrameSize);
                    if (bytesRead != binFrameSize)
                        throw new InvalidDataException($"Unexpected end of file [{track.FileName}]");

                    // audio sectors are little-endian in BIN files; swap to big-endian for CHD
                    if (track.Swap)
                        SwapPairs(readBuffer, f * CdConstants.FrameSize, track.DataSize);
                }

                sha1.Append(readBuffer, 0, (int)hunkBytes);

                var (entry, data) = processor.ProcessHunk(readBuffer, currentOffset);
                entries[h] = entry;
                blockList.Add(data);
                currentOffset += data.Length;
            }
        }
        finally
        {
            foreach (var file in sourceFiles.Values)
                file.Dispose();
        }

        var rawSha1 = sha1.Finish();

        // 4. Build metadata entries and compressed map
        var metadataEntries = MetadataWriter.BuildCdMetadataEntries(toc);
        var compressedMap = MapCompressor.Compress(entries, hunkCount, hunkBytes, unitBytes);

        // 5. Write output file
        using var fs = new FileStream(chdPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        var header = ChdHeaderV5.CreateRaw(CodecTags.ZLIB, logicalBytes, hunkBytes, unitBytes);
        header.WriteToStream(fs);

        foreach (var block in blockList)
            fs.Write(block, 0, block.Length);

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
}
