using CHDSharp;
using CHDSharp.Models;
using CHDSharpEncoder.Interfaces;
using CHDSharpEncoder.Models;
using MapEntry = CHDSharpEncoder.Models.MapEntry;

namespace CHDSharpEncoder;

/// <summary>
/// Creates CHD v5 files from raw binary data (<see cref="EncodeRaw(Stream, string, uint, uint, IReadOnlyList{uint}?, ChdEncodeOptions?, System.Threading.CancellationToken)"/>), from CD
/// CUE/BIN sources (<see cref="EncodeCd"/>), or by re-compressing an existing CHD
/// (<see cref="Copy"/>). Uses the zlib codec by default, matching chdman's
/// <c>--compression zlib</c> output; produced files pass <c>chdman verify</c> and
/// extract byte-identically via <c>chdman extractraw</c>.
/// </summary>
/// <remarks>
/// Encoding runs a producer→worker→consumer pipeline (<see cref="HunkProcessor.CompressAll"/>):
/// hunks are read and hashed on one thread, compressed in parallel by <c>TaskCount</c> workers
/// (each with private, persistent codec instances), and written back strictly in hunk order by a
/// single consumer. The output is byte-identical to a single-threaded encode regardless of the
/// worker count, because codec outputs are deterministic and dedup/offset assignment stays
/// sequential. <c>-c none</c> (uncompressed CHD) uses a dedicated sequential path that writes the
/// V5 raw map (4-byte hunk-index entries, chdman-parity layout).
/// </remarks>
/// <summary>
/// Summary of a completed <see cref="ChdEncoder.EncodeLaserDisc"/> run: the derived A/V
/// parameters and output geometry (mirrors chdman <c>createld</c>'s console report).
/// </summary>
public sealed record LaserDiscEncodingInfo(
    ulong FpsTimes1million,
    uint Width,
    uint Height,
    bool Interlaced,
    uint Channels,
    uint SampleRate,
    uint MaxSamplesPerFrame,
    uint BytesPerFrame,
    uint HunkBytes,
    ulong FirstFrame,
    ulong Frames);

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
    /// <param name="codecTags">The codec tags to use, tried per hunk in order (default zlib;
    /// the single tag <see cref="CodecTags.None"/> produces an uncompressed CHD).</param>
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

        codecTags ??= [CodecTags.Zlib];

        // input_start/input_length (CHDlite input_start_byte/input_bytes parity): the image
        // covers [InputStartBytes, InputStartBytes + InputLengthBytes) of the source.
        var startBytes = options?.InputStartBytes ?? 0;
        var logicalBytes = ComputeLogicalLength(sourceStream, startBytes, options?.InputLengthBytes);

        // User-supplied metadata entries plus optional automatic classification
        // ('DVD ' for ISO-9660 images, synthesized 'GDDD' hard-disk geometry otherwise).
        var metadataEntries = new List<MetadataEntry>();
        if (options?.Metadata is { Count: > 0 } userMetadata)
            metadataEntries.AddRange(userMetadata);

        if (options?.AutoClassify == true)
        {
            if (IsIso9660Image(sourceStream, startBytes, logicalBytes))
            {
                metadataEntries.Add(MetadataWriter.BuildDvdMetadata());
                if (unitBytes == DefaultUnitBytes && hunkBytes % DvdSectorSize == 0)
                {
                    unitBytes = DvdSectorSize;
                }
            }
            else
            {
                metadataEntries.Add(MetadataWriter.BuildHardDiskMetadata(logicalBytes, unitBytes));
            }
        }

        EncodeCore(chdPath, hunkBytes, unitBytes, codecTags, options, logicalBytes, metadataEntries,
            CreateRawStreamReader(sourceStream, startBytes, logicalBytes, hunkBytes, sourceStream.CanSeek),
            cancellationToken);
    }

    /// <summary>
    /// Encodes a laserdisc CHD from an AVI file (chdman <c>createld</c> parity). Each output
    /// hunk holds one or more whole video frames; every frame is assembled into MAME's raw
    /// 'chav' layout and compressed with the 'avhu' codec (delta-RLE Huffman video + per-channel
    /// mono 48 kHz FLAC audio). The file carries 'AVAV' A/V metadata, plus 'AVLD' VBI metadata
    /// (16 packed bytes per frame) when the field height is 262 or 312 — appended after the
    /// map exactly like chdman.
    /// </summary>
    /// <param name="aviPath">Path of the source AVI file (YUY2/VYUY/UYVY video + PCM audio).</param>
    /// <param name="chdPath">Path of the output .chd file (created/overwritten).</param>
    /// <param name="hunkBytes">Hunk size in bytes; must be a whole multiple of the frame size.
    /// Default 0 = one frame per hunk (chdman's default).</param>
    /// <param name="codecTags">The codec tags to use, tried per hunk in order (default
    /// <see cref="CodecTags.Avhu"/>; chdman rejects uncompressed for laserdiscs).</param>
    /// <param name="options">Optional encoding configuration (see <see cref="ChdEncodeOptions"/>).</param>
    /// <param name="inputStartFrame">First input frame to encode (chdman <c>-isf</c>, in whole
    /// frames of the source; doubled with the field range for interlaced sources).</param>
    /// <param name="inputLengthFrames">Number of input frames to encode (chdman <c>-if</c>);
    /// <c>null</c> encodes through the last frame.</param>
    /// <param name="cancellationToken">Cancels the encode; <see cref="OperationCanceledException"/>
    /// is thrown when cancellation is requested.</param>
    /// <exception cref="InvalidDataException">The AVI has no usable video stream, an unsupported
    /// format, or an out-of-range frame selection.</exception>
    /// <exception cref="NotSupportedException">The AVI video format is not YUY2/VYUY/UYVY.</exception>
    /// <exception cref="ArgumentException"><paramref name="hunkBytes"/> is not a multiple of the frame size.</exception>
    public static LaserDiscEncodingInfo EncodeLaserDisc(string aviPath, string chdPath,
        uint hunkBytes = 0, IReadOnlyList<uint>? codecTags = null, ChdEncodeOptions? options = null,
        long inputStartFrame = 0, long? inputLengthFrames = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aviPath);
        codecTags ??= [CodecTags.Avhu];
        if (codecTags is [CodecTags.None])
            throw new ArgumentException("Uncompressed is not supported for laserdisc CHDs", nameof(codecTags));

        using var avi = AviReader.Open(aviPath);
        var aviInfo = avi.Info;
        if (aviInfo.VideoSampletime == 0 || aviInfo.VideoTimescale == 0)
            throw new InvalidDataException("AVI file has no valid video timing");

        // determine parameters of the incoming video stream (do_create_ld)
        ulong fpsTimes1million = (ulong)aviInfo.VideoTimescale * 1000000 / aviInfo.VideoSampletime;
        uint width = (uint)aviInfo.Width;
        uint height = (uint)aviInfo.Height;
        bool interlaced = fpsTimes1million / 1000000 <= 30 && height % 2 == 0 && height > 288;

        // process input start/end in source frames, then adjust for interlacing
        ulong totalFrames = aviInfo.VideoNumsamples;
        if (totalFrames == 0)
            throw new InvalidDataException("AVI file contains no video frames");
        ulong start = (ulong)Math.Max(0, inputStartFrame);
        ulong end = inputLengthFrames is { } len ? start + (ulong)Math.Max(0, len) : totalFrames;
        if (start >= totalFrames)
            throw new InvalidDataException($"Input start frame ({start}) is beyond end of input ({totalFrames})");
        if (end > totalFrames)
            throw new InvalidDataException($"Input length is larger than available input from start offset ({totalFrames - start} frames)");

        if (interlaced)
        {
            fpsTimes1million *= 2;
            height /= 2;
            start *= 2;
            end *= 2;
        }

        uint channels = Math.Min(aviInfo.AudioChannels, 8u);
        uint rate = aviInfo.AudioSamplerate;

        // bytes per frame: worst-case raw 'chav' block (max samples via ceil-div)
        ulong maxSamplesPerFrameLong = rate > 0 ? ((ulong)rate * 1000000 + fpsTimes1million - 1) / fpsTimes1million : 0;
        if (maxSamplesPerFrameLong > ushort.MaxValue)
            throw new InvalidDataException($"Audio samples per frame ({maxSamplesPerFrameLong}) exceeds the AVHuff limit (65535)");
        uint maxSamplesPerFrame = (uint)maxSamplesPerFrameLong;
        if (width == 0 || height == 0 || width > ushort.MaxValue || height > ushort.MaxValue)
            throw new InvalidDataException($"Video geometry {width}x{height} is out of range");
        if (width % 2 != 0)
            throw new InvalidDataException($"Video width {width} must be even for YUY2 video");
        if (channels > 0 && maxSamplesPerFrame < 16)
            throw new InvalidDataException($"Audio samples per frame ({maxSamplesPerFrame}) is below the FLAC minimum (16)");
        uint bytesPerFrame = AvHuffEncoder.RawDataSize(width, height, channels, maxSamplesPerFrame);

        // process hunk size (parse_hunk_size granularity: whole multiples of the frame size)
        if (hunkBytes == 0)
            hunkBytes = bytesPerFrame;
        if (hunkBytes % bytesPerFrame != 0)
            throw new ArgumentException(
                $"Hunk size ({hunkBytes}) must be a whole multiple of the frame size ({bytesPerFrame})", nameof(hunkBytes));
        uint unitBytes = bytesPerFrame;

        ulong frames = end - start;
        ulong logicalBytes = frames * hunkBytes;
        uint hunkCount = (uint)(logicalBytes / hunkBytes);
        if (hunkCount == 0)
            hunkCount = 1;

        // laserdisc VBI metadata is captured only for NTSC/PAL field heights (524/2, 624/2)
        bool captureVbi = height == 524 / 2 || height == 624 / 2;
        var ldFrameData = captureVbi ? new byte[frames * VbiParse.PackedBytes] : null;

        // metadata written before compression ('AVAV', checksummed), like chdman createld
        var metadataEntries = new List<MetadataEntry>
        {
            MetadataWriter.BuildAvMetadata(fpsTimes1million, width, height, interlaced, channels, rate)
        };
        if (options?.Metadata is { Count: > 0 } userMetadata)
            metadataEntries.AddRange(userMetadata);

        int interlaceFactor = interlaced ? 2 : 1;
        uint frameStride = hunkBytes / bytesPerFrame;
        var fullFrame = new byte[(int)(width * height * interlaceFactor * 2)];
        var fieldFrame = new byte[(int)(width * height * 2)];
        var rawFrame = new byte[bytesPerFrame];
        var audioPlanes = new short[channels][];
        for (int ch = 0; ch < channels; ch++)
            audioPlanes[ch] = new short[maxSamplesPerFrame];

        // producer: assemble each hunk from whole frames of the AVI
        int ReadLdHunk(uint hunkIndex, byte[] buffer)
        {
            Array.Clear(buffer);
            for (uint slot = 0; slot < frameStride; slot++)
            {
                ulong frameNum = (ulong)hunkIndex * frameStride + slot;
                if (frameNum >= frames)
                    break;

                int frameSamples = AssembleAvFrame(avi, frameNum + start, interlaceFactor, width, height, channels,
                    rate, fpsTimes1million, maxSamplesPerFrame, fullFrame, fieldFrame, audioPlanes,
                    ldFrameData, frameNum);

                // assemble into an exact-size region (the frame's own sample count), then copy
                // into the zero-filled hunk slot — MAME's read_data pads the tail with zeroes
                int needed = 12 + (int)(channels * frameSamples * 2) + (int)(width * height * 2);
                var target = buffer.AsSpan((int)(slot * bytesPerFrame), (int)bytesPerFrame);
                AvHuffEncoder.AssembleData(rawFrame.AsSpan(0, needed), fieldFrame, (int)width, (int)height,
                    (int)channels, frameSamples, audioPlanes);
                rawFrame.AsSpan(0, needed).CopyTo(target);
            }

            return (int)Math.Min(hunkBytes, logicalBytes - (ulong)hunkIndex * hunkBytes);
        }

        var codecs = ChdCodecs.CreateAll(codecTags, hunkBytes);
        var entries = new MapEntry[hunkCount];
        using var sha1 = new Sha1();
        var selfMap = new Dictionary<string, uint>((int)hunkCount, StringComparer.Ordinal);
        using var parentMap = options?.ParentPath is { Length: > 0 } parentPath
            ? new ParentMap(parentPath, hunkBytes, unitBytes)
            : null;
        var processor = new HunkProcessor(hunkBytes, codecTags, options?.TaskCount ?? CHDSharp.Chd.TaskCount);

        using (var fs = new FileStream(chdPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            var header = ChdHeaderV5.CreateRaw(codecTags.ToArray(), logicalBytes, hunkBytes, unitBytes);
            if (parentMap != null)
                header.ParentSha1 = parentMap.ParentSha1;

            header.WriteToStream(fs);

            // 'AVAV' lives right after the header, before the compressed data (chdman appends
            // it before compressing any hunks); the header's metaoffset is patched below
            long metaOffset = MetadataWriter.WriteCdMetadata(fs, metadataEntries);

            long currentOffset = fs.Position;
            processor.CompressAll(
                hunkCount,
                ReadLdHunk,
                sha1,
                result => ConsumeHunk(result, entries, selfMap, fs, ref currentOffset, codecs, options, hunkCount, hunkBytes, parentMap),
                cancellationToken);

            var rawSha1 = sha1.Finish();
            var compressedMap = MapCompressor.Compress(entries, hunkCount, hunkBytes, unitBytes);
            var mapOffset = (ulong)currentOffset;
            fs.Write(compressedMap, 0, compressedMap.Length);

            // 'AVLD' VBI metadata is appended AFTER the map (chdman writes it once compression
            // finished), linked as the successor of the 'AVAV' entry, flags 0 (not hashed)
            if (ldFrameData != null)
            {
                long avldOffset = MetadataWriter.WriteCdMetadata(fs, [MetadataWriter.BuildAvLdMetadata(ldFrameData)]);
                var patchNext = new BigEndianWriter();
                patchNext.WriteU64((ulong)avldOffset);
                fs.Position = metaOffset + 8;
                fs.Write(patchNext.ToArray(), 0, 8);
            }

            // patch header fields (mapoffset@40, metaoffset@48, rawsha1@64, sha1@84)
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

        return new LaserDiscEncodingInfo(fpsTimes1million, width, height, interlaced, channels, rate,
            maxSamplesPerFrame, bytesPerFrame, hunkBytes, start, frames);
    }

    /// <summary>
    /// Reads and decodes one AVI frame into encode-ready pieces: planar native-endian audio
    /// (<paramref name="audioPlanes"/>, filled with this frame's sample count) and the
    /// YUY2-ordered field slice (<paramref name="fieldFrame"/>). Captures the packed VBI record
    /// when <paramref name="ldFrameData"/> is non-null. Returns the frame's sample count.
    /// </summary>
    private static int AssembleAvFrame(AviReader avi, ulong effFrame, int interlaceFactor, uint width, uint height,
        uint channels, uint rate, ulong fpsTimes1million, uint maxSamplesPerFrame,
        byte[] fullFrame, byte[] fieldFrame, short[][] audioPlanes, byte[]? ldFrameData, ulong frameIndex)
    {
        // determine effective frame number and first/last samples (chd_avi_compressor::read_data)
        ulong firstSample = rate > 0
            ? (rate * effFrame * 1000000 + fpsTimes1million - 1) / fpsTimes1million
            : 0;
        ulong endSample = rate > 0
            ? (rate * (effFrame + 1) * 1000000 + fpsTimes1million - 1) / fpsTimes1million
            : 0;
        int samples = (int)Math.Min(endSample - firstSample, maxSamplesPerFrame);

        // loop over channels and read the samples (silence-filled past the end of the stream)
        for (int ch = 0; ch < channels; ch++)
        {
            var plane = audioPlanes[ch];
            if (samples > 0)
            {
                try
                {
                    avi.ReadSoundSamples(ch, (uint)Math.Min(firstSample, uint.MaxValue), (uint)samples, plane);
                }
                catch (ArgumentOutOfRangeException)
                {
                    // beyond the end of the audio stream: silence
                    Array.Clear(plane, 0, samples);
                }
            }
            else
            {
                Array.Clear(plane, 0, plane.Length);
            }
        }

        // read the video data and slice the field for interlaced sources
        avi.ReadVideoFrame((uint)(effFrame / (ulong)interlaceFactor), fullFrame);
        int rowBytes = (int)width * 2;
        int fullStride = (int)width * interlaceFactor * 2;
        int srcRow = (int)(effFrame % (ulong)interlaceFactor) * rowBytes;
        for (int y = 0, src = srcRow, dst = 0; y < (int)height; y++, src += fullStride, dst += rowBytes)
        {
            Buffer.BlockCopy(fullFrame, src, fieldFrame, dst, rowBytes);
        }

        // update VBI metadata for this frame
        if (ldFrameData != null)
        {
            var vbi = VbiParse.ParseAll(fieldFrame, (int)width, (int)width, 8);
            VbiParse.MetadataPack(ldFrameData.AsSpan((int)(frameIndex * VbiParse.PackedBytes)), (uint)frameIndex, vbi);
        }

        return samples;
    }

    /// <summary>Computes the logical image length for a raw encode, honoring
    /// <see cref="ChdEncodeOptions.InputStartBytes"/> / <see cref="ChdEncodeOptions.InputLengthBytes"/>.</summary>
    private static ulong ComputeLogicalLength(Stream sourceStream, long startBytes, long? inputLength)
    {
        if (startBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceStream), "InputStartBytes must be >= 0");

        ulong total;
        if (sourceStream.CanSeek)
        {
            var length = sourceStream.Length;
            if (startBytes > length)
                throw new ArgumentException($"InputStartBytes ({startBytes}) exceeds the source length ({length})");

            total = (ulong)(length - startBytes);
        }
        else
        {
            total = inputLength is { } len
                ? (ulong)len
                : throw new ArgumentException(
                    "InputLengthBytes is required when encoding a non-seekable stream without a known length");
        }

        if (inputLength is { } lengthBytes)
        {
            if (lengthBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(sourceStream), "InputLengthBytes must be >= 0");

            total = Math.Min(total, (ulong)lengthBytes);
        }

        return total;
    }

    /// <summary>Builds a hunk reader for a raw stream: seekable sources are read by offset,
    /// non-seekable sources are drained sequentially (the pipeline reads hunks strictly in
    /// order on a single producer thread, so no rewinding is ever needed).</summary>
    private static Func<uint, byte[], int> CreateRawStreamReader(Stream source, long startBytes, ulong logicalBytes, uint hunkBytes, bool seekable)
    {
        if (seekable)
        {
            source.Position = startBytes;
            return (hunkIndex, buffer) => ReadRawHunk(source, hunkIndex, buffer, logicalBytes, hunkBytes);
        }

        var reader = new SequentialStreamReader(source, startBytes);
        return (hunkIndex, buffer) => reader.ReadHunk(hunkIndex, buffer, logicalBytes, hunkBytes);
    }

    /// <summary>Reads hunks sequentially from a non-seekable stream, skipping
    /// <see cref="ChdEncodeOptions.InputStartBytes"/> upfront.</summary>
    private sealed class SequentialStreamReader
    {
        private readonly Stream _source;
        private readonly long _skipBytes;
        private ulong _position;
        private bool _skipDone;

        internal SequentialStreamReader(Stream source, long skipBytes)
        {
            _source = source;
            _skipBytes = skipBytes;
        }

        internal int ReadHunk(uint hunkIndex, byte[] buffer, ulong logicalBytes, uint hunkBytes)
        {
            // The pipeline always asks in order; any other access pattern cannot be served
            // from a forward-only stream.
            var expected = _position / hunkBytes;
            if (hunkIndex != expected)
                throw new InvalidDataException(
                    $"Non-seekable source cannot rewind: pipeline requested hunk {hunkIndex}, stream is at {expected}");

            var valid = logicalBytes - (ulong)hunkIndex * hunkBytes;
            if ((long)valid <= 0)
                return 0;

            var count = (int)Math.Min(hunkBytes, valid);

            // Drain the skip prefix once, on the first hunk.
            if (!_skipDone)
            {
                _skipDone = true;
                var skip = _skipBytes;
                var skipBuf = new byte[Math.Min(skip, 256 * 1024)];
                while (skip > 0)
                {
                    var read = _source.Read(skipBuf, 0, (int)Math.Min(skip, skipBuf.Length));
                    if (read == 0)
                        throw new EndOfStreamException("Non-seekable source ended before InputStartBytes");

                    skip -= read;
                }
            }

            var total = 0;
            while (total < count)
            {
                var read = _source.Read(buffer, total, count - total);
                if (read == 0)
                    break;

                total += read;
            }

            _position += (ulong)count;
            return total;
        }
    }

    /// <summary>
    /// Detects an ISO-9660 filesystem image: the primary volume descriptor at sector 16
    /// (byte offset 0x8000 from the image start) starts with the "CD001" magic. Restores the
    /// stream position. Only seekable streams can be probed.
    /// </summary>
    private static bool IsIso9660Image(Stream sourceStream, long imageStart, ulong length)
    {
        if (!sourceStream.CanSeek || length < Iso9660PvdOffset + 5)
            return false;

        var original = sourceStream.Position;
        try
        {
            sourceStream.Position = imageStart + (long)Iso9660PvdOffset;
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
    /// <param name="codecTags">The codec tags to use, tried per hunk in order (default zlib;
    /// the single tag <see cref="CodecTags.None"/> produces an uncompressed CHD).</param>
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
    /// <param name="codecTags">The codec tags to use, tried per hunk in order (default zlib;
    /// the single tag <see cref="CodecTags.None"/> produces an uncompressed CHD).</param>
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

        codecTags ??= [CodecTags.Zlib];

        // 1. Parse the image descriptor (CUE, GDI, ISO or TOC)
        var toc = CdImageParser.Parse(cuePath);
        if (toc.Tracks.Count == 0)
            throw new InvalidDataException($"{Path.GetExtension(cuePath)} file contains no tracks");

        // 2. Pad each track to a 4-frame boundary and assign logical frame positions
        ulong totalFrames = 0;
        for (var i = 0; i < toc.Tracks.Count; i++)
        {
            var track = toc.Tracks[i];
            var extraFrames = (CdConstants.TrackPadding - track.Frames % CdConstants.TrackPadding) % CdConstants.TrackPadding;
            track.PaddedFrames = track.Frames + extraFrames;
            track.LogicalFrameStart = (long)totalFrames;
            totalFrames += (ulong)track.PaddedFrames;
            toc.Tracks[i] = track;
        }

        var logicalBytes = totalFrames * CdConstants.FrameSize;
        var framesPerHunk = (int)(hunkBytes / CdConstants.FrameSize);

        // 3. Build metadata entries (track entries + any user-supplied entries)
        var metadataEntries = MetadataWriter.BuildCdMetadataEntries(toc);
        if (options?.Metadata is { Count: > 0 } userMetadata)
            metadataEntries.AddRange(userMetadata);

        // 4. Parallel pipeline: the producer performs track-aware reads from the BIN file(s)
        // (only the producer thread touches the source files), workers compress, and the
        // single consumer writes blocks and map entries in hunk order
        var sourceFiles = new Dictionary<string, FileStream>(StringComparer.OrdinalIgnoreCase);
        try
        {
            EncodeCore(chdPath, hunkBytes, unitBytes, codecTags, options, logicalBytes, metadataEntries,
                (hunkIndex, buffer) => ReadCdHunk(hunkIndex, buffer, toc, framesPerHunk, totalFrames, sourceFiles),
                cancellationToken);
        }
        finally
        {
            foreach (var file in sourceFiles.Values)
                file.Dispose();
        }
    }

    /// <summary>
    /// Re-compresses an existing CHD file into a new CHD (chdman <c>copy</c> / CHDlite
    /// <c>ChdArchiver::copy</c> parity): every hunk of the source is read (through its parent
    /// when the source is a child) and re-encoded with the target codec list. All metadata
    /// entries of the source are cloned into the output. The output uses the source's hunk and
    /// unit sizes. Runs through the same parallel producer→worker→consumer pipeline as
    /// <see cref="EncodeRaw(Stream, string, uint, uint, IReadOnlyList{uint}?, ChdEncodeOptions?, System.Threading.CancellationToken)"/>,
    /// so output is byte-identical regardless of the worker count.
    /// </summary>
    /// <param name="sourcePath">Path of the source CHD file (V1-V5, standalone or child).</param>
    /// <param name="chdPath">Path of the output .chd file (created/overwritten).</param>
    /// <param name="codecTags">The codec tags for the output, tried per hunk in order (default
    /// zlib; the single tag <see cref="CodecTags.None"/> produces an uncompressed CHD).</param>
    /// <param name="options">Optional encoding configuration. <see cref="ChdEncodeOptions.SourceParentPath"/>
    /// supplies the parent of a child source; <see cref="ChdEncodeOptions.ParentPath"/> creates the
    /// output as a delta child of a different parent (chdman <c>-op</c>).</param>
    /// <param name="cancellationToken">Cancels the copy; <see cref="OperationCanceledException"/>
    /// is thrown when cancellation is requested.</param>
    /// <exception cref="IOException">The source (or its parent) cannot be opened.</exception>
    public static void Copy(string sourcePath, string chdPath, IReadOnlyList<uint>? codecTags = null,
        ChdEncodeOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourcePath);
        codecTags ??= [CodecTags.Zlib];
        options ??= new ChdEncodeOptions();

        var openErr = ChdFile.Open(sourcePath, options.SourceParentPath, out var source);
        if (openErr != ChdError.Chderrnone || source == null)
            throw new IOException($"Cannot open source CHD '{sourcePath}' ({openErr.GetMessage()} ({openErr}))");

        using (source)
        {
            var hunkBytes = source.HunkBytes;
            var unitBytes = source.UnitBytes;
            var logicalBytes = source.TotalBytes;

            // clone all metadata from the source (chdman copy parity)
            var metadataEntries = new List<MetadataEntry>();
            foreach (var m in source.Metadata)
                metadataEntries.Add(new MetadataEntry { Tag = MetadataWriter.TagFromString(m.Tag), Flags = m.Flags, Payload = m.Data });
            if (options.Metadata is { Count: > 0 } userMetadata)
                metadataEntries.AddRange(userMetadata);

            EncodeCore(chdPath, hunkBytes, unitBytes, codecTags, options, logicalBytes, metadataEntries,
                CreateSourceReader(source, logicalBytes, hunkBytes),
                cancellationToken);
        }
    }

    /// <summary>
    /// Wraps <see cref="ReadSourceHunk"/> for the compression pipeline. The delegate captures
    /// <paramref name="source"/> only as this method's parameter (never disposed here), so it
    /// stays valid for the synchronous pipeline run that the caller performs inside its
    /// <c>using (source)</c> block.
    /// </summary>
    private static Func<uint, byte[], int> CreateSourceReader(ChdFile source, ulong logicalBytes, uint hunkBytes)
    {
        return (hunkIndex, buffer) => ReadSourceHunk(source, hunkIndex, buffer, logicalBytes, hunkBytes);
    }

    /// <summary>
    /// Shared encoding core used by <see cref="EncodeRaw(Stream, string, uint, uint, IReadOnlyList{uint}?, ChdEncodeOptions?, System.Threading.CancellationToken)"/>,
    /// <see cref="EncodeCd"/> and <see cref="Copy"/>: writes the header, runs the parallel
    /// hunk pipeline over <paramref name="readHunk"/>, then writes metadata and the compressed
    /// map and patches the header hashes. The single tag <see cref="CodecTags.None"/> diverts to
    /// the uncompressed map writer (<see cref="EncodeUncompressed"/>).
    /// </summary>
    /// <param name="chdPath">Path of the output .chd file.</param>
    /// <param name="hunkBytes">Hunk size in bytes.</param>
    /// <param name="unitBytes">Unit size in bytes.</param>
    /// <param name="codecTags">The codec tags (never null).</param>
    /// <param name="options">Optional encoding configuration.</param>
    /// <param name="logicalBytes">The logical (uncompressed) image size in bytes.</param>
    /// <param name="metadataEntries">Metadata entries to write before the map.</param>
    /// <param name="readHunk">Reads hunk <c>hunkIndex</c> into <c>buffer</c> (exactly
    /// <c>hunkBytes</c> bytes; the tail of a partial final hunk must be zero-filled) and returns
    /// the number of valid bytes to fold into the raw SHA-1.</param>
    /// <param name="cancellationToken">Cancels the encode.</param>
    private static void EncodeCore(string chdPath, uint hunkBytes, uint unitBytes,
        IReadOnlyList<uint> codecTags, ChdEncodeOptions? options, ulong logicalBytes,
        IReadOnlyList<MetadataEntry> metadataEntries, Func<uint, byte[], int> readHunk,
        CancellationToken cancellationToken)
    {
        if (codecTags is [CodecTags.None])
        {
            EncodeUncompressed(chdPath, hunkBytes, unitBytes, options, logicalBytes, metadataEntries, readHunk, cancellationToken);
            return;
        }

        var codecs = ChdCodecs.CreateAll(codecTags, hunkBytes);

        var hunkCount = (uint)((logicalBytes + hunkBytes - 1) / hunkBytes);
        if (hunkCount == 0)
        {
            hunkCount = 1;
        }

        var entries = new MapEntry[hunkCount];
        using var sha1 = new Sha1();
        var selfMap = new Dictionary<string, uint>((int)hunkCount, StringComparer.Ordinal);
        using var parentMap = options?.ParentPath is { Length: > 0 } parentPath
            ? new ParentMap(parentPath, hunkBytes, unitBytes)
            : null;
        var processor = new HunkProcessor(hunkBytes, codecTags, options?.TaskCount ?? Chd.TaskCount);

        using var fs = new FileStream(chdPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        var header = ChdHeaderV5.CreateRaw(codecTags.ToArray(), logicalBytes, hunkBytes, unitBytes);
        if (parentMap != null)
        {
            header.ParentSha1 = parentMap.ParentSha1;
        }

        header.WriteToStream(fs);

        // Metadata lives right after the header, before the compressed hunk data — chdman
        // appends metadata via file_append() before compressing any hunks, so byte parity
        // requires this order (the header's metaoffset is patched below).
        long? metaOffset = null;
        if (metadataEntries.Count > 0)
        {
            metaOffset = MetadataWriter.WriteCdMetadata(fs, metadataEntries);
        }

        var currentOffset = RunCompressionPipeline(processor, hunkCount, readHunk, sha1, entries, selfMap, fs,
            codecs, options, hunkBytes, parentMap, cancellationToken);

        var rawSha1 = sha1.Finish();

        var compressedMap = MapCompressor.Compress(entries, hunkCount, hunkBytes, unitBytes);
        var mapOffset = (ulong)currentOffset;

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
    /// Writes an uncompressed CHD (<c>-c none</c>) with chdman's exact layout: header with
    /// mapoffset at 124 (right after the header), the V5 raw map (one big-endian u32 hunk index
    /// per hunk; 0 = not stored, reads as zeroes or from the parent), metadata between the map
    /// and the data, and each non-zero hunk stored raw at a hunk-aligned offset in hunk order.
    /// All-zero hunks are not stored. Like chdman, no SHA-1 is written for uncompressed CHDs
    /// (there is nothing to verify); the header hash fields stay zero.
    /// </summary>
    private static void EncodeUncompressed(string chdPath, uint hunkBytes, uint unitBytes,
        ChdEncodeOptions? options, ulong logicalBytes, IReadOnlyList<MetadataEntry> metadataEntries,
        Func<uint, byte[], int> readHunk, CancellationToken cancellationToken)
    {
        var hunkCount = (uint)((logicalBytes + hunkBytes - 1) / hunkBytes);
        if (hunkCount == 0)
        {
            hunkCount = 1;
        }

        ChdFile? parent = null;
        if (options?.ParentPath is { Length: > 0 } parentPath)
        {
            var perr = ChdFile.Open(parentPath, out parent);
            if (perr != ChdError.Chderrnone || parent == null)
                throw new IOException($"Unable to open parent CHD '{parentPath}' ({perr.GetMessage()} ({perr}))");

            if (parent.HunkBytes != hunkBytes || parent.UnitBytes != unitBytes)
            {
                parent.Dispose();
                throw new ArgumentException(
                    $"Parent CHD hunk/unit size mismatch: parent is {parent.HunkBytes}/{parent.UnitBytes} bytes, " +
                    $"requested {hunkBytes}/{unitBytes} bytes.");
            }
        }

        using var fs = new FileStream(chdPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using (parent)
        {
            var header = ChdHeaderV5.CreateRaw(new[] { CodecTags.None }, logicalBytes, hunkBytes, unitBytes);
            if (parent != null)
            {
                header.ParentSha1 = parent.Sha1;
            }

            header.WriteToStream(fs);

            // the raw map lives right after the header (mapoffset = 124): one big-endian
            // u32 per hunk holding the hunk index of the stored data (offset / hunkBytes);
            // entry 0 means "not stored" (zero-fill, or the parent's same-index hunk)
            var map = new byte[hunkCount * 4];
            fs.Write(map, 0, map.Length);

            // metadata between the map and the data (chdman writes metadata before compression)
            long? metaOffset = null;
            if (metadataEntries.Count > 0)
            {
                metaOffset = MetadataWriter.WriteCdMetadata(fs, metadataEntries);
            }

            var buffer = new byte[hunkBytes];
            for (uint h = 0; h < hunkCount; h++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Array.Clear(buffer, 0, buffer.Length);
                readHunk(h, buffer);

                // all-zero hunks are not stored (entry stays 0)
                if (buffer.AsSpan().IndexOfAnyExcept((byte)0) < 0)
                {
                    ReportNoneHunkProgress(options, h, hunkCount, hunkBytes, 0);
                    continue;
                }

                // align the append to a hunk boundary and compute the hunk index
                var aligned = (fs.Position + hunkBytes - 1) / hunkBytes * hunkBytes;
                if (aligned != fs.Position)
                {
                    fs.Position = aligned;
                }

                var entry = (uint)(fs.Position / hunkBytes);
                map[h * 4] = (byte)(entry >> 24);
                map[h * 4 + 1] = (byte)(entry >> 16);
                map[h * 4 + 2] = (byte)(entry >> 8);
                map[h * 4 + 3] = (byte)entry;

                fs.Write(buffer, 0, buffer.Length);
                ReportNoneHunkProgress(options, h, hunkCount, hunkBytes, (int)hunkBytes);
            }

            // write the map back at its offset (124)
            fs.Position = ChdHeaderV5.Length;
            fs.Write(map, 0, map.Length);

            // Patch header: metaoffset at byte 48 (mapoffset is already 124 from CreateRaw;
            // rawsha1/sha1 stay zero, exactly like chdman's uncompressed output)
            if (metaOffset.HasValue)
            {
                var patchW = new BigEndianWriter();
                patchW.WriteU64((ulong)metaOffset.Value);
                fs.Position = 48;
                fs.Write(patchW.ToArray(), 0, 8);
            }
        }
    }

    /// <summary>Reports per-hunk progress for the uncompressed encode path.</summary>
    private static void ReportNoneHunkProgress(ChdEncodeOptions? options, uint hunkIndex, uint hunkCount, uint hunkBytes, int storedBytes)
    {
        if (options?.HunkCompleted is not { } callback)
            return;

        callback(new HunkProgress(hunkIndex, hunkCount, (int)hunkBytes, storedBytes,
            MapEntry.CompressionNone, "none", storedBytes / (double)hunkBytes));
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

    /// <summary>Reads hunk <paramref name="hunkIndex"/> from a source CHD file; returns the number
    /// of valid bytes (the final partial hunk of the source is padded to a full hunk in the file,
    /// but only its real bytes are folded into the raw SHA-1).</summary>
    private static int ReadSourceHunk(ChdFile source, uint hunkIndex, byte[] buffer, ulong logicalBytes, uint hunkBytes)
    {
        var err = source.ReadHunk(hunkIndex, buffer);
        if (err != ChdError.Chderrnone)
            throw new InvalidDataException($"Failed to read hunk {hunkIndex} from source CHD: {err.GetMessage()} ({err})");

        var valid = logicalBytes - (ulong)hunkIndex * hunkBytes;
        return (int)Math.Min(hunkBytes, valid);
    }

    /// <summary>Reads hunk <paramref name="hunkIndex"/> of a CD image: track-aware reads from the
    /// BIN/WAV file(s), zero-filled padding frames, and little-endian→big-endian audio swapping.
    /// The full padded buffer is what gets compressed and CRC-16'd; only the valid frames (all
    /// of them except the last hunk's excess zero padding past <paramref name="totalFrames"/>)
    /// are folded into the raw SHA-1, matching chdman.</summary>
    private static int ReadCdHunk(uint hunkIndex, byte[] buffer, CdToc toc, int framesPerHunk, ulong totalFrames,
        Dictionary<string, FileStream> files)
    {
        var hunkStartFrame = hunkIndex * framesPerHunk;
        for (var f = 0; f < framesPerHunk; f++)
        {
            var frame = hunkStartFrame + f;
            if (frame >= (long)totalFrames)
                break;

            var track = FindTrackContainingFrame(toc, frame);
            var frameInTrack = (int)(frame - track.LogicalFrameStart);

            // frames past the track's data and GDI gap (pad) frames are zero-filled
            if (frameInTrack >= track.Frames)
                continue;
            if (track.PadFrames > 0 && frameInTrack >= track.Frames - track.PadFrames)
                continue;

            // the BIN file stores datasize+subsize bytes per sector (no subcode → 2352);
            // the remainder of the 2448-byte CHD frame stays zero-filled. MAME's physical read
            // path reads every track sector (pregap included) at the track's file offset
            // (cdrom.cpp read_partial_sector, phys=true), so the pregap frames are read from
            // the file exactly like data frames.
            var binFrameSize = track.DataSize + track.SubSize;
            var sourceOffset = track.FileOffset + (long)frameInTrack * binFrameSize;
            var file = GetSourceFile(files, track.FileName!);
            file.Position = sourceOffset;
            var bytesRead = file.Read(buffer, f * CdConstants.FrameSize, binFrameSize);
            if (bytesRead != binFrameSize)
                throw new InvalidDataException($"Unexpected end of file [{track.FileName}]");

            // audio sectors are little-endian in BIN files; swap to big-endian for CHD
            if (track.Swap)
                SwapPairs(buffer, f * CdConstants.FrameSize, track.DataSize);
        }

        // hash only the valid frames; the last hunk's excess zero padding is stored (and
        // CRC-16'd) but must not be folded into the raw SHA-1, exactly like ReadSourceHunk
        var validFrames = (long)totalFrames - hunkStartFrame;
        if (validFrames > framesPerHunk)
        {
            validFrames = framesPerHunk;
        }

        if (validFrames < 0)
        {
            validFrames = 0;
        }

        return (int)(validFrames * CdConstants.FrameSize);
    }

    /// <summary>
    /// Runs the compression pipeline for one encode. The consumer callback appends compressed
    /// blocks to <paramref name="fs"/> in hunk order; offsets and the dedup map advance in the
    /// same order, so the output is byte-identical to the sequential path.
    /// </summary>
    /// <returns>The byte offset just past the last compressed block (the map's base offset).</returns>
    /// <remarks><paramref name="fs"/> and <paramref name="parentMap"/> are owned by the caller and
    /// disposed only after this method returns (<see cref="HunkProcessor.CompressAll"/> is
    /// synchronous), so the consumer closure never outlives them.</remarks>
    private static long RunCompressionPipeline(HunkProcessor processor, uint hunkCount,
        Func<uint, byte[], int> readHunk, Sha1 sha1, MapEntry[] entries, Dictionary<string, uint> selfMap,
        Stream fs, IReadOnlyList<IChdCodec> codecs, ChdEncodeOptions? options, uint hunkBytes,
        ParentMap? parentMap, CancellationToken cancellationToken)
    {
        var currentOffset = fs.Position;
        processor.CompressAll(
            hunkCount,
            readHunk,
            sha1,
            result => ConsumeHunk(result, entries, selfMap, fs, ref currentOffset, codecs, options, hunkCount, hunkBytes, parentMap),
            cancellationToken);
        return currentOffset;
    }

    /// <summary>
    /// Single-consumer hunk sink, invoked by the pipeline in hunk order: performs SELF-dedup
    /// (the map is only ever updated with already-consumed hunks, so references never chain),
    /// then parent-hunk dedup against <paramref name="parentMap"/> (chdman priority: a hunk
    /// found in the same image is a SELF reference; otherwise a matching parent unit becomes
    /// a PARENT reference), assigns the sequential file offset, appends the block to the
    /// output, and reports progress.
    /// </summary>
    private static void ConsumeHunk(HunkResult result, MapEntry[] entries, Dictionary<string, uint> selfMap,
        Stream output, ref long currentOffset, IReadOnlyList<IChdCodec> codecs, ChdEncodeOptions? options,
        uint hunkCount, uint hunkBytes, ParentMap? parentMap)
    {
        var sha1Hex = Convert.ToHexString(result.Sha1);
        MapEntry entry;
        var data = result.Data;
        if (selfMap.TryGetValue(sha1Hex, out var sourceHunk))
        {
            entry = new MapEntry
            {
                Compression = MapEntry.CompressionSelf,
                CompLength = 0,
                Offset = sourceHunk,
                Crc16 = 0
            };
            data = null;
        }
        else if (parentMap != null && parentMap.TryGetParentUnit(result.Crc16, sha1Hex, out var parentUnit))
        {
            // the parent reference stores the matching unit index (0-based in units), which
            // the reader resolves against the parent; nothing is appended to this file
            entry = new MapEntry
            {
                Compression = MapEntry.CompressionParent,
                CompLength = 0,
                Offset = parentUnit,
                Crc16 = 0
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
                Crc16 = result.Crc16
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
        for (var i = 0; i < length; i += 2)
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
            case MapEntry.CompressionNone:
                storedBytes = (int)hunkBytes;
                codecName = "none";
                break;
            case MapEntry.CompressionSelf:
                storedBytes = 0;
                codecName = "self";
                break;
            case MapEntry.CompressionParent:
                storedBytes = 0;
                codecName = "parent";
                break;
            default:
                storedBytes = (int)entry.CompLength;
                codecName = entry.Compression < codecs.Count
                    ? CodecTags.ToString(codecs[entry.Compression].Tag)
                    : "?";
                break;
        }

        callback(new HunkProgress(hunkIndex, hunkCount, (int)hunkBytes, storedBytes, entry.Compression,
            codecName, storedBytes / (double)hunkBytes));
    }
}