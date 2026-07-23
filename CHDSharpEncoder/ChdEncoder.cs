namespace CHDSharpEncoder;

public class ChdEncoder
{
    public void EncodeRaw(Stream sourceStream, string chdPath, uint hunkBytes = 4096, uint unitBytes = 512)
    {
        if (sourceStream == null)
            throw new ArgumentNullException(nameof(sourceStream));
        if (hunkBytes == 0 || unitBytes == 0 || hunkBytes % unitBytes != 0)
            throw new ArgumentException($"hunkBytes ({hunkBytes}) must be a multiple of unitBytes ({unitBytes})");

        ulong logicalBytes = (ulong)sourceStream.Length;
        uint hunkCount = (uint)((logicalBytes + hunkBytes - 1) / hunkBytes);
        if (hunkCount == 0) hunkCount = 1;

        var entries = new MapEntry[hunkCount];
        var blockList = new List<byte[]>();
        var sha1 = new Sha1();
        long currentOffset = ChdHeaderV5.LENGTH;
        var processor = new HunkProcessor(hunkBytes);

        byte[] readBuffer = new byte[hunkBytes];

        for (uint h = 0; h < hunkCount; h++)
        {
            Array.Clear(readBuffer, 0, (int)hunkBytes);

            long streamOffset = (long)h * hunkBytes;
            if (streamOffset < (long)logicalBytes)
            {
                sourceStream.Position = streamOffset;
                int bytesRead = sourceStream.Read(readBuffer, 0, (int)hunkBytes);
                // remaining bytes stay zero (default)
            }

            sha1.Append(readBuffer, 0, (int)hunkBytes);

            var (entry, data) = processor.ProcessHunk(readBuffer, currentOffset);
            entries[h] = entry;
            blockList.Add(data);
            currentOffset += data.Length;
        }

        byte[] rawSha1 = sha1.Finish();

        byte[] compressedMap = MapCompressor.Compress(entries, hunkCount, hunkBytes, unitBytes);
        ulong mapOffset = (ulong)currentOffset;

        // Write file
        using var fs = new FileStream(chdPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        var header = ChdHeaderV5.CreateRaw(CodecTags.ZLIB, logicalBytes, hunkBytes, unitBytes);
        header.WriteToStream(fs);

        foreach (byte[] block in blockList)
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
        byte[] combinedSha1 = Sha1.Compute(rawSha1);
        fs.Position = 84;
        fs.Write(combinedSha1, 0, 20);
    }

    public void EncodeRaw(string sourcePath, string chdPath, uint hunkBytes = 4096, uint unitBytes = 512)
    {
        using var fs = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        EncodeRaw(fs, chdPath, hunkBytes, unitBytes);
    }
}
